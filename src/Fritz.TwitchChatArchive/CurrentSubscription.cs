using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fritz.TwitchChatArchive
{

  public class CurrentSubscription : TableEntity
  {

    private string _ChannelId;
    public string ChannelId { 
      get { return _ChannelId; }
      set { 
        RowKey = value;
        _ChannelId = value;
      }
    }

    private DateTime _ExpirationDateTimeUtc;
    public DateTime ExpirationDateTimeUtc { 
      get { return _ExpirationDateTimeUtc; }
      set
      {
        _ExpirationDateTimeUtc = value;
        PartitionKey = _ExpirationDateTimeUtc.ToString("yyyyMMdd");
      }
    }

  }

}
