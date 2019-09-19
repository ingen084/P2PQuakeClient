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
			//TODO: 調査エコーの発信
			if (packet.Code == 615)
			{
				if (packet.Data.Length != 2
				 || !int.TryParse(packet.Data[0], out var senderId)
				 || !long.TryParse(packet.Data[1], out var uniqueNumber))
					return;

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
					return;

				// バッファになければ無視
				lock (EchoHistories)
					if (!EchoHistories.ContainsKey((senderId, uniqueNumber)))
						return;

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
				return;

			lock (DataSignatureHistories)
			{
				if (DataSignatureHistories.Contains(packet.Data[0]))
					return;
				DataSignatureHistories.Add(packet.Data[0]);
				if (DataSignatureHistories.Count > 100)
					DataSignatureHistories.RemoveAt(0);
			}

			//TODO: メッセージ受信周り
		}
	}
}
