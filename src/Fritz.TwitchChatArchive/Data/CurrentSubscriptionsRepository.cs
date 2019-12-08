using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Configuration;


namespace Fritz.TwitchChatArchive.Data {

	// Cheer 1001 baldbeardedbuilder 8/12/19 

	public class CurrentSubscriptionsRepository {

		private IConfiguration _Configuration;

		public CurrentSubscriptionsRepository(IConfiguration configuration) {

			_Configuration = configuration;

		}

		private CloudTable GetCloudTable(string tableName) {		

			var account = CloudStorageAccount.Parse(_Configuration["TwitchChatStorage"]);
			var client = account.CreateCloudTableClient(new TableClientConfiguration());
			return client.GetTableReference(tableName);


		}

		public async Task<IEnumerable<CurrentSubscription>> GetExpiringSubscriptions() {	
    
			var table = GetCloudTable("CurrentSubscriptions");

      var partitionKey = DateTime.UtcNow.ToString("yyyyMMdd");
      var query = new TableQuery<CurrentSubscription>
      {
        FilterString = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, partitionKey)
      };

      TableContinuationToken token = null;
      var outList = new List<CurrentSubscription>();
      while (true)
      {
        var results = await table.ExecuteQuerySegmentedAsync<CurrentSubscription>(query.Take(10), token);
        if (results.Results.Count == 0) break;

        outList.AddRange(results.Results);

        if (results.ContinuationToken != null)
        {
          token = results.ContinuationToken;
        }
        else
        {
          break;
        }

      }

      return outList;

    }

    public Task AddSubscription(CurrentSubscription sub) {

      var table = GetCloudTable("CurrentSubscriptions");

      return table.ExecuteAsync(TableOperation.InsertOrReplace(sub));

    }

    public Task RemoveSubscription(CurrentSubscription sub) {

      var table = GetCloudTable("CurrentSubscriptions");

      return table.ExecuteAsync(TableOperation.Delete(sub));

    }



  }

}