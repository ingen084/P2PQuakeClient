using P2PQuakeClient.Connection;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace P2PQuakeClient.Test
{
	public class PacketTests
	{
		[Fact]
		public void PacketSplitTest()
		{
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
			var encoding = Encoding.GetEncoding("Shift_JIS");
			var splitter = new PacketSplitter();

			IEnumerable<string> Split(params string[] input)
			{
				foreach (var str in input)
				{
					var bytes = encoding.GetBytes(str);
					foreach (var result in splitter.WriteAndSplit(bytes, bytes.Length))
						yield return result;
				}
			}

			Assert.Equal("test", Split("test\r\n").ToArray().First());
			Assert.Equal("test", Split("te", "st\r\n").ToArray().First());
			Assert.Equal("test/test", string.Join("/", Split("test\r\nte", "st\r\n").ToArray()));
		}
		[Fact]
		public void ParsePacketInstanceTest()
		{
			//長さ不足
			Assert.ThrowsAny<EpspPacketParseException>(() => new EpspPacket("fe"));
			//形式エラー
			Assert.ThrowsAny<EpspPacketParseException>(() => new EpspPacket("12345"));
			//コードの解析エラー
			Assert.ThrowsAny<EpspPacketParseException>(() => new EpspPacket("xxx 1"));
			//経由数の解析エラー
			Assert.ThrowsAny<EpspPacketParseException>(() => new EpspPacket("000 x"));
			//コンテンツなしパケット解析チェック
			Assert.Equal(0, new EpspPacket("000 1").Code);
			Assert.Equal(1U, new EpspPacket("000 1").HopCount);
			Assert.Equal(123U, new EpspPacket("000 123").HopCount);
			//コンテンツありパケット解析チェック
			Assert.Equal(123U, new EpspPacket("000 123 hogehoge").HopCount);
			Assert.Equal("hogehoge", new EpspPacket("000 123 hogehoge").Data[0]);
			Assert.Equal("hogehoge", string.Join("", new EpspPacket("000 123 hoge:hoge").Data));
		}
	}
}
