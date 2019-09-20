using P2PQuakeClient.Connections;
using P2PQuakeClient.PacketData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace P2PQuakeClient
{
	public class EpspClient
	{
		public EpspClient(IEpspLogger logger, string[] serverHosts, int areaCode, ushort listenPort, int maxConnectablePeerCount)
		{
			MaxConnectablePeerCount = maxConnectablePeerCount;
			AreaCode = areaCode;
			ListenPort = listenPort;
			ServerHosts = serverHosts ?? throw new ArgumentNullException(nameof(serverHosts));
			Logger = logger ?? throw new ArgumentNullException(nameof(logger));

			ClientInfo = new ClientInformation("0.34", "P2PQuakeClient@ingen084", Assembly.GetEntryAssembly().GetName().Version.ToString()
#if DEBUG
				+ "_DEBUG"
#endif
				);
			PeerController = new PeerController(this);
		}

		/// <summary>
		/// データを受信した
		/// <para>bool: ライブラリ側で検証済みかどうか</param>
		/// <para>int: コード</param>
		/// <para>string[]: パケットのデータ 鍵も含みます</para>
		/// </summary>
		public event Action<bool, EpspPacket> DataReceived;
		internal void OnDataReceived(bool validated, EpspPacket packet)
			=> Task.Run(() => DataReceived?.Invoke(validated, packet));

		internal PeerController PeerController { get; }
		internal IEpspLogger Logger { get; }
		private readonly string[] ServerHosts;
		public bool IsNetworkJoined { get; private set; }

		public ClientInformation ClientInfo { get; set; }

		public int AreaCode { get; }
		public int MaxConnectablePeerCount { get; }
		public int MinimumKeepPeerCount { get; set; } = 5;
		public ushort ListenPort { get; }
		private Task ListenerTask { get; set; }

		public int PeerId { get; private set; }
		public RsaKey RsaKey { get; private set; }
		public bool IsPortForwarded { get; private set; }
		public TimeSpan ProtocolTimeOffset { get; private set; }
		public DateTime ProtocolTime => DateTime.Now + ProtocolTimeOffset;

		private TcpListener TcpListener { get; set; }

		private async Task<ServerConnection> ConnectServerAndHandshakeAsync()
		{
			var hosts = ServerHosts.OrderBy(h => Guid.NewGuid()); // ランダマイズ

			ServerConnection server = null;
			foreach (var host in hosts)
			{
				Logger.Info($"{host} に接続中…");
				server = new ServerConnection(host, 6910);
				try
				{
					await server.ConnectAndWaitClientInfoRequest();
					if (server.IsConnected)
					{
						var serverInfo = await server.SendClientInformation(ClientInfo);
						Logger.Info($"接続しました。 {serverInfo.SoftwareName}-{serverInfo.SoftwareVersion} (v{serverInfo.ProtocolVersion})");
						return server;
					}
				}
				catch (Exception ex) when (ex is EpspException || ex is SocketException || ex is AggregateException)
				{
					Logger.Info($"サーバーへの接続に失敗しました。 {ex.Message}");
				}
				Logger.Info("接続に失敗しました。");
				server.Dispose();
			}
			Logger.Error($"すべてのサーバーに接続できませんでした。");
			return null;
		}

		private void Listener()
		{
			TcpListener = new TcpListener(IPAddress.Any, ListenPort);
			TcpListener.Start();
			Logger.Info($"ポート{ListenPort} でListenを開始しました。");
			try
			{
				while (true)
				{
					var client = TcpListener.AcceptTcpClient();
					Logger.Debug("着信接続: " + (client.Client.RemoteEndPoint as IPEndPoint).Address);

					// とりあえず接続中におなじIPがあれば即時切断
					if (PeerController.CheckDuplicatePeer((client.Client.RemoteEndPoint as IPEndPoint).Address))
					{
						client.Close();
						client.Dispose();
						continue;
					}

					Task.Run(async () =>
					{
						Logger.Debug((client.Client.RemoteEndPoint as IPEndPoint).Address + " ピア登録開始");
						if (!await PeerController.AddPeer(new EpspPeer(this, client)))
						{
							Logger.Debug((client.Client.RemoteEndPoint as IPEndPoint).Address + " ピア登録失敗");
							client.Close();
							client.Dispose();
							return;
						}
						Logger.Debug((client.Client.RemoteEndPoint as IPEndPoint).Address + " ピア登録成功");
					});
				}
			}
			catch (Exception ex)
			{
				Logger.Warning($"Listenスレッドで例外発生: {ex}");
			}
			Logger.Info("Listenを終了しました。");
		}
		public async Task<bool> JoinNetworkAsync()
		{
			if (IsNetworkJoined)
			{
				Logger.Error("すでにネットワークに参加済みだったため、参加処理はキャンセルされました。");
				return true;
			}
			Logger.Info("ネットワークに参加しています。");

			ListenerTask = new Task(Listener, CancellationToken.None, TaskCreationOptions.LongRunning);
			ListenerTask.Start();

			var server = await ConnectServerAndHandshakeAsync();
			if (server == null)
			{
				Logger.Info("サーバーに接続できないためネットワークに参加できませんでした。");
				return false;
			}
			PeerId = await server.GetTemporaryPeerId();
			Logger.Info($"仮ピアIDが割り当てられました: {PeerId}");
			Logger.Debug($"ポート開放チェックをしています…");
			IsPortForwarded = await server.CheckPortForwarding(PeerId, 6911);
			Logger.Info($"ポートは開放されていま{(IsPortForwarded ? "" : "せんで")}した。");

			Logger.Info("ピアに接続しています。");
			await GetAndConnectPeerAsync(server);

			// MEMO エコーに失敗するなと思ってパケット覗いてみたらずっと仮ピアIDが使用されていた
			var mainPeerId = await server.GetPeerId(PeerId, 6911, AreaCode, PeerController.Count, MaxConnectablePeerCount);
			Logger.Info($"本ピアIDを取得しました: {mainPeerId} 使用しません。");
			IsNetworkJoined = true;
			if ((RsaKey = await server.GetRsaKey(PeerId)) == null)
				Logger.Warning("鍵の取得に失敗しました。");
			else
				Logger.Info("鍵を取得しました。");

			await server.GetRegionalPeersCount();

			await GetAndCalcProtocolTimeAsync(server);
			Logger.Debug("PC時刻との時差: " + ProtocolTimeOffset);

			await server.SafeDisconnect();
			server.Dispose();

			Logger.Info("ネットワークへの参加が完了しました。");

			return true;
		}

		public async Task<bool> EchoAsync()
		{
			if (!IsNetworkJoined)
			{
				Logger.Error("ネットワークに参加していないため、エコーができませんでした。");
				return false;
			}
			Logger.Info("サーバーにエコーを送っています。");
			var server = await ConnectServerAndHandshakeAsync();
			if (server == null)
			{
				Logger.Info("サーバーに接続できないためエコーを送ることができませんでした。");
				return false;
			}
			if (!await server.SendEcho(PeerId, PeerController.Count))
			{
				Logger.Info("IPアドレスが変化していたため、エコーに失敗しました。");
				server.Dispose();
				return false;
			}
			if (((RsaKey?.Expiration ?? DateTime.Now) - DateTime.Now) < TimeSpan.FromMinutes(20))
			{
				Logger.Info("鍵の再取得を行います。");
				var key = await server.UpdateRsaKey(PeerId, RsaKey?.PrivateKey);
				if (key == null)
					Logger.Warning("鍵の再取得に失敗しました。");
				else
					RsaKey = key;
			}

			if (PeerController.Count < MinimumKeepPeerCount)
				await GetAndConnectPeerAsync(server);
			await GetAndCalcProtocolTimeAsync(server);
			await server.SafeDisconnect();
			server.Dispose();

			Logger.Info("エコーが完了しました。");
			return true;
		}

		public async Task LeaveNetworkAsync()
		{
			if (!IsNetworkJoined)
			{
				Logger.Error("ネットワークに参加していないため、離脱ができませんでした。");
				return;
			}
			Logger.Info("ネットワークから離脱しています。");

			PeerController.DisconnectAllPeers();

			var server = await ConnectServerAndHandshakeAsync();
			if (server == null)
			{
				Logger.Info("サーバーに接続できないためネットワークから離脱した扱いになります。");
				IsNetworkJoined = false;
				return;
			}

			await server.LeaveNetworkRequest(PeerId, RsaKey?.PrivateKey);

			IsNetworkJoined = false;
			await server.SafeDisconnect();
			server.Dispose();
			Logger.Info("ネットワークから離脱しました。");
			return;
		}

		private async Task GetAndCalcProtocolTimeAsync(ServerConnection server)
		{
			ProtocolTimeOffset = await server.GetProtocolTime() - DateTime.Now;
		}

		private async Task GetAndConnectPeerAsync(ServerConnection server)
		{
			var connectedPeers = new List<EpspPeer>();
			await Task.WhenAll((await server.GetPeerInformations(PeerId)).Where(p => !PeerController.CheckDuplicatePeer(p.Id)).Select(async peerInfo =>
			 {
				 var peer = new EpspPeer(this, peerInfo);
				 if (!await PeerController.AddPeer(peer))
				 {
					 peer.Dispose();
					 return;
				 }
				 lock (connectedPeers)
					 connectedPeers.Add(peer);
			 }));
			await server.NoticeConnectedPeerIds(connectedPeers.Select(p => p.PeerId).ToArray());
		}
	}
}
