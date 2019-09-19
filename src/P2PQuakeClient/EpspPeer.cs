using P2PQuakeClient.Connections;
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
		public Peer PeerInfo { get; }
		public IEpspLogger Logger => EpspClient.Logger;

		public PeerConnection Connection { get; private set; }
		public bool IsConnected => Connection?.IsConnected ?? false;
		public ClientInformation ClientInformation { get; private set; }
		public int PeerId => Connection?.PeerId ?? default;

		public async Task<bool> ConnectAndHandshakeAsync()
		{
			if (Connection == null)
			{
				Logger.Info($"ピア{PeerInfo.Id} に接続中…");
				Connection = new PeerConnection(PeerInfo.Hostname, PeerInfo.Port, PeerInfo.Id);
			}
			try
			{
				ClientInformation = await Connection.ConnectAndExchangeClientInformation(EpspClient.ClientInfo);
				if (Connection.IsConnected)
				{
					await Connection.ExchangePeerId(EpspClient.PeerId);
					Logger.Info($"ピア{PeerId} を登録しました。 {ClientInformation.SoftwareName} - {ClientInformation.SoftwareVersion} (v{ClientInformation.ProtocolVersion})");
					return true;
				}
			}
			catch (EpspException ex)
			{
				Logger.Warning($"ピア{PeerInfo?.Id.ToString() ?? ""} への接続に失敗しました。\n{ex}");
			}
			Logger.Info($"ピア{PeerInfo.Id} に接続できませんでした。");
			return false;
		}

		public void Dispose()
		{
			Connection?.Dispose();
		}
	}
}
