using System;

namespace P2PQuakeClient.SignedData
{
	public class PeerSignedData : ServerSignedData
	{
		public PeerSignedData(string data, byte[] dataSignature, DateTime dataExpiration, byte[] publicKey, byte[] publicKeySignature, DateTime publicKeyExpiration)
			: base(data, dataExpiration, dataSignature)
		{
			PublicKey = publicKey ?? throw new ArgumentNullException(nameof(publicKey));
			PublicKeySignature = publicKeySignature ?? throw new ArgumentNullException(nameof(publicKeySignature));
			PublicKeyExpiration = publicKeyExpiration;
		}
		public byte[] PublicKey { get; }
		public byte[] PublicKeySignature { get; }
		public DateTime PublicKeyExpiration { get; }

		public string[] ToPacketData()
			=> new[] { Convert.ToBase64String(Signature), Expiration.ToString("yyyy/MM/dd HH-mm-ss"), Convert.ToBase64String(PublicKey), Convert.ToBase64String(PublicKeySignature), PublicKeyExpiration.ToString("yyyy/MM/dd HH-mm-ss"), Data };
	}
}
