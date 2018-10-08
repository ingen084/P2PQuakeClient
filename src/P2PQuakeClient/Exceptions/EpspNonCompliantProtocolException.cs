using System;

namespace P2PQuakeClient
{
	public class EpspNonCompliantProtocolException : EpspException
	{
		public EpspNonCompliantProtocolException(string message) : base(message)
		{
		}

		public EpspNonCompliantProtocolException(string message, Exception innerException) : base(message, innerException)
		{
		}
	}
}
