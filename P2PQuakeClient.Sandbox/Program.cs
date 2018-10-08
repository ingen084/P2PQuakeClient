using P2PQuakeClient.Connections;
using P2PQuakeClient.PacketData;
using System;
using System.Text;
using System.Threading.Tasks;

namespace P2PQuakeClient.Sandbox
{
	class Program
	{
		static async Task Main(string[] args)
		{
			Console.WriteLine("Hello World!");
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
			var clientInfo = new ClientInformation("0.34", "P2PQuakeClient@ingen084", "sandbox");
			int peerId = 0;
			RsaKey rsaKey = null;

			Console.WriteLine("ネットワークに参加しています。");
			using (var connection = new ServerConnection("www.p2pquake.net", 6910))
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
						Console.WriteLine($" - {peer.Id} {peer.Hostname}:{peer.Port}");
					peerId = await connection.GetPeerId(peerId, 6911, 901, 0, 255);
					Console.WriteLine($"PeerId: {peerId}");
					rsaKey = await connection.GetRsaKey(peerId);
					Console.WriteLine($"ProtocolTime: {(await connection.GetProtocolTime()).ToString("yyyy/MM/dd HH:mm:ss.fff")}");
				}
				finally
				{
					await connection.SafeDisconnect();
				}
			}
			Console.WriteLine("参加完了しました。");
			Console.ReadLine();
			Console.WriteLine("ネットワークから離脱します。");
			using (var connection = new ServerConnection("www.p2pquake.net", 6910))
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
			Console.ReadLine();
		}
	}
}
