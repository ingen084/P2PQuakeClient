﻿using P2PQuakeClient.SignedData;
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

		public event Action? PeerCountChanged;

		private List<EpspPeer> Peers { get; } = new List<EpspPeer>();
		public int Count => Peers.Count;

		public bool CheckDuplicatePeer(IPAddress addr)
		{
			lock (Peers)
				return Peers.Any(p => (p.Connection?.TcpClient.Client.RemoteEndPoint as IPEndPoint)?.Address == addr);
		}
		public bool CheckDuplicatePeer(int peerId)
		{
			lock (Peers)
				return Peers.Any(p2 => p2.Id == peerId);
		}

		public async Task<bool> AddPeer(EpspPeer peer)
		{
			if (await peer.ConnectAndHandshakeAsync())
			{
				if (peer.Connection == null)
					throw new Exception("接続が確立できていません");
				peer.Connection.DataReceived += p =>
				{
					try
					{
						DataReceived(peer, p);
					}
					catch (Exception ex)
					{
						Client.Logger.Warning("ピアからのデータ処理中に例外が発生しました: " + ex);
					}
				};
				lock (Peers)
					Peers.Add(peer);
				PeerCountChanged?.Invoke();
				peer.Connection.Disconnected += () =>
				{
					Client.Logger.Info($"{(peer.Connection.Established ? "" : "未完了状態の")}ピア {(peer.Id == default ? peer.Connection?.TcpClient.Client.RemoteEndPoint?.ToString() : peer.Id.ToString())} が切断しました。");
					lock (Peers)
						Peers.Remove(peer);
					PeerCountChanged?.Invoke();
				};
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
				if (packet.Data?.Length != 2
				 || !int.TryParse(packet.Data[0], out var senderId)
				 || !long.TryParse(packet.Data[1], out var uniqueNumber))
				{
					Client.Logger.Warning($"{peer.Id} から無効な調査エコーを受け取りました。");
					return;
				}

				lock (EchoHistories)
				{
					if (EchoHistories.ContainsKey((senderId, uniqueNumber)))
						return;
					EchoHistories.Add((senderId, uniqueNumber), peer.Id);
					if (EchoHistories.Count > 100)
						EchoHistories.Remove(EchoHistories.Keys.First());
				}
				packet.HopCount++;
#pragma warning disable CS8602 // null 参照の可能性があるものの逆参照です。
				await Task.WhenAny(Peers.Where(p => p != peer && p.Connection != null).Select(p => p.Connection.SendPacket(packet)));
#pragma warning restore CS8602 // null 参照の可能性があるものの逆参照です。

				// 調査エコーで指定されていた発信元ピアID  一意な数  自らのピアID  接続中のピアID(カンマ区切り)  調査エコーが届いた経由数
				if (peer.Connection != null)
					await peer.Connection.SendPacket(new EpspPacket(635, 1, packet.Data[0], packet.Data[1], Client.PeerId.ToString(), string.Join(',', Peers.Select(p => p.Id.ToString())), (packet.HopCount - 1).ToString()));
				return;
			}
			if (packet.Code == 635)
			{
				if (packet.Data?.Length != 5
				 || !int.TryParse(packet.Data[0], out var senderId)
				 || !long.TryParse(packet.Data[1], out var uniqueNumber))
				{
					Client.Logger.Warning($"{peer.Id} から無効な調査エコー返答を受け取りました。");
					return;
				}

				// バッファになければ無視
				lock (EchoHistories)
					if (!EchoHistories.ContainsKey((senderId, uniqueNumber)))
					{
						Client.Logger.Debug($"{peer.Id} からバッファにない調査エコーを受け取りました。");
						return;
					}

				packet.HopCount++;

				var fromPeerId = EchoHistories[(senderId, uniqueNumber)];
				EpspPeer? fromPeer = null;
				lock (Peers)
					fromPeer = Peers.FirstOrDefault(p => p.Id == fromPeerId);
				if (fromPeer == null)
#pragma warning disable CS8602 // null 参照の可能性があるものの逆参照です。
					await Task.WhenAny(Peers.Where(p => p != peer && p.Connection != null).Select(p => p.Connection.SendPacket(packet)));
#pragma warning restore CS8602 // null 参照の可能性があるものの逆参照です。
				else if (fromPeer.Connection != null)
					await fromPeer.Connection.SendPacket(packet);
			}
			// 500番台
			if (packet.Data?.Length < 3)
			{
				Client.Logger.Warning($"{peer.Id} から不正な500番台パケットを受け取りました。");
				return;
			}

			lock (DataSignatureHistories)
			{
				if (packet.Data?[0] is not string signature)
					return;
				if (DataSignatureHistories.Contains(signature))
					return;
				DataSignatureHistories.Add(signature);
				if (DataSignatureHistories.Count > 100)
					DataSignatureHistories.RemoveAt(0);
			}

			// 受信した時点で再送させる
			var nextPacket = packet.Clone();
			nextPacket.HopCount++;
			// どれかに送信できれば次の処理へ
#pragma warning disable CS8602 // null 参照の可能性があるものの逆参照です。
			var tasks = Peers.Where(p => p != peer && p.Connection != null).Select(p => p.Connection.SendPacket(nextPacket)).ToArray();
			if (tasks.Length > 0)
				await Task.WhenAny(tasks);
#pragma warning restore CS8602 // null 参照の可能性があるものの逆参照です。

			bool validated = false;
			switch (packet.Code)
			{
				case 551:
				case 552:
				case 561:
					{
						string targetData = "";
						if (packet.Code == 551)
							targetData = packet.Data[2] + packet.Data[3];
						else if (packet.Code == 552)
							targetData = packet.Data[2];
						else if (packet.Code == 561)
							targetData = packet.Data[2];

						if (!DateTime.TryParse(packet.Data[1].Replace('-', ':'), out var expirationTime))
							return;
						if (!RsaCryptoService.VerifyServerData(new ServerSignedData(targetData, expirationTime, Convert.FromBase64String(packet.Data[0])), Client.ProtocolTime))
						{
							Client.Logger.Warning($"{peer.Id} からの伝送パケットの署名の検証に失敗しました。\n{packet.ToPacketString()}");
							return;
						}
						validated = true;

						// 各地域ピア数のパケットの場合集計する
						if (packet.Code == 561)
						{
							var data = targetData.Split(';');
							int total = 0;
							foreach (var datum in data)
							{
								var areaInfo = datum.Split(',');
								if (areaInfo.Length >= 2 && int.TryParse(areaInfo[1], out var count))
									total += count;
							}
							Client.TotalNetworkPeerCount = total;
						}
					}
					break;
				case 555:
					{
						if (packet.Data.Length < 6
						 || !DateTime.TryParse(packet.Data[1].Replace('-', ':'), out var expirationTime)
						 || !DateTime.TryParse(packet.Data[4].Replace('-', ':'), out var keyExpirationTime))
							return;
						if (!RsaCryptoService.VerifyPeerData(new PeerSignedData(packet.Data[5], Convert.FromBase64String(packet.Data[0]), expirationTime, Convert.FromBase64String(packet.Data[2]), Convert.FromBase64String(packet.Data[3]), keyExpirationTime), Client.ProtocolTime))
						{
							Client.Logger.Warning($"{peer.Id} からの地震感知情報の署名の検証に失敗しました。\n{packet.ToPacketString()}");
							return;
						}
						validated = true;
					}
					break;
				default:
					Client.Logger.Warning($"{peer.Id} から未定義の伝送系パケットを受信しました。");
					break;
			}

			Client.OnDataReceived(validated, packet);
		}
		public async Task SendPacketAllClientAsync(EpspPacket packet)
		{
			// 一応重複配送チェック
			if (packet.Code / 100 == 5)
				lock (DataSignatureHistories)
				{
					if (packet.Data?[0] is not string key)
						return;
					if (DataSignatureHistories.Contains(key))
						return;
					DataSignatureHistories.Add(key);
					if (DataSignatureHistories.Count > 100)
						DataSignatureHistories.RemoveAt(0);
				}
#pragma warning disable CS8602 // null 参照の可能性があるものの逆参照です。
			var tasks = Peers.Where(p => p.Connection != null).Select(peer => peer.Connection.SendPacket(packet)).ToArray();
			if (tasks.Length > 0)
				await Task.WhenAny(tasks);
#pragma warning restore CS8602 // null 参照の可能性があるものの逆参照です。
		}

		public void DisconnectAllPeers()
		{
			foreach (var peer in Peers.ToArray())
			{
				peer?.Connection?.Disconnect();
				peer?.Dispose();
			}
		}
	}
}
