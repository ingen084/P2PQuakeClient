using Asn1PKCS.Decoder;
using P2PQuakeClient.PacketData;
using P2PQuakeClient.SignedData;
using System;
using System.Security.Cryptography;
using System.Text;

namespace P2PQuakeClient
{
	public static class RsaCryptoService
	{
		private static readonly byte[] ServerPublicKey = Convert.FromBase64String("MIGdMA0GCSqGSIb3DQEBAQUAA4GLADCBhwKBgQC8p/vth2yb/k9x2/PcXKdb6oI3gAbhvr/HPTOwla5tQHB83LXNF4Y+Sv/Mu4Uu0tKWz02FrLgA5cuJZfba9QNULTZLTNUgUXIB0m/dq5Rx17IyCfLQ2XngmfFkfnRdRSK7kGnIXvO2/LOKD50JsTf2vz0RQIdw6cEmdl+Aga7i8QIBEQ==");
		private static readonly byte[] PeerPublicKey = Convert.FromBase64String("MIGdMA0GCSqGSIb3DQEBAQUAA4GLADCBhwKBgQDTJKLLO7wjCHz80kpnisqcPDQvA9voNY5QuAA+bOWeqvl4gmPSiylzQZzldS+n/M5p4o1PRS24WAO+kPBHCf4ETAns8M02MFwxH/FlQnbvMfi9zutJkQAu3Hq4293rHz+iCQW/MWYB5IfzFBnWtEdjkhqHsGy6sZMMe+qx/F1rcQIBEQ==");

		private static readonly SHA1Managed SHA1 = new SHA1Managed();
		private static readonly MD5CryptoServiceProvider MD5 = new MD5CryptoServiceProvider();
		private static readonly Encoding SJIS = Encoding.GetEncoding(932);

		#region Verifier
		// ピアの鍵だけ期限と鍵の順番が逆らしい…どうしてこうなった。
		public static bool VerifyPeerData(PeerSignedData signedData, DateTime nowTime)
			=> Verify(signedData.PublicKey, signedData.PublicKeyExpiration, signedData.PublicKeySignature, PeerPublicKey, nowTime, true)
			&& Verify(MD5.ComputeHash(SJIS.GetBytes(signedData.Data)), signedData.Expiration, signedData.Signature, signedData.PublicKey, nowTime);

		public static bool VerifyServerData(ServerSignedData signedData, DateTime nowTime)
			=> Verify(MD5.ComputeHash(SJIS.GetBytes(signedData.Data)), signedData.Expiration, signedData.Signature, ServerPublicKey, nowTime);

		private static bool Verify(byte[] data, DateTime expiration, byte[] signature, byte[] publicKey, DateTime nowTime, bool reverseData = false)
		{
			if (expiration < nowTime)
				return false;

			var expirationBytes = SJIS.GetBytes(expiration.ToString("yyyy/MM/dd HH-mm-ss"));

			var signedData = new byte[expirationBytes.Length + data.Length];
			if (reverseData)
			{
				Buffer.BlockCopy(data, 0, signedData, 0, data.Length);
				Buffer.BlockCopy(expirationBytes, 0, signedData, data.Length, expirationBytes.Length);
			}
			else
			{
				Buffer.BlockCopy(expirationBytes, 0, signedData, 0, expirationBytes.Length);
				Buffer.BlockCopy(data, 0, signedData, expirationBytes.Length, data.Length);
			}

			using var rsa = new RSACryptoServiceProvider();
			rsa.ImportParameters(PKCS8DERDecoder.DecodePublicKey(publicKey));

			var deformatter = new RSAPKCS1SignatureDeformatter(rsa);
			deformatter.SetHashAlgorithm("SHA1");

			return deformatter.VerifySignature(SHA1.ComputeHash(signedData), signature);
		}
		#endregion

		#region Signer
		public static PeerSignedData SignPeer(string data, RsaKey key, DateTime nowTime)
		{
			var expiration = nowTime.AddMinutes(1);

			return new PeerSignedData(data,
				Sign(key.PrivateKey, MD5.ComputeHash(SJIS.GetBytes(data)), expiration),
				expiration, key.PublicKey, key.Signature, key.Expiration);
		}

		private static byte[] Sign(byte[] data, byte[] privateKey, DateTime expiration)
		{
			byte[] expirationBytes = SJIS.GetBytes(expiration.ToString("yyyy/MM/dd HH-mm-ss"));

			byte[] signedData = new byte[expirationBytes.Length + data.Length];
			Buffer.BlockCopy(expirationBytes, 0, signedData, 0, expirationBytes.Length);
			Buffer.BlockCopy(data, 0, signedData, expirationBytes.Length, data.Length);

			using var rsa = new RSACryptoServiceProvider();
			rsa.ImportParameters(PKCS8DERDecoder.DecodePrivateKey(privateKey));

			var formatter = new RSAPKCS1SignatureFormatter(rsa);
			formatter.SetHashAlgorithm("SHA1");

			return formatter.CreateSignature(SHA1.ComputeHash(data));
		}
		#endregion
	}
}
