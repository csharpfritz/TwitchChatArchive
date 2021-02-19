using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Azure.AI.TextAnalytics;
using ExcelDataReader;
using Fritz.TwitchChatArchive.Data;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;

namespace Fritz.TwitchChatArchive
{
	public class Analysis
	{
		private readonly IConfiguration _Configuration;
		private readonly AnalyzedCommentRepository _Repo;
		private readonly HttpClient _Client;

		private readonly AzureKeyCredential credentials;
		private readonly Uri endpoint;

		public Analysis(IConfiguration configuration, IHttpClientFactory httpClientFactory)
		{
			_Configuration = configuration;
			_Repo = new AnalyzedCommentRepository(configuration);

			credentials = new AzureKeyCredential(_Configuration["CognitiveServicesKey"]);
			endpoint = new Uri(_Configuration["CognitiveServicesEndPoint"]);
		}


		//[FunctionName("ChatAnalysis")]
		//private async Task Run(
		//[BlobTrigger("chatlog/{name}", Connection = "TwitchChatStorage")] Stream myBlob, 
		//string name, 
		//ILogger log)
		//{

		//	var comments = await JsonSerializer.DeserializeAsync<IEnumerable<Comment>>(myBlob);

		//	await Analyze(comments);

		//	log.LogInformation($"C# Blob trigger function Processed blob\n Name:{name} \n Size: {myBlob.Length} Bytes");
		//}

		private async Task Analyze(IEnumerable<Comment> comments, ILogger logger)
		{

			var client = new TextAnalyticsClient(endpoint, credentials);

			var recordCount = 0;
			var sentiment = new List<AnalyzeSentimentResult>();
			var entities = new List<RecognizeEntitiesResult>();
			while (recordCount < comments.Count())
			{

				var docTask = client.AnalyzeSentimentBatchAsync(comments.Skip(recordCount).Take(5).Select(c => c.message.body));
				var entTask = client.RecognizeEntitiesBatchAsync(comments.Skip(recordCount).Take(5).Select(c => c.message.body));

				await Task.WhenAll(docTask, entTask);

				recordCount += docTask.Result.Value.Count;
				logger.LogInformation($"Analyzed {recordCount} of {comments.Count()}");

				sentiment.AddRange(docTask.Result.Value);
				entities.AddRange(entTask.Result.Value);

			}

			var tasks = new List<Task>();
			for (var i = 0; i < sentiment.Count; i++)
			{

				AnalyzedComment ac = null;
				try
				{
					ac = new AnalyzedComment
					{
						CommentDate = comments.Skip(i).First().created_at,
						CommentId = comments.Skip(i).First()._id,
						EntryId = comments.Skip(i).First().content_id,
						IdentifiedObjects = string.Join(", ", entities.Skip(i).First().Entities.Select(e => e.Text).ToArray()),
						PositiveSentiment = sentiment.Skip(i).First().DocumentSentiment.ConfidenceScores.Positive,
						NegativeSentiment = sentiment.Skip(i).First().DocumentSentiment.ConfidenceScores.Negative,
						NeutralSentiment = sentiment.Skip(i).First().DocumentSentiment.ConfidenceScores.Neutral,
						Text = comments.Skip(i).First().message.body,
						Timestamp = comments.Skip(i).First().created_at,
						UserNameHash = comments.Skip(i).First().commenter.display_name.GetHashCode().ToString()
					};
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex);
				}

				tasks.Add(_Repo.AddOrUpdate(ac));

			}

			await Task.WhenAll(tasks);

		}

