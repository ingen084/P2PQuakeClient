using P2PQuakeClient.PacketData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace P2PQuakeClient.Connections
{
	public class ServerConnection : EpspConnection
	{
		public ServerConnection(string host, int port) : base(host, port)
		{
		}

		/// <summary>
		/// サーバに接続し、クライアント情報の要求を待機する
		/// </summary>
		public async Task ConnectAndWaitClientInfoRequest()
		{
			StartReceive();
			await WaitNextPacket(211);
		}

		/// <summary>
		/// クライアント情報を送信する
		/// </summary>
		/// <param name="information">クライアント情報</param>
		/// <returns>サーバ情報</returns>
		public async Task<ClientInformation> SendClientInformation(ClientInformation information)
		{
			await SendPacket(new EpspPacket(131, 1, information.ToPacketData()));

			await WaitNextPacket(212, 292);
			if (LastPacket.Code == 292)
				throw new EpspVersionObsoletedException("クライアント側のプロトコルバージョンが古いため、正常に接続できませんでした。");

			if (LastPacket.Data.Length < 3)
				throw new EpspException("サーバから正常なレスポンスがありせんでした。");

			return new ClientInformation(LastPacket.Data[0], LastPacket.Data[1], LastPacket.Data[2]);
		}

		/// <summary>
		/// 仮ピアIDを要求する
		/// </summary>
		/// <returns></returns>
		public async Task<int> GetTemporaryPeerId()
		{
			await SendPacket(new EpspPacket(113, 1));
			await WaitNextPacket(233);

			if (LastPacket.Data.Length < 1)
				throw new EpspException("サーバから正常なレスポンスがありせんでした。");
			if (!int.TryParse(LastPacket.Data[0], out var id))
				throw new EpspException("サーバから送信された仮IDをパースすることができませんでした。");
			return id;
		}

		/// <summary>
		/// ポート開放がされているかチェックする
		/// </summary>
		/// <returns></returns>
		public async Task<bool> CheckPortForwarding(int temporaryPeerId, ushort port)
		{
			await SendPacket(new EpspPacket(114, 1, temporaryPeerId.ToString(), port.ToString()));
			await WaitNextPacket(234);

			if (LastPacket.Data.Length < 1)
				throw new EpspException("サーバから正常なレスポンスがありせんでした。");
			if (!int.TryParse(LastPacket.Data[0], out var id))
				throw new EpspException("サーバから送信されたデータパースすることができませんでした。");
			return id == 1;
		}

		/// <summary>
		/// ピアの情報を取得する
		/// </summary>
		/// <returns></returns>
		public async Task<Peer[]> GetPeerInformations(int temporaryPeerId)
		{
			await SendPacket(new EpspPacket(115, 1, temporaryPeerId.ToString()));
			await WaitNextPacket(235);

			if (LastPacket.Data.Length < 1)
				throw new EpspException("サーバから正常なレスポンスがありせんでした。");

			//TODO: エラー処理
			var peers = new List<Peer>();
			foreach (var peerStr in LastPacket.Data)
			{
				var param = peerStr.Split(",");
				peers.Add(new Peer(param[0], int.Parse(param[1]), int.Parse(param[2])));
			}
			return peers.ToArray();
		}

		/// <summary>
		/// 接続したピアIDを通知する
		/// </summary>
		/// <param name="connectedPeerIds">接続したピアID</param>
		public async Task NoticeConnectedPeerIds(params int[] connectedPeerIds)
		{
			await SendPacket(new EpspPacket(155, 1, connectedPeerIds.Select(i => i.ToString()).ToArray()));
		}


		/// <summary>
		/// 本ピアIDとして登録する
		/// </summary>
		/// <returns>現在の接続数</returns>
		public async Task<int> RegistPeerInfo(int temporaryPeerId, int port, int areaCode, int currentConnectingPeerCount, int maximumConnectablePeerCount)
		{
			//MEMO: 仕様書のサンプルにはこれ以外のパラメタがつけられているが。
			await SendPacket(new EpspPacket(116, 1, temporaryPeerId.ToString(), port.ToString(), areaCode.ToString(), currentConnectingPeerCount.ToString(), maximumConnectablePeerCount.ToString()));
			await WaitNextPacket(236);

			if (LastPacket.Data.Length < 1)
				throw new EpspException("サーバから正常なレスポンスがありせんでした。");
			if (!int.TryParse(LastPacket.Data[0], out var id))
				throw new EpspException("サーバから送信された本IDをパースすることができませんでした。");
			return id;
		}

		/// <summary>
		/// RSA鍵を要求する
		/// </summary>
		/// <returns>RSA鍵情報</returns>
		public async Task<RsaKey> GetRsaKey(int peerId)
		{
			await SendPacket(new EpspPacket(117, 1, peerId.ToString()));
			await WaitNextPacket(237, 295);

			if (LastPacket.Code == 295)
				return null;
			if (LastPacket.Data.Length < 4)
				throw new EpspException("サーバから正常なレスポンスがありせんでした。");

			return new RsaKey(Convert.FromBase64String(LastPacket.Data[0]), Convert.FromBase64String(LastPacket.Data[1]), DateTime.Parse(LastPacket.Data[2].Replace('-', ':')), Convert.FromBase64String(LastPacket.Data[3]));
		}

		/// <summary>
		/// ピアの分布を取得する
		/// </summary>
		/// <returns>ピアの分布</returns>
		public async Task<Dictionary<int, int>> GetRegionalPeersCount()
		{
			await SendPacket(new EpspPacket(127, 1));
			await WaitNextPacket(247);

			if (LastPacket.Data.Length < 1)
				throw new EpspException("サーバから正常なレスポンスがありせんでした。");

			//TODO: エラー処理
			var peers = new Dictionary<int, int>();
			foreach (var peerStr in LastPacket.Data[0].Split(';'))
			{
				var param = peerStr.Split(",");
				peers.Add(int.Parse(param[0]), int.Parse(param[1]));
			}
			return peers;
		}

		/// <summary>
		/// プロトコル時刻を取得する
		/// </summary>
		/// <returns>プロトコル時刻</returns>
		public async Task<DateTime> GetProtocolTime()
		{
			await SendPacket(new EpspPacket(118, 1));
			await WaitNextPacket(238);

			if (LastPacket.Data.Length < 1)
				throw new EpspException("サーバから正常なレスポンスがありせんでした。");
			if (!DateTime.TryParse(LastPacket.Data[0].Replace('-', ':'), out var time))
				throw new EpspException("サーバから送信された時刻をパースすることができませんでした。");
			return time;
		}

		/// <summary>
		/// 切断を要求する
		/// </summary>
		public async Task SafeDisconnect()
		{
			await SendPacket(new EpspPacket(119, 1));
			await WaitNextPacket(239);
			Disconnect();
		}

		/// <summary>
		/// ネットワークからの離脱を要求する
		/// </summary>
		/// <param name="peerId">ピアID</param>
		/// <param name="rsaPrivateKey">割り当てられた秘密鍵</param>
		public async Task LeaveNetworkRequest(int peerId, byte[] rsaPrivateKey = null)
		{
			await SendPacket(new EpspPacket(128, 1, peerId.ToString(), rsaPrivateKey == null ? "Unknown" : Convert.ToBase64String(rsaPrivateKey)));
			await WaitNextPacket(248, 299);
		}

		/// <summary>
		/// RSA鍵を再要求する
		/// </summary>
		/// <returns>RSA鍵情報</returns>
		public async Task<RsaKey> UpdateRsaKey(int peerId, byte[] rsaPrivateKey)
		{
			await SendPacket(new EpspPacket(124, 1, peerId.ToString(), rsaPrivateKey == null ? "Unknown" : Convert.ToBase64String(rsaPrivateKey)));
			await WaitNextPacket(244, 295);

			if (LastPacket.Code == 295)
				return null;
			if (LastPacket.Data.Length < 4)
				throw new EpspException("サーバから正常なレスポンスがありせんでした。");

			return new RsaKey(Convert.FromBase64String(LastPacket.Data[0]), Convert.FromBase64String(LastPacket.Data[1]), DateTime.Parse(LastPacket.Data[2].Replace('-', ':')), Convert.FromBase64String(LastPacket.Data[3]));
		}

		/// <summary>
		/// エコーする
		/// </summary>
		/// <param name="peerId">現在のピアID</param>
		/// <param name="connectingCount">現在のピアとの接続数</param>
		/// <returns>正常にエコーできたかどうか</returns>
		public async Task<bool> SendEcho(int peerId, int connectingCount)
		{
			await SendPacket(new EpspPacket(123, 1, peerId.ToString(), connectingCount.ToString()));
			await WaitNextPacket(243, 299);
			return LastPacket.Code == 243;
		}
	}
}
