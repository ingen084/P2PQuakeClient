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
			PeerId = peerId;
		}

		protected override async void OnReceive(EpspPacket packet)
		{
			switch (packet.Code)
			{
				case var code when (code / 100) == 5:
					DataReceived?.Invoke(packet);
					break;
				case 611: //echo
					await SendPacket(new EpspPacket(631, 1));
					ManualResetEvent.Set();
					return;
				case 615:
				case 635:
					DataReceived?.Invoke(packet);
					break;
			}
			base.OnReceive(packet);
		}

		/// <summary>
		/// そのピアがデータを送受信できる段階にあるかどうか
		/// </summary>
		public bool Established { get; set; } = false;
		/// <summary>
		/// こちらから接続を受け入れたがわかどうか
		/// </summary>
		public bool IsHosted { get; set; }
		/// <summary>
		/// ピアID
		/// </summary>
		public int PeerId { get; set; }
		/// <summary>
		/// 伝送すべき情報を受信した
		/// 500･600番代の調査パケットをやり取りします
		/// </summary>
		public event Action<EpspPacket> DataReceived;

		Timer EchoTimer;
		public async Task<ClientInformation> ConnectAndExchangeClientInformation(ClientInformation information)
		{
			StartReceive();

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

			if (IsHosted)
			{
				await SendPacket(new EpspPacket(614, 1, information.ToPacketData()));
				await WaitNextPacket(634, 694);
				// TODO: 634であればバージョンチェック
			}
			else
			{
				await WaitNextPacket(614);
				// TODO: バージョンチェック
				await SendPacket(new EpspPacket(634, 1, information.ToPacketData()));
			}

			if (LastPacket.Code == 694)
				throw new EpspVersionObsoletedException("こちらのピア側のプロトコルバージョンが古いため、正常に接続できませんでした。");
			if (LastPacket.Data.Length < 3)
				throw new EpspException("ピアから正常なレスポンスがありせんでした。");

			Established = true;
			return new ClientInformation(LastPacket.Data[0], LastPacket.Data[1], LastPacket.Data[2]);
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
			await WaitNextPacket(632);

			if (LastPacket.Data.Length < 1)
				throw new EpspException("サーバから正常なレスポンスがありせんでした。");
			if (!int.TryParse(LastPacket.Data[0], out var id))
				throw new EpspException("サーバから送信された仮IDをパースすることができませんでした。");
			PeerId = id;
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
