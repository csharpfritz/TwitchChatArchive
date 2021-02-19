using Microsoft.Extensions.Configuration;

namespace Fritz.TwitchChatArchive.Data
{
	public class AnalyzedCommentRepository : BaseTableRepository<AnalyzedComment>
	{
		public AnalyzedCommentRepository(IConfiguration configuration) : base(configuration)
		{
		}
	}

}
