using P2PQuakeClient.Connections;
using P2PQuakeClient.PacketData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace P2PQuakeClient.Sandbox
{
	class Program
	{
		static async Task Main(string[] args)
		{
			try
			{
				Console.WriteLine("Hello World!");
				Console.ReadLine();

				const string host = "p2pquake.ddo.jp";

				Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
				var clientInfo = new ClientInformation("0.34", "P2PQuakeClient@ingen084", "sandbox");
				int peerId = 0;
				RsaKey rsaKey = null;
				List<PeerConnection> peerConnections = new List<PeerConnection>();

				Console.WriteLine("ネットワークに参加しています。");
				using (var connection = new ServerConnection(host, 6910))
				{
					connection.Connected += () => Console.WriteLine("接続しました");
					connection.Disconnected += () => Console.WriteLine("切断しました");
					try
					{
						await connection.ConnectAndWaitClientInfoRequest();
						var serverInfo = await connection.SendClientInformation(clientInfo);
						Console.WriteLine($"ServerInfo: {serverInfo.SoftwareName}/{serverInfo.SoftwareVersion}({serverInfo.ProtocolVersion})");

						peerId = await connection.GetTemporaryPeerId();
						Console.WriteLine($"TemporaryPeerId: {peerId}");
						Console.WriteLine($"Port: {await connection.CheckPortForwarding(peerId, 6911)}");
						Console.WriteLine("Peers:");
						foreach (var peer in await connection.GetPeerInformations(peerId))
						{
							Console.WriteLine($" - {peer.Id} {peer.Hostname}:{peer.Port}");

							var peerConnection = new PeerConnection(peer.Hostname, peer.Port, peer.Id);
							peerConnection.Connected += () => Console.WriteLine("  - ピアに接続しました。 " + peer.Id);
							peerConnection.Disconnected += () =>
							{
								Console.WriteLine("  - ピアから切断しました。 " + peer.Id);
								if (peerConnections.Contains(peerConnection))
									peerConnections.Remove(peerConnection);
							};
							peerConnection.DataReceived += p => Console.WriteLine($"要伝送データ受信: {peer.Id} {p.ToPacketString()}");
							try
							{
								var peerInfo = await peerConnection.ConnectAndExchangeClientInformation(clientInfo);
								Console.WriteLine($"  - PeerInfo: {peerInfo.SoftwareName}/{peerInfo.SoftwareVersion}({peerInfo.ProtocolVersion})");
								await peerConnection.ExchangePeerId(peerId);
								Console.WriteLine("  - 接続完了 " + peerConnection.PeerId);
							}
							catch (Exception ex)
							{
								Console.WriteLine("  - 接続失敗: " + ex.Message);
								continue;
							}
							peerConnections.Add(peerConnection);
						}
						await connection.NoticeConnectedPeerIds(peerConnections.Select(c => c.PeerId).ToArray());

						peerId = await connection.GetPeerId(peerId, 6911, 901, peerConnections.Count(), 10);
						Console.WriteLine($"PeerId: {peerId}");
						rsaKey = await connection.GetRsaKey(peerId);
						await connection.GetRegionalPeersCount();
						Console.WriteLine($"ProtocolTime: {(await connection.GetProtocolTime()).ToString("yyyy/MM/dd HH:mm:ss.fff")}");
					}
					finally
					{
						await connection.SafeDisconnect();
					}
				}
				Console.WriteLine("参加完了しました。");

				var pingTimer = new Timer(1000 * 60 * 10);
				pingTimer.Elapsed += async (s, e) =>
				{
					Console.WriteLine("エコーをしています。");
					using (var connection = new ServerConnection(host, 6910))
					{
						connection.Connected += () => Console.WriteLine("接続しました");
						connection.Disconnected += () => Console.WriteLine("切断しました");
						try
						{
							await connection.ConnectAndWaitClientInfoRequest();
							var serverInfo = await connection.SendClientInformation(clientInfo);
							Console.WriteLine($"ServerInfo: {serverInfo.SoftwareName}/{serverInfo.SoftwareVersion}({serverInfo.ProtocolVersion})");
							if (!await connection.SendEcho(peerId, peerConnections.Count))
								Console.WriteLine("IPアドレスが違うそうです。");
							if (rsaKey == null || rsaKey.Expiration < DateTime.Now.AddMinutes(30))
								rsaKey = await connection.UpdateRsaKey(peerId, rsaKey?.PrivateKey);
							Console.WriteLine($"ProtocolTime: {(await connection.GetProtocolTime()).ToString("yyyy/MM/dd HH:mm:ss.fff")}");
						}
						finally
						{
							await connection.SafeDisconnect();
						}
					}
				};
				pingTimer.Start();

				Console.ReadLine();
				Console.WriteLine("ネットワークから離脱します。");

				pingTimer.Stop();

				foreach (var connection in peerConnections.ToArray())
					connection.Disconnect();

				using (var connection = new ServerConnection(host, 6910))
				{
					connection.Connected += () => Console.WriteLine("接続しました");
					connection.Disconnected += () => Console.WriteLine("切断しました");
					try
					{
						await connection.ConnectAndWaitClientInfoRequest();
						var serverInfo = await connection.SendClientInformation(clientInfo);
						Console.WriteLine($"ServerInfo: {serverInfo.SoftwareName}/{serverInfo.SoftwareVersion}({serverInfo.ProtocolVersion})");
						await connection.WithdrawalRequest(peerId, rsaKey?.PrivateKey);
					}
					finally
					{
						await connection.SafeDisconnect();
					}
				}
				Console.WriteLine("ネットワークから離脱しました。");
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
			}
			Console.ReadLine();
		}
	}
}
