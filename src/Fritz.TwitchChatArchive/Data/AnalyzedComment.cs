using Microsoft.Azure.Cosmos.Table;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fritz.TwitchChatArchive.Data
{

	public class AnalyzedComment : TableEntity
	{

		public string EntryId { 
			get { return PartitionKey; }
			set { PartitionKey = value; }
		}

		public string CommentId { 
			get { return RowKey; }
			set { RowKey = value; }
		}

		/// <summary>
		/// Reference Id from the foreign content system
		/// </summary>
		public string ContentId { get; set; }

		public string ContentType { get; set; }

		public string ContentSource { get; set; }

		public string ContentTitle { get; set; }

		public DateTimeOffset ContentPublishDate { get; set; }

		public string UserNameHash { get; set; }

		public DateTimeOffset CommentDate { get; set; }

		public double PositiveSentiment { get; set; }
		public double NegativeSentiment { get; set; }
		public double NeutralSentiment { get; set; }

		public string IdentifiedObjects { get; set; }
		public string Text { get; set; }
	}

}
