using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System.Threading.Tasks;

namespace Fritz.TwitchChatArchive.Data
{
	public class AnalyzedCommentRepository : BaseTableRepository<AnalyzedComment>
	{
		public AnalyzedCommentRepository(IConfiguration configuration) : base(configuration) { }

		protected override string ConnectionStringName => "AnalysisStorage";

		public bool TitleExists(string title) {

			var table = GetCloudTable(TableName);
			var query = new TableQuery<AnalyzedComment>
			{
				FilterString = TableQuery.GenerateFilterCondition("ContentTitle", QueryComparisons.Equal, title)
			};

			return table.ExecuteQuery(query).Any();

		}

	}

}
