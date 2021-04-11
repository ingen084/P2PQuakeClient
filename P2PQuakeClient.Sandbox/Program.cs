using System;
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
				Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
				Console.WriteLine("改行で続行");
				Console.ReadLine();

				var hosts = new[] {
					"p2pquake.dyndns.info",
					"www.p2pquake.net",
					"p2pquake.dnsalias.net",
					"p2pquake.ddo.jp"
				};

				var client = new EpspClient(new EasyConsoleLogger(), hosts, 901, 6911, 1024)
				{
					//MinimumKeepPeerCount = 10
				};
				client.DataReceived += (v, d) =>
				{
					Console.WriteLine($"**データ受信 {d.Code} hop:{d.HopCount} data:{string.Join(':', d.Data)}");
				};

				void UpdateTitle()
				{
					Console.Title = $"P2P地震情報 テストクライアント 接続/総ピア:{client.PeerCount}/{client.TotalNetworkPeerCount} ネットワーク:{(client.IsNetworkJoined ? "接続中" : "未接続")} ポート:{(client.IsPortForwarded ? "解放済" : "未開放")} 鍵:{(client.RsaKey == null ? "未取得" : "取得済み")}";
				}
				UpdateTitle();
				client.StateUpdated += () =>
				{
					UpdateTitle();
				};

				if (!await client.JoinNetworkAsync())
				{
					Console.WriteLine("**Join失敗**");
					return;
				}
				try
				{
					using var timer = new Timer(1000 * 60 * 10);
					timer.Elapsed += async (s, e) =>
					{
						try
						{
							if (!await client.EchoAsync())
								Console.WriteLine("**エコーに失敗");
						}
						catch (Exception ex)
						{
							Console.WriteLine("**エコー中に問題が発生しました " + ex.Message);
						}
					};
					timer.Start();
					Console.ReadLine();
					timer.Stop();
				}
				finally
				{
					await client.LeaveNetworkAsync();
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("Unhandled Exception: " + ex);
			}
		}
	}
	public class EasyConsoleLogger : IEpspLogger
	{
		public void Trace(string message)
			=> Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [TRAC] {message}");

		public void Debug(string message)
			=> Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [DEBG] {message}");

		public void Info(string message)
			=> Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [INFO] {message}");

		public void Warning(string message)
			=> Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [WARN] {message}");

		public void Error(string message)
			=> Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [ERR ] {message}");
	}
}
