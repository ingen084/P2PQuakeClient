using System;

namespace P2PQuakeClient
{
	public class EpspPacketParseException : Exception
	{
		public EpspPacketParseException(string message) : base(message)
		{
		}
		public EpspPacketParseException(string message, Exception innerException) : base(message, innerException)
		{
		}
	}
}
