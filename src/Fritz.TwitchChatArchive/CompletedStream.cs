using System;
using System.Collections.Generic;
using System.Text;

namespace Fritz.TwitchChatArchive
{

	[Serializable]
	public class CompletedStream
	{

		public string ChannelId { get; set; }

		public string VideoId { get; set; }

		[NonSerialized]
		public static readonly CompletedStream Empty = new CompletedStream();

		public override string ToString()
		{
			return $"ChannelId: {ChannelId}, VideoId: {VideoId}";
		}

	}

}
