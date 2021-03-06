﻿using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace P2PQuakeClient.Connections
{
	public abstract class EpspConnection : IDisposable
	{
		public TcpClient TcpClient { get; }
		protected NetworkStream Stream { get; set; }

		/// <summary>
		/// 接続完了 ただしTcpClientを渡した場合は流れてこない
		/// </summary>
		public event Action Connected;
		/// <summary>
		/// 切断
		/// </summary>
		public event Action Disconnected;

		/// <summary>
		/// 接続済みかどうか
		/// </summary>
		public bool IsConnected => TcpClient?.Connected ?? false;

		byte[] ReceiveBuffer { get; }
		PacketSplitter Splitter { get; }

		Task ConnectionTask { get; set; }
		CancellationTokenSource TokenSource { get; }

		string Host { get; }
		int Port { get; }
		protected EpspConnection(string host, int port)
		{
			ReceiveBuffer = new byte[1024];
			Splitter = new PacketSplitter();
			TokenSource = new CancellationTokenSource();
			TcpClient = new TcpClient();
			Host = host;
			Port = port;
		}
		protected EpspConnection(TcpClient client)
		{
			ReceiveBuffer = new byte[1024];
			Splitter = new PacketSplitter();
			TokenSource = new CancellationTokenSource();
			TcpClient = client;
		}

		protected ManualResetEventSlim ManualResetEvent { get; } = new ManualResetEventSlim();
		public void StartReceive()
		{
			if (ConnectionTask != null)
				throw new InvalidOperationException("すでに受信は開始済みです。");
			Connected += () => ManualResetEvent.Set();
			Disconnected += () => ManualResetEvent.Set();
			if (!TcpClient.Connected)
			{
				if (!TcpClient.ConnectAsync(Host, Port).Wait(2000))
					throw new SocketException(10060);
				Connected?.Invoke();
			}
			ConnectionTask = new Task(ReceiveTask().Wait, TokenSource.Token, TaskCreationOptions.LongRunning);
			ConnectionTask.Start();
		}
		private async Task ReceiveTask()
		{
			try
			{
				TcpClient.NoDelay = true;

				Stream = TcpClient.GetStream();

				var count = 0;
				while (Stream.CanRead && (count = await Stream.ReadAsync(ReceiveBuffer, 0, ReceiveBuffer.Length, TokenSource.Token)) > 0)
					foreach (var rawPacket in Splitter.ParseAndSplit(ReceiveBuffer, count))
						OnReceive(new EpspPacket(rawPacket));
			}
			catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException || ex is IOException || ex is SocketException)
			{
			}
			catch (EpspException ex)
			{
				Console.WriteLine("Receive Packet Parse Exception: " + ex);
			}
			catch (Exception ex)
			{
				Console.WriteLine("Receive General Exception: " + ex);
			}
			Disconnect();
		}

		protected EpspPacket LastPacket { get; set; }
		protected virtual void OnReceive(EpspPacket packet)
		{
			//if (this is ServerConnection)
			//	Console.WriteLine("↓ " + packet.ToPacketString());
			LastPacket = packet;
			ManualResetEvent.Set();
		}

		protected async Task WaitNextPacket(params int[] allowPacketCodes)
		{
			ManualResetEvent.Reset(); //TODO: 初回接続時は飛んでくるパケットが早すぎてResetするまえに送られてきてしまうことがあるらしい
			if (!await Task.Run(() => ManualResetEvent.Wait(10000)))
				throw new EpspException("要求がタイムアウトしました。");
			if (LastPacket == null) //nullの場合は接続失敗
				throw new EpspException("接続に失敗しました。");
			if (LastPacket.Code == 298)
				throw new EpspNonCompliantProtocolException("クライアントが仕様に準拠していないようです。");
			if (!allowPacketCodes.Contains(LastPacket.Code))
				throw new EpspException("接続先から期待しているレスポンスがありせんでした。: " + LastPacket.Code);
		}

		public async Task SendPacket(EpspPacket packet)
		{
			try
			{
				if (!TcpClient.Connected || TokenSource.IsCancellationRequested)
				{
					Disconnect();
					return;
				}
				//if (this is ServerConnection)
				//	Console.WriteLine("↑ " + packet.ToPacketString());
				//else if (packet.Code / 100 == 5)
				//	Console.WriteLine("P↑ " + packet.Code);
				byte[] buffer = Splitter.Encoding.GetBytes(packet.ToPacketString() + "\r\n");
				await Stream.WriteAsync(buffer, 0, buffer.Length);
			}
			catch (Exception ex) when (ex is IOException || ex is SocketException)
			{
				Disconnect();
			}
			catch (Exception ex)
			{
				Console.WriteLine("Send General Exception: " + ex);
				Disconnect();
			}
		}


		public virtual void Disconnect()
		{
			if (!TcpClient.Connected)
				return;
			TokenSource.Cancel();
			TcpClient.Close();
			Disconnected?.Invoke();
		}

		public void Dispose()
		{
			if (!TokenSource.IsCancellationRequested)
				Disconnect();
			TcpClient?.Dispose();
		}
	}
}
