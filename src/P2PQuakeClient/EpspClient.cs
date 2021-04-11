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
		public EpspClient(IEpspLogger logger, string[] serverHosts, int areaCode, ushort? listenPort = null, int maxConnectablePeerCount = 5)
		{
			MaxConnectablePeerCount = maxConnectablePeerCount;
			AreaCode = areaCode;
			ListenPort = listenPort;
			ServerHosts = serverHosts ?? throw new ArgumentNullException(nameof(serverHosts));
			Logger = logger ?? throw new ArgumentNullException(nameof(logger));

			ClientInfo = new ClientInformation("0.34", "ingenP2PQTest", "alpha1");
			PeerController = new PeerController(this);
			PeerController.PeerCountChanged += () => StateUpdated?.Invoke();
		}

		/// <summary>
		/// データを受信した
		/// <para>bool: 署名検証済みかどうか</param>
		/// <para>EpspPacket: パケットのデータ 鍵も含みます</para>
		/// </summary>
		public event Action<bool, EpspPacket>? DataReceived;
		internal void OnDataReceived(bool validated, EpspPacket packet)
			=> Task.Run(() => DataReceived?.Invoke(validated, packet));

		internal PeerController PeerController { get; }
		internal IEpspLogger Logger { get; }
		private readonly string[] ServerHosts;

		private bool isNetworkJoined = false;
		public bool IsNetworkJoined
		{
			get => isNetworkJoined;
			private set
			{
				if (isNetworkJoined == value)
					return;
				isNetworkJoined = value;
				StateUpdated?.Invoke();
			}
		}

		// TODO: PropertyCahngedのほうがよかったな
		public event Action? StateUpdated;
		public int PeerCount => PeerController.Count;

		private int totalNetworkPeerCount = 0;
		public int TotalNetworkPeerCount
		{
			get => totalNetworkPeerCount;
			internal set
			{
				if (totalNetworkPeerCount == value)
					return;
				totalNetworkPeerCount = value;
				StateUpdated?.Invoke();
			}
		}

		public ClientInformation ClientInfo { get; set; }

		public int AreaCode { get; }
		public int MaxConnectablePeerCount { get; }
		public int MinimumKeepPeerCount { get; set; } = 5;
		public ushort? ListenPort { get; }
		private Thread? ListenerThread { get; set; }

		public int PeerId { get; private set; }
		private RsaKey? rsaKey;
		public RsaKey? RsaKey
		{
			get => rsaKey;
			private set
			{
				if (rsaKey == value)
					return;
				rsaKey = value;
				StateUpdated?.Invoke();
			}
		}

		public bool IsPortForwarded { get; private set; }

		public TimeSpan ProtocolTimeOffset { get; private set; }
		public DateTime ProtocolTime => DateTime.Now + ProtocolTimeOffset;

		private TcpListener? TcpListener { get; set; }
		private ManualResetEventSlim ListenerMre { get; } = new(false);

		private async Task<ServerConnection?> ConnectServerAndHandshakeAsync()
		{
			var hosts = ServerHosts.OrderBy(h => Guid.NewGuid()); // ランダマイズ

			ServerConnection? server = null;
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
			if (ListenPort is not ushort listenPort)
			{
				Logger.Info($"ポートのListenは無効化されています");
				return;
			}
			TcpListener = new TcpListener(IPAddress.Any, listenPort);
			TcpListener.Start();
			Logger.Info($"ポート{ListenPort} でListenを開始しました。");
			ListenerMre.Set();
			try
			{
				while (true)
				{
					var client = TcpListener.AcceptTcpClient();
					// エンドポイントが取得できない場合そのまま戻る
					if (client.Client.RemoteEndPoint is not IPEndPoint ipe)
					{
						client.Close();
						client.Dispose();
						continue;
					}
					Logger.Debug("Accepted: " + ipe.Address);

					// とりあえず接続中におなじIPがあれば即時切断
					if (PeerController.CheckDuplicatePeer(ipe.Address))
					{
						client.Close();
						client.Dispose();
						continue;
					}

					Task.Run(async () =>
					{
						Logger.Debug(ipe.Address + " ピア登録開始");
						if (!await PeerController.AddPeer(new EpspPeer(this, client)))
						{
							Logger.Debug(ipe.Address + " ピア登録失敗");
							client.Close();
							client.Dispose();
							return;
						}
						Logger.Debug(ipe.Address + " ピア登録成功");
					});
				}
			}
			catch (Exception ex)
			{
				Logger.Warning($"Listenスレッドで例外発生: {ex}");
			}
			Logger.Info("Listenを終了しました。");
		}
		/// <summary>
		/// ネットワークに参加する
		/// </summary>
		/// <returns>参加できているか</returns>
		public async Task<bool> JoinNetworkAsync()
		{
			if (IsNetworkJoined)
			{
				Logger.Error("すでにネットワークに参加済みだったため、参加処理はキャンセルされました。");
				return true;
			}
			Logger.Info("ネットワークに参加しています。");

			var server = await ConnectServerAndHandshakeAsync();
			if (server == null)
			{
				Logger.Info("サーバーに接続できないためネットワークに参加できませんでした。");
				return false;
			}
			PeerId = await server.GetTemporaryPeerId();
			Logger.Info($"仮ピアIDが割り当てられました: {PeerId}");


			if (ListenPort is ushort listenPort)
			{
				ListenerThread = new Thread(Listener);
				ListenerThread.Start();
				ListenerMre.Wait();
				Logger.Debug($"ポート開放チェックをしています…");
				IsPortForwarded = await server.CheckPortForwarding(PeerId, listenPort);
				Logger.Info($"ポートは開放されていま{(IsPortForwarded ? "" : "せんで")}した。");
			}

			Logger.Info("ピアに接続しています。");
			await GetAndConnectPeerAsync(server);

			TotalNetworkPeerCount = await server.RegistPeerInfo(PeerId, ListenPort ?? 6911, AreaCode, PeerController.Count, MaxConnectablePeerCount);
			Logger.Info($"本ピアIDとして登録しました。 総参加数: {TotalNetworkPeerCount}");
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

		/// <summary>
		/// サーバーにエコーを送信する
		/// </summary>
		/// <returns>成功したか</returns>
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

		/// <summary>
		/// 地震感知情報を送信する
		/// </summary>
		public async Task SendEarthquakeDetectedMessageAsync()
		{
			if (!IsNetworkJoined)
			{
				Logger.Error("ネットワークに参加していないため、地震感知情報の送信ができませんでした。");
				return;
			}
			Logger.Info("地震感知情報を送信しています。");

			EpspPacket packet;
			var content = (DateTime.Now.Ticks / (PeerId != 0 ? PeerId : 983)) + "," + AreaCode;
			if (RsaKey == null)
				packet = new EpspPacket(555, 1, "", ProtocolTime.AddMinutes(10).ToString("yyyy/MM/dd HH-mm-ss"), "", "", content);
			else
			{
				var signedData = RsaCryptoService.SignPeer(content, RsaKey, ProtocolTime);
				packet = new EpspPacket(555, 1, signedData.ToPacketData());
			}
			await PeerController.SendPacketAllClientAsync(packet);
		}

		/// <summary>
		/// ネットワークから離脱する
		/// </summary>
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
			await server.NoticeConnectedPeerIds(connectedPeers.Select(p => p.Id).ToArray());
		}
	}
}
