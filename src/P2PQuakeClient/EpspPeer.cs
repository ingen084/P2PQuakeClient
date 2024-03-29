﻿using P2PQuakeClient.Connections;
using P2PQuakeClient.PacketData;
using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace P2PQuakeClient
{
	public class EpspPeer : IDisposable
	{
		public EpspPeer(EpspClient epspClient, Peer peerInfo)
		{
			EpspClient = epspClient ?? throw new ArgumentNullException(nameof(epspClient));
			PeerInfo = peerInfo ?? throw new ArgumentNullException(nameof(peerInfo));
		}
		public EpspPeer(EpspClient epspClient, TcpClient client)
		{
			EpspClient = epspClient ?? throw new ArgumentNullException(nameof(epspClient));
			Connection = new PeerConnection(client);
		}

		private EpspClient EpspClient { get; }
		public Peer? PeerInfo { get; }
		public IEpspLogger Logger => EpspClient.Logger;

		public PeerConnection? Connection { get; private set; }
		public bool IsConnected => Connection?.IsConnected ?? false;
		public ClientInformation? ClientInformation { get; private set; }
		public int Id => Connection?.Id ?? default;

		public async Task<bool> ConnectAndHandshakeAsync()
		{
			if (Connection == null)
			{
#pragma warning disable CS8602 // null 参照の可能性があるものの逆参照です。
				Logger.Info($"ピア{PeerInfo.Id} に接続中…");
				Connection = new PeerConnection(PeerInfo.Hostname, PeerInfo.Port, PeerInfo.Id);
#pragma warning restore CS8602 // null 参照の可能性があるものの逆参照です。
			}
			try
			{
				Logger.Info($"{(Connection.IsHosted ? "ホスト" : "クライアント")}モードでクライアント情報を交換中…");
				ClientInformation = await Connection.ConnectAndExchangeClientInformation(EpspClient.ClientInfo);
				if (Connection.IsConnected)
				{
					await Connection.ExchangePeerId(EpspClient.PeerId);
					Logger.Info($"ピア{Id} を登録しました。 {ClientInformation.SoftwareName}-{ClientInformation.SoftwareVersion} (v{ClientInformation.ProtocolVersion}) 現在の接続数:{EpspClient.PeerController.Count + 1}");
					return true;
				}
			}
			catch (Exception ex) when (ex is EpspException || ex is SocketException)
			{
				Logger.Warning($"ピア{PeerInfo?.Id.ToString() ?? ""} への接続に失敗しました。 {ex.Message}");
			}
			Logger.Info($"ピア{PeerInfo?.Id} に接続できませんでした。");
			return false;
		}

		public void Dispose()
		{
			Connection?.Dispose();
			GC.SuppressFinalize(this);
		}
	}
}
