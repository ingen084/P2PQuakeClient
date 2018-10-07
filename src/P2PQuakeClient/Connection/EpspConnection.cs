using System.Net.Sockets;

namespace P2PQuakeClient.Connection
{
	public abstract class EpspConnection
	{
		protected TcpClient Client { get; }
		protected NetworkStream Stream { get; }

		byte[] buffer;

		protected EpspConnection()
		{
			buffer = new byte[1024];
		}
	}
}
