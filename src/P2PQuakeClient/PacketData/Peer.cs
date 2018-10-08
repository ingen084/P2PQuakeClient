using System;

namespace P2PQuakeClient.PacketData
{
	public class Peer
	{
		public Peer(string hostname, int port, int id)
		{
			Hostname = hostname ?? throw new ArgumentNullException(nameof(hostname));
			Port = port;
			Id = id;
		}

		public string Hostname { get; set; }
		public int Port { get; set; }
		public int Id { get; set; }
	}
}