		private async Task Analyze(IEnumerable<AnalyzedComment> comments, ILogger logger)
		{

			var client = new TextAnalyticsClient(endpoint, credentials);

			var recordCount = 0;
			var sentiment = new List<AnalyzeSentimentResult>();
			var entities = new List<RecognizeEntitiesResult>();
			while (recordCount < comments.Count())
			{

				var docTask = client.AnalyzeSentimentBatchAsync(comments.Skip(recordCount).Take(5).Select(c => c.Text));
				var entTask = client.RecognizeEntitiesBatchAsync(comments.Skip(recordCount).Take(5).Select(c => c.Text));

				await Task.WhenAll(docTask, entTask);

				recordCount += docTask.Result.Value.Count;
				logger.LogInformation($"Analyzed {recordCount} of {comments.Count()}");

				sentiment.AddRange(docTask.Result.Value);
				entities.AddRange(entTask.Result.Value);

			}

			var tasks = new List<Task>();
			for (var i = 0; i < sentiment.Count; i++)
			{

				AnalyzedComment ac = comments.Skip(i).First();
				try
				{

					ac.IdentifiedObjects = string.Join(", ", entities.Skip(i).First().Entities.Where(e => !string.IsNullOrEmpty(e.Text)).Select(e => e.Text).ToArray());
					ac.PositiveSentiment = sentiment.Skip(i).First().DocumentSentiment.ConfidenceScores.Positive;
					ac.NegativeSentiment = sentiment.Skip(i).First().DocumentSentiment.ConfidenceScores.Negative;
					ac.NeutralSentiment = sentiment.Skip(i).First().DocumentSentiment.ConfidenceScores.Neutral;

				}
				catch (Exception ex)
				{
					Console.WriteLine(ex);
				}

				tasks.Add(_Repo.AddOrUpdate(ac));

			}

			await Task.WhenAll(tasks);

		}

#if DEBUG
		[FunctionName("PrototypeLoadChat")]
		public async Task PrototypeLoadChat(
			[TimerTrigger("* * * * 5 *", RunOnStartup =false)]TimerInfo timer,
			ILogger logger
		) 
		{

			var account = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("TwitchChatStorage"));
			var blobClient = account.CreateCloudBlobClient();
			var container = blobClient.GetContainerReference("chatlog");
			var blob = container.GetBlockBlobReference("20201222_C# Corner with Instafluff: Mystery Detective Mod for Stardew Valley!.json");

			var contents = await blob.DownloadTextAsync();
			var comments = JsonSerializer.Deserialize<IEnumerable<Comment>>(contents);

			await Analyze(comments, logger);


		}

		[FunctionName("PrototypeLoadPubble")]
		public async Task PrototypeLoadPubble(
			[TimerTrigger("* * * * 5 *", RunOnStartup = false)]TimerInfo Timer,
			ILogger logger
		) {

			var ds = ReadExcelAsDataSet(@"C:\dev\TwitchChatArchive\posts_export_learntv_posts_fb1a4d63ccc140c5921316a279a789e6.xlsx");
			var comments = ConvertPubbleToComments(ds);

			await Analyze(comments, logger);

		}

		private IEnumerable<AnalyzedComment> ConvertPubbleToComments(DataSet ds)
		{

			var tbl = ds.Tables["Questions"];
			var outList = new List<AnalyzedComment>();
			foreach (DataRow record in tbl.Rows)
			{

				var cellOne = record[0].ToString();
				if (string.IsNullOrEmpty(cellOne) || cellOne == "App ID") continue;

				outList.Add(new AnalyzedComment
				{
					EntryId = cellOne,
					CommentDate = DateTimeOffset.ParseExact(record[6].ToString(), "yyyy-MM-ddTHH:mm:ss \"GMT\"", CultureInfo.InvariantCulture),
					CommentId = Guid.NewGuid().ToString(),
					Text = record[5].ToString(),
					UserNameHash = record[1].GetHashCode().ToString()
				});
			}

			return outList;

		}

		[FunctionName("PrototypeLoadBlog")]
		public async Task PrototypeLoadBlog(
			[TimerTrigger("* * * * 5 *", RunOnStartup = false)] TimerInfo Timer,
			ILogger logger
		) {

			var ds = ReadExcelAsDataSet(@"C:\dev\TwitchChatArchive\Xamarin Blog comments.xlsx");
			var comments = ConvertBlogToComments(ds);

			await Analyze(comments, logger);

		}

		private IEnumerable<AnalyzedComment> ConvertBlogToComments(DataSet ds)
		{

			// TODO: Pick up here

			var tbl = ds.Tables["in"];
			var outList = new List<AnalyzedComment>();
			foreach (DataRow record in tbl.Rows)
			{

				var cellOne = record[0].ToString();
				if (string.IsNullOrEmpty(cellOne) || cellOne == "App ID") continue;

				outList.Add(new AnalyzedComment
				{
					EntryId = cellOne,
					CommentDate = DateTimeOffset.ParseExact(record[6].ToString(), "yyyy-MM-ddTHH:mm:ss \"GMT\"", CultureInfo.InvariantCulture),
					CommentId = Guid.NewGuid().ToString(),
					Text = record[5].ToString(),
					UserNameHash = record[1].GetHashCode().ToString()
				});
			}

			return outList;

		}



#endif

		private DataSet ReadExcelAsDataSet(string path) {

			using (var stream = File.Open(path, FileMode.Open, FileAccess.Read))
			{
				// Auto-detect format, supports:
				//  - Binary Excel files (2.0-2003 format; *.xls)
				//  - OpenXml Excel files (2007 format; *.xlsx, *.xlsb)
				using (var reader = ExcelReaderFactory.CreateReader(stream))
				{

					return reader.AsDataSet();

				}
			}


		}

	}
}
