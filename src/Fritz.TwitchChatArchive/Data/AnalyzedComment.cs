using Microsoft.Azure.Cosmos.Table;
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

		public string UserNameHash { get; set; }

		public DateTimeOffset CommentDate { get; set; }

		public double PositiveSentiment { get; set; }
		public double NegativeSentiment { get; set; }
		public double NeutralSentiment { get; set; }

		public string IdentifiedObjects { get; set; }
		public string Text { get; set; }
	}

}
