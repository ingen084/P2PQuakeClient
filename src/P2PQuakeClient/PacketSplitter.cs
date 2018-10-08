using System;
using System.Collections.Generic;
using System.Text;

namespace P2PQuakeClient
{
	public class PacketSplitter
	{
		public Encoding Encoding { get; } = Encoding.GetEncoding("Shift_JIS");

		private readonly object _lockObject = new object();
		private byte[] _pendingBytes;

		public IEnumerable<string> ParseAndSplit(byte[] bytes, int byteCount)
		{
			lock (_lockObject)
			{
				//合成用バッファ
				byte[] buffer = new byte[(_pendingBytes?.Length ?? 0) + byteCount];

				if (_pendingBytes != null)
					Buffer.BlockCopy(_pendingBytes, 0, buffer, 0, _pendingBytes.Length);
				Buffer.BlockCopy(bytes, 0, buffer, _pendingBytes?.Length ?? 0, byteCount);

				var lastIndex = 0;
				while (true)
				{
					var index = Array.IndexOf<byte>(buffer, 0x0D, lastIndex);
					if (index == -1 || buffer.Length == index + 1 || buffer[index + 1] != 0x0A)
						break;
					var result = new byte[index - lastIndex];
					Buffer.BlockCopy(buffer, lastIndex, result, 0, result.Length);
					lastIndex = index + 2;
					yield return Encoding.GetString(result);
				}
				if (buffer.Length == lastIndex)
				{
					_pendingBytes = null;
					yield break;
				}
				_pendingBytes = new byte[buffer.Length - lastIndex];
				Buffer.BlockCopy(buffer, lastIndex, _pendingBytes, 0, _pendingBytes.Length);
			}
		}
	}
}
