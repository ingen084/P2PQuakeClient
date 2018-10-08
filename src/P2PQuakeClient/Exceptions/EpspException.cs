using System;

namespace P2PQuakeClient
{
	public class EpspException : Exception
	{
		public EpspException(string message) : base(message)
		{
		}
		public EpspException(string message, Exception innerException) : base(message, innerException)
		{
		}
	}
}
