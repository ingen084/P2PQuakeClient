using System;
using System.Collections.Generic;
using System.Text;

namespace P2PQuakeClient
{
	public interface IEpspLogger
	{
		void Trace(string message);
		void Debug(string message);
		void Info(string message);
		void Warning(string message);
		void Error(string message);
	}
}
