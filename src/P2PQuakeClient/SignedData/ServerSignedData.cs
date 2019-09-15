using System;

namespace P2PQuakeClient.SignedData
{
	public class ServerSignedData
	{
		public ServerSignedData(string data, DateTime expiration, byte[] signature)
		{
			Data = data ?? throw new ArgumentNullException(nameof(data));
			Expiration = expiration;
			Signature = signature ?? throw new ArgumentNullException(nameof(signature));
		}

		public string Data { get; }
		public DateTime Expiration { get; }
		public byte[] Signature { get; }
	}
}
