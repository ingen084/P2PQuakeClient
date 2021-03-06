﻿using System;
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

				var client = new EpspClient(new EasyConsoleLogger(), hosts, 901, 6911, 100);
				client.DataReceived += (v, d) =>
				{
					Console.WriteLine("**データ受信 " + d.Code);
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
						if (!await client.EchoAsync())
							Console.WriteLine("**エコーに失敗");
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
