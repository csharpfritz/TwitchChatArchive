using Microsoft.Azure.Cosmos.Table;
using System;

namespace Fritz.TwitchChatArchive.Data
{

  public class CurrentSubscription : TableEntity
  {

    private string _ChannelId;
    public string ChannelId
    {
      get { return _ChannelId; }
      set
      {
        RowKey = value;
        _ChannelId = value;
      }
    }

    private DateTime _ExpirationDateTimeUtc;
    public DateTime ExpirationDateTimeUtc
    {
      get { return _ExpirationDateTimeUtc; }
      set
      {
        _ExpirationDateTimeUtc = value;
        PartitionKey = _ExpirationDateTimeUtc.ToString("yyyyMMdd");
      }
    }

  }

}
