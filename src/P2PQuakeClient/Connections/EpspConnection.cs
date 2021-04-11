using System;
using System.Collections.Concurrent;
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
		protected NetworkStream? Stream { get; set; }

		/// <summary>
		/// 接続完了 ただしTcpClientを渡した場合は流れてこない
		/// </summary>
		public event Action? Connected;
		/// <summary>
		/// 切断
		/// </summary>
		public event Action? Disconnected;

		/// <summary>
		/// 接続済みかどうか
		/// </summary>
		public bool IsConnected => TcpClient?.Connected ?? false;

		byte[] ReceiveBuffer { get; }
		PacketSplitter Splitter { get; }

		Thread? ConnectionThread { get; set; }
		CancellationTokenSource TokenSource { get; }

		string? Host { get; }
		int? Port { get; }
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
			if (ConnectionThread != null)
				throw new InvalidOperationException("すでに受信は開始済みです。");
			// Connected += () => ManualResetEvent.Set();
			Disconnected += () => ManualResetEvent.Set();
			if (!TcpClient.Connected)
			{
				if (Host is not string host || Port is not int port)
					throw new InvalidOperationException("接続先情報が正常に設定されていません");

				if (!TcpClient.ConnectAsync(host, port).Wait(2000))
					throw new EpspException("接続がタイムアウトしました");
				Connected?.Invoke();
			}
			ConnectionThread = new Thread(ReceiveTask);
			ConnectionThread.Start();
		}
		private async void ReceiveTask(object? _)
		{
			try
			{
				TcpClient.NoDelay = true;

				Stream = TcpClient.GetStream();

				var count = 0;
				while (Stream.CanRead && (count = await Stream.ReadAsync(ReceiveBuffer.AsMemory(0, ReceiveBuffer.Length), TokenSource.Token)) > 0)
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
		protected ConcurrentQueue<EpspPacket> PacketQuete { get; } = new();
		protected virtual void OnReceive(EpspPacket packet)
		{
			if (this is PeerConnection pc && pc.IsHosted)
				Console.WriteLine(GetHashCode() + " PH↓ " + packet.ToPacketString());
			//else
			//	Console.WriteLine(GetHashCode() + "P↓ " + packet.ToPacketString());
			PacketQuete.Enqueue(packet);
			ManualResetEvent.Set();
		}

		protected Task<EpspPacket> WaitNextPacket(params int[] allowPacketCodes)
			=> WaitNextPacketWithSkipReset(allowPacketCodes);
		protected async Task<EpspPacket> WaitNextPacketWithSkipReset(params int[] allowPacketCodes)
		{
			if (
				PacketQuete.IsEmpty &&
				!await Task.Run(() =>
				{
					ManualResetEvent.Reset();
					return ManualResetEvent.Wait(10000);
				}))
				throw new EpspException("要求がタイムアウトしました。");
			if (!PacketQuete.TryDequeue(out var lastPacket))
				throw new EpspException("パケットを受信できませんでした");
			if (lastPacket.Code == 298)
				throw new EpspNonCompliantProtocolException("クライアントが仕様に準拠していないようです。");
			if (!allowPacketCodes.Contains(lastPacket.Code))
				throw new EpspException("接続先から期待しているレスポンスがありせんでした。: " + lastPacket.Code);
			return lastPacket;
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
				if (this is PeerConnection pc && pc.IsHosted)
					Console.WriteLine(GetHashCode() + " PH↑ " + packet.ToPacketString());
				//else// if (packet.Code / 100 == 5)
				//	Console.WriteLine(GetHashCode() + "P↑ " + packet.ToPacketString());
				byte[] buffer = Splitter.Encoding.GetBytes(packet.ToPacketString() + "\r\n");
				if (Stream != null)
					await Stream.WriteAsync(buffer.AsMemory(0, buffer.Length));
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
			GC.SuppressFinalize(this);
		}
	}
}
