using P2PQuakeClient.Connections;
using P2PQuakeClient.PacketData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace P2PQuakeClient
{
	public class EpspClient
	{
		public EpspClient(IEpspLogger logger, string[] serverHosts, int areaCode, ushort listenPort)
		{
			AreaCode = areaCode;
			ListenPort = listenPort;
			ServerHosts = serverHosts ?? throw new ArgumentNullException(nameof(serverHosts));
			Logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		internal IEpspLogger Logger { get; }
		private readonly string[] ServerHosts;
		public bool IsNetworkJoined { get; private set; }

		public ClientInformation ClientInfo { get; set; } = new ClientInformation("0.34", "P2PQuakeClient@ingen084", "sandbox");

		public List<EpspPeer> Peers { get; } = new List<EpspPeer>();

		public int AreaCode { get; }
		public ushort ListenPort { get; }
		private Task ListenerTask { get; set; }

		public int PeerId { get; private set; }
		public RsaKey RsaKey { get; private set; }
		public bool IsPortForwarded { get; private set; }
		public TimeSpan ProtocolTimeOffset { get; private set; }
		public DateTime ProtocolTime => DateTime.Now + ProtocolTimeOffset;

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
						Logger.Info($"接続しました。 {serverInfo.SoftwareName} - {serverInfo.SoftwareVersion} (v{serverInfo.ProtocolVersion})");
						return server;
					}
				}
				catch (EpspException ex)
				{
					Logger.Info($"サーバーへの接続中に例外が発生しました。\n{ex}");
				}
				Logger.Info("接続に失敗しました。");
				server.Dispose();
			}
			Logger.Error($"すべてのサーバーに接続できませんでした。");
			return null;
		}

		private void Listener()
		{
			var listener = new TcpListener(IPAddress.Any, ListenPort);
			listener.Start();
			Logger.Info($"ポート{ListenPort} でListenを開始しました。");
			while (true)
			{
				var client = listener.AcceptTcpClient();
				Logger.Debug("着信接続: " + (client.Client.RemoteEndPoint as IPEndPoint).Address);
				var found = false;

				// とりあえず接続中におなじIPがあれば即時切断
				lock (Peers)
					found = Peers.Any(p => (p.Connection.TcpClient.Client.RemoteEndPoint as IPEndPoint).Address == (client.Client.RemoteEndPoint as IPEndPoint).Address);
				if (found)
				{
					client.Close();
					continue;
				}

				Task.Run(async () =>
				{
					Logger.Debug((client.Client.RemoteEndPoint as IPEndPoint).Address + " ピア登録開始");
					if (!await AddPeer(new EpspPeer(this, client)))
					{
						Logger.Debug((client.Client.RemoteEndPoint as IPEndPoint).Address + " ピア登録失敗");
						client.Close();
						return;
					}
					Logger.Debug((client.Client.RemoteEndPoint as IPEndPoint).Address + " ピア登録成功");
				});
			}
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
			Logger.Info($"仮ピアIDが割り当てられました。 {PeerId}");
			Logger.Debug($"ポート開放チェックをしています…");
			IsPortForwarded = await server.CheckPortForwarding(PeerId, 6911);
			Logger.Info($"ポートは開放されていま{(IsPortForwarded ? "" : "せんで")}した。");

			Logger.Info("ピアに接続しています。");
			await GetAndConnectPeerAsync(server);

			Logger.Info("本ピアIDを取得しています。");
			PeerId = await server.GetPeerId(PeerId, 6911, 901, Peers.Count, 10);
			IsNetworkJoined = true;
			Logger.Info("鍵を取得しています。");
			if ((RsaKey = await server.GetRsaKey(PeerId)) == null)
				Logger.Warning("鍵の取得に失敗しました。");
			await server.GetRegionalPeersCount();
			await GetAndCalcProtocolTimeAsync(server);
			await server.SafeDisconnect();
			server.Dispose();
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
			if (!await server.SendEcho(PeerId, Peers.Count))
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

			await GetAndConnectPeerAsync(server);
			await GetAndCalcProtocolTimeAsync(server);
			await server.SafeDisconnect();
			server.Dispose();
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

			var server = await ConnectServerAndHandshakeAsync();
			if (server == null)
			{
				Logger.Info("サーバーに接続できないためネットワークから離脱した扱いになります。");
				IsNetworkJoined = false;
				return;
			}
		}

		private async Task GetAndCalcProtocolTimeAsync(ServerConnection server)
		{
			ProtocolTimeOffset = await server.GetProtocolTime() - DateTime.Now;
		}

		private async Task GetAndConnectPeerAsync(ServerConnection server)
		{
			var connectedPeers = new List<EpspPeer>();
			await Task.WhenAll((await server.GetPeerInformations(PeerId)).Where(p => !Peers.Any(p2 => p2.PeerId == p.Id)).Select(async peerInfo =>
			 {
				 var peer = new EpspPeer(this, peerInfo);
				 if (!await AddPeer(peer))
				 {
					 peer.Dispose();
					 return;
				 }
				 lock (connectedPeers)
					 connectedPeers.Add(peer);
			 }));
			await server.NoticeConnectedPeerIds(connectedPeers.Select(p => p.PeerId).ToArray());
		}

		public async Task<bool> AddPeer(EpspPeer peer)
		{
			if (await peer.ConnectAndHandshakeAsync())
			{
				//TODO イベントハンドラの設定
				peer.Connection.Disconnected += () =>
				{
					lock (Peers)
						Peers.Remove(peer);
				};
				lock (Peers)
					Peers.Add(peer);
				return true;
			}
			return false;
		}
	}
}
