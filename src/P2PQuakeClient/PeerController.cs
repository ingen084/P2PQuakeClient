using P2PQuakeClient.SignedData;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace P2PQuakeClient
{
	internal class PeerController
	{
		private EpspClient Client { get; }
		public PeerController(EpspClient client)
		{
			Client = client ?? throw new ArgumentNullException(nameof(client));
		}

		private List<EpspPeer> Peers { get; } = new List<EpspPeer>();
		public int Count => Peers.Count;

		public bool CheckDuplicatePeer(IPAddress addr)
		{
			lock (Peers)
				return Peers.Any(p => (p.Connection.TcpClient.Client.RemoteEndPoint as IPEndPoint).Address == addr);
		}
		public bool CheckDuplicatePeer(int peerId)
		{
			lock (Peers)
				return Peers.Any(p2 => p2.PeerId == peerId);
		}

		public async Task<bool> AddPeer(EpspPeer peer)
		{
			if (await peer.ConnectAndHandshakeAsync())
			{
				peer.Connection.DataReceived += p => DataReceived(peer, p);
				peer.Connection.Disconnected += () =>
				{
					Client.Logger.Info($"{(peer.Connection.Established ? "" : "未完了状態の")}ピア{(peer.PeerId == default ? "" : (peer.PeerId + " "))}が切断しました。");
					lock (Peers)
						Peers.Remove(peer);
				};
				lock (Peers)
					Peers.Add(peer);
				return true;
			}
			return false;
		}

		List<string> DataSignatureHistories { get; } = new List<string>();
		Dictionary<(int sender, long uniq), int> EchoHistories { get; } = new Dictionary<(int, long), int>();
		private async void DataReceived(EpspPeer peer, EpspPacket packet)
		{
			Client.Logger.Trace($"{peer.PeerId} DataReceived:\n{packet.ToPacketString()}");

			//TODO: 調査エコーの発信
			if (packet.Code == 615)
			{
				if (packet.Data.Length != 2
				 || !int.TryParse(packet.Data[0], out var senderId)
				 || !long.TryParse(packet.Data[1], out var uniqueNumber))
				{
					Client.Logger.Warning($"{peer.PeerId} から無効な調査エコーを受け取りました。");
					return;
				}

				lock (EchoHistories)
				{
					if (EchoHistories.ContainsKey((senderId, uniqueNumber)))
						return;
					EchoHistories.Add((senderId, uniqueNumber), peer.PeerId);
					if (EchoHistories.Count < 100)
						EchoHistories.Remove(EchoHistories.Keys.First());
				}
				packet.HopCount++;
				await Task.WhenAll(Peers.Where(p => p != peer).Select(p => p.Connection.SendPacket(packet)));

				// 調査エコーで指定されていた発信元ピアID  一意な数  自らのピアID  接続中のピアID(カンマ区切り)  調査エコーが届いた経由数
				await peer.Connection.SendPacket(new EpspPacket(635, 1, packet.Data[0], packet.Data[1], Client.PeerId.ToString(), string.Join(',', Peers.Select(p => p.PeerId.ToString())), (packet.HopCount - 1).ToString()));
				return;
			}
			if (packet.Code == 635)
			{
				if (packet.Data.Length != 5
				 || !int.TryParse(packet.Data[0], out var senderId)
				 || !long.TryParse(packet.Data[1], out var uniqueNumber))
				{
					Client.Logger.Warning($"{peer.PeerId} から無効な調査エコー返答を受け取りました。");
					return;
				}

				// バッファになければ無視
				lock (EchoHistories)
					if (!EchoHistories.ContainsKey((senderId, uniqueNumber)))
					{
						Client.Logger.Debug($"{peer.PeerId} からバッファにない調査エコーを受け取りました。");
						return;
					}

				packet.HopCount++;

				var fromPeerId = EchoHistories[(senderId, uniqueNumber)];
				EpspPeer fromPeer = null;
				lock (Peers)
					fromPeer = Peers.FirstOrDefault(p => p.PeerId == fromPeerId);
				if (fromPeer == null)
					await Task.WhenAll(Peers.Where(p => p != peer).Select(p => p.Connection.SendPacket(packet)));
				else
					await fromPeer.Connection.SendPacket(packet);
			}
			// 500番台
			if (packet.Data.Length < 3)
			{
				Client.Logger.Warning($"{peer.PeerId} から不正な500番台パケットを受け取りました。");
				return;
			}

			lock (DataSignatureHistories)
			{
				if (DataSignatureHistories.Contains(packet.Data[0]))
					return;
				DataSignatureHistories.Add(packet.Data[0]);
				if (DataSignatureHistories.Count > 100)
					DataSignatureHistories.RemoveAt(0);
			}

			bool validated = false;
			switch (packet.Code)
			{
				case 551:
				case 552:
				case 561:
					{
						string targetData = "";
						if (packet.Code == 551)
							targetData = $"{packet.Data[2]}:{packet.Data[3]}";
						else if (packet.Code == 552)
							targetData = packet.Data[2];
						else if (packet.Code == 561)
							targetData = packet.Data[2];

						if (packet.Data.Length != 4
						|| !DateTime.TryParse(packet.Data[1].Replace('-', ':'), out var expirationTime))
							return;
						if (!RsaCryptoService.VerifyServerData(new ServerSignedData(targetData, expirationTime, Convert.FromBase64String(packet.Data[0])), Client.ProtocolTime))
						{
							Client.Logger.Warning($"{peer.PeerId} からのパケットの署名の検証に失敗しました。");
							return;
						}
						validated = true;
					}
					break;
				case 555:
					{
						if (packet.Data.Length != 6
						 || !DateTime.TryParse(packet.Data[1].Replace('-', ':'), out var expirationTime)
						 || !DateTime.TryParse(packet.Data[4].Replace('-', ':'), out var keyExpirationTime))
							return;
						if (!RsaCryptoService.VerifyPeerData(new PeerSignedData(packet.Data[5], Convert.FromBase64String(packet.Data[0]), expirationTime, Convert.FromBase64String(packet.Data[2]), Convert.FromBase64String(packet.Data[3]), keyExpirationTime), Client.ProtocolTime))
						{
							Client.Logger.Warning($"{peer.PeerId} からのパケットの署名の検証に失敗しました。");
							return;
						}
						validated = true;
					}
					break;
				default:
					Client.Logger.Warning($"{peer.PeerId} から未定義の伝送系パケットを受信しました。");
					break;
			}
			
			Client.OnDataReceived(validated, packet);

			packet.HopCount++;
			await Task.WhenAll(Peers.Where(p => p != peer).Select(p => p.Connection.SendPacket(packet)));
		}
	}
}
