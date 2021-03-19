using Fritz.TwitchChatArchive.Data;
using System;

namespace Fritz.TwitchChatArchive
{
	internal class Content
	{

		public Guid Id { get; set; } = Guid.NewGuid();

		public string ContentType { get; set; }

		public string Source { get; set; }

		public string Title { get; set; }

		public DateTimeOffset PublicationDate { get; set; }

	}
}