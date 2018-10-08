using System;

namespace P2PQuakeClient.PacketData
{
	public class RsaKey
	{
		public RsaKey(byte[] publicKey, byte[] privateKey, DateTime expiration, byte[] signature)
		{
			PublicKey = publicKey ?? throw new ArgumentNullException(nameof(publicKey));
			PrivateKey = privateKey ?? throw new ArgumentNullException(nameof(privateKey));
			Expiration = expiration;
			Signature = signature ?? throw new ArgumentNullException(nameof(signature));
		}

		public byte[] PublicKey { get; set; }
		public byte[] PrivateKey { get; set; }
		public DateTime Expiration { get; set; }
		public byte[] Signature { get; set; }
	}
}
