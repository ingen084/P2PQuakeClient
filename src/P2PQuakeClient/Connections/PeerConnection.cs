using P2PQuakeClient.PacketData;
using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Timers;

namespace P2PQuakeClient.Connections
{
	public class PeerConnection : EpspConnection
	{
		public PeerConnection(TcpClient client) : base(client)
		{
			IsHosted = true;
		}
		public PeerConnection(string host, int port, int peerId) : base(host, port)
		{
			IsHosted = false;
			Id = peerId;
		}

		protected override async void OnReceive(EpspPacket packet)
		{
			switch (packet.Code)
			{
				case var code when (code / 100) == 5:
					DataReceived?.Invoke(packet);
					return;
				case 611: //echo
					await SendPacket(new EpspPacket(631, 1));
					return;
				case 615:
				case 635:
					DataReceived?.Invoke(packet);
					break;
				case 694:
					throw new EpspNonCompliantProtocolException("こちらのピア側のプロトコルバージョンが古いため、正常に接続できませんでした");
			}
			base.OnReceive(packet);
		}

		/// <summary>
		/// そのピアがデータを送受信できる段階にあるかどうか
		/// </summary>
		public bool Established { get; private set; } = false;
		/// <summary>
		/// こちらから接続を受け入れたがわかどうか
		/// </summary>
		public bool IsHosted { get; }
		/// <summary>
		/// ピアID
		/// </summary>
		public int Id { get; private set; }
		/// <summary>
		/// 伝送すべき情報を受信した
		/// 500･600番代の調査パケットをやり取りします
		/// </summary>
		public event Action<EpspPacket>? DataReceived;

		Timer? EchoTimer;
		public async Task<ClientInformation> ConnectAndExchangeClientInformation(ClientInformation information)
		{
			EpspPacket? clientVersionPacket = null;

			if (IsHosted)
			{
				StartReceive();
				await SendPacket(new EpspPacket(614, 1, information.ToPacketData()));
				clientVersionPacket = await WaitNextPacket(634);

				// TODO: 634であればバージョンチェック
			}
			else
			{
				StartReceive();
				clientVersionPacket = await WaitNextPacket(614);
				// TODO: バージョンチェック
				await SendPacket(new EpspPacket(634, 1, information.ToPacketData()));
			}
			if (clientVersionPacket.Data == null || clientVersionPacket.Data.Length < 3)
				throw new EpspException("ピアから正常なレスポンスがありせんでした。");

			// TODO: ゆらぎをもたせる
			// TODO: watchdog timer の追加
			EchoTimer = new Timer(150 * 1000);
			EchoTimer.Elapsed += async (s, e) =>
			{
				try
				{
					await SendEcho();
				}
				catch
				{
					// エコーに失敗したら切断
					Disconnect();
				}
			};
			EchoTimer.Start();

			Established = true;
			return new ClientInformation(clientVersionPacket.Data[0], clientVersionPacket.Data[1], clientVersionPacket.Data[2]);
		}

		/// <summary>
		/// ピアIDを交換する
		/// </summary>
		/// <param name="peerId">こちらのピアID</param>
		public async Task ExchangePeerId(int peerId)
		{
			if (!IsHosted)
			{
				await WaitNextPacket(612);
				await SendPacket(new EpspPacket(632, 1, peerId.ToString()));
				return;
			}
			await SendPacket(new EpspPacket(612, 1));
			var peerIdPacket = await WaitNextPacket(632);

			if (peerIdPacket.Data?.Length < 1)
				throw new EpspException("ピアから正常なレスポンスがありせんでした。");
			if (!int.TryParse(peerIdPacket.Data?[0], out var id))
				throw new EpspException("ピアから送信されたIDをパースすることができませんでした。");
			Id = id;
		}

		/// <summary>
		/// エコーする
		/// </summary>
		async Task SendEcho()
		{
			await SendPacket(new EpspPacket(611, 1));
			await WaitNextPacket(631);
		}

		public override void Disconnect()
		{
			EchoTimer?.Stop();
			base.Disconnect();
		}
	}
}
