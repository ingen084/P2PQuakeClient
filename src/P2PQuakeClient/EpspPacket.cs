namespace P2PQuakeClient
{
	public class EpspPacket
	{
		public EpspPacket(string message)
		{
			if (message.Length < 5)
				throw new EpspPacketParseException("メッセージが短すぎます。");
			//COD HOPCOUNT DATA
			if (message[3] != ' ')
				throw new EpspPacketParseException("パケットの形式が不正です。");
			if (!int.TryParse(message.Substring(0, 3), out var code))
				throw new EpspPacketParseException("コードの解析に失敗しました。");
			Code = code;
			var hopCountEndindex = message.Substring(4).IndexOf(' ');
			if (hopCountEndindex == -1)
				hopCountEndindex = message.Length - 4;
			if (!uint.TryParse(message.Substring(4, hopCountEndindex), out var hopCount))
				throw new EpspPacketParseException("経由数の解析に失敗しました。");
			HopCount = hopCount;
			if (hopCountEndindex == message.Length - 4)
				return;
			Data = message.Substring(hopCountEndindex + 5).Split(':');
		}

		/// <summary>
		/// パケットのコード
		/// </summary>
		public int Code { get; set; }
		/// <summary>
		/// パケットの経由数
		/// </summary>
		public uint HopCount { get; set; }
		/// <summary>
		/// パケットのデータ
		/// </summary>
		public string[] Data { get; set; }
	}
}
