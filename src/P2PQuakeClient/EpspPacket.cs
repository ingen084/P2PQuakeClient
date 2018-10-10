﻿namespace P2PQuakeClient
{
	public class EpspPacket
	{
		/// <summary>
		/// パケット文字列を読み込み、インスタンスを生成します。
		/// </summary>
		/// <param name="message">パケット文字列</param>
		public EpspPacket(string message)
		{
			if (message.Length < 5)
				throw new EpspException("メッセージが短すぎます。");
			//COD HOPCOUNT DATA
			if (message[3] != ' ')
				throw new EpspException("パケットの形式が不正です。");
			if (!int.TryParse(message.Substring(0, 3), out var code))
				throw new EpspException("コードの解析に失敗しました。");
			Code = code;
			var hopCountEndindex = message.Substring(4).IndexOf(' ');
			if (hopCountEndindex == -1)
				hopCountEndindex = message.Length - 4;
			if (!uint.TryParse(message.Substring(4, hopCountEndindex), out var hopCount))
				throw new EpspException("経由数の解析に失敗しました。");
			HopCount = hopCount;
			if (hopCountEndindex == message.Length - 4)
				return;
			Data = message.Substring(hopCountEndindex + 5).Split(':');
		}
		/// <summary>
		/// 各種パラメタからパケットを生成します。
		/// </summary>
		/// <param name="code">コード</param>
		/// <param name="hopCount">経由数</param>
		/// <param name="data">パケットのデータ</param>
		public EpspPacket(int code, uint hopCount, params string[] data)
		{
			Code = code;
			HopCount = hopCount;
			Data = data;
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

		/// <summary>
		/// インスタンスからパケット文字列を生成します。
		/// </summary>
		/// <returns>生成されたパケット文字列</returns>
		public string ToPacketString() => $"{Code.ToString("000")} {HopCount}{((Data?.Length ?? 0) > 0 ? " " + string.Join(':', Data) : "")}";
	}
}