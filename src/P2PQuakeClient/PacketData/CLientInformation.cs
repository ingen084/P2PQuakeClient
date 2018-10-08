using System;

namespace P2PQuakeClient.PacketData
{
	public class ClientInformation
	{
		public ClientInformation(string protocolVersion, string softwareName, string softwareVersion)
		{
			ProtocolVersion = protocolVersion ?? throw new ArgumentNullException(nameof(protocolVersion));
			SoftwareName = softwareName ?? throw new ArgumentNullException(nameof(softwareName));
			SoftwareVersion = softwareVersion ?? throw new ArgumentNullException(nameof(softwareVersion));
		}

		public string ProtocolVersion { get; set; }
		public string SoftwareName { get; set; }
		public string SoftwareVersion { get; set; }

		public string[] ToPacketData() => new[] { ProtocolVersion, SoftwareName, SoftwareVersion };
	}
}
