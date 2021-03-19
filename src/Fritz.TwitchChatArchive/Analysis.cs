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
using Microsoft.WindowsAzure.Storage.Blob;

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

		private async Task Analyze(IEnumerable<Comment> comments, Content content, ILogger logger)
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
						EntryId = content.Id.ToString(),
						ContentType = content.ContentType,
						ContentSource = content.Source,
						ContentTitle = content.Title,
						ContentPublishDate = content.PublicationDate,
						CommentDate = comments.Skip(i).First().created_at,
						CommentId = comments.Skip(i).First()._id,
						IdentifiedObjects = string.Join(", ", entities.Skip(i).First().Entities.Select(e => e.Text).ToArray()),
						PositiveSentiment = sentiment.Skip(i).First().DocumentSentiment.ConfidenceScores.Positive,
						NegativeSentiment = sentiment.Skip(i).First().DocumentSentiment.ConfidenceScores.Negative,
						NeutralSentiment = sentiment.Skip(i).First().DocumentSentiment.ConfidenceScores.Neutral,
						Text = comments.Skip(i).First().message.body,
						Timestamp = comments.Skip(i).First().created_at,
						UserNameHash = comments.Skip(i).First().commenter?.display_name?.GetHashCode().ToString() ?? "null"
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

		private async Task Analyze(IEnumerable<AnalyzedComment> comments,  ILogger logger)
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
			[TimerTrigger("* * * * 5 *", RunOnStartup = false)] TimerInfo timer,
			ILogger logger
		)
		{

			var account = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("TwitchChatStorage"));
			var blobClient = account.CreateCloudBlobClient();
			var container = blobClient.GetContainerReference("chatlog");
			var filename = "20210208_C# with CSharpFritz - Introducing ASP.NET Core Authentication and Authorization.json";
			var blob = container.GetBlockBlobReference(filename);

			var contents = await blob.DownloadTextAsync();
			var comments = JsonSerializer.Deserialize<IEnumerable<Comment>>(contents);

			var theContent = new Content
			{
				ContentType = ContentType.LiveVideo,
				Source = "VisualStudio@Twitch",
				PublicationDate = DateTimeOffset.ParseExact(filename.Split('_')[0], "yyyyMMdd", CultureInfo.InvariantCulture),
				Title = filename.Split('_')[1]
			};
			await Analyze(comments, theContent, logger);


		}

		[FunctionName("InitializeTwitchChat")]
		public async Task InitializeTwitchChat(
			[TimerTrigger("* * * * 5 *", RunOnStartup = false)] TimerInfo timer,
			ILogger logger
		)
		{

			var folder = "chatlog";
			var account = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("TwitchChatStorage"));
			var blobClient = account.CreateCloudBlobClient();
			var container = blobClient.GetContainerReference(folder);

			// Get all files and prepare to loop
			BlobContinuationToken token = null;
			var files = new List<IListBlobItem>();
			do
			{

				var blobSegment = await container.ListBlobsSegmentedAsync(token);
				token = blobSegment.ContinuationToken;
				files.AddRange(blobSegment.Results);

			} while (token != null);

			Parallel.ForEach(files,
				new ParallelOptions
				{
					MaxDegreeOfParallelism = 4
				},
				file =>
				{

					var filename = file.Uri.LocalPath.Substring(folder.Length+2);  //"20210208_C# with CSharpFritz - Introducing ASP.NET Core Authentication and Authorization.json";
					var title = filename.Split('_')[1].Replace(".json","");
					if (_Repo.TitleExists(title)) return;
					var publishDate = DateTimeOffset.ParseExact(filename.Split('_')[0], "yyyyMMdd", CultureInfo.InvariantCulture);

					// Download the blob
					var blob = container.GetBlockBlobReference(filename);
					string contents = string.Empty;

					try
					{
						contents = blob.DownloadTextAsync().GetAwaiter().GetResult();
					} catch (Exception ex)
					{
						logger.LogError(ex, $"Error while downloading blob '{filename}'");
						return;
					}
					var comments = JsonSerializer.Deserialize<IEnumerable<Comment>>(contents);

					var theContent = new Content
					{
						ContentType = ContentType.LiveVideo,
						Source = "VisualStudio@Twitch",
						PublicationDate = publishDate,
						Title = title
					};

					Analyze(comments, theContent, logger).GetAwaiter().GetResult();

				});

		}

		[FunctionName("PrototypeLoadPubble")]
		public async Task PrototypeLoadPubble(
			[TimerTrigger("* * * * 5 *", RunOnStartup = true)]TimerInfo Timer,
			ILogger logger
		) {

			var ds = ReadExcelAsDataSet(@"C:\dev\TwitchChatArchive\posts_export_learntv_posts_fb1a4d63ccc140c5921316a279a789e6.xlsx");

			// TODO: Identify HOW to qualify the pubble content
			var theContent = new Content
			{
				ContentType = ContentType.LiveVideo,
				Source = "LearnTV",
				PublicationDate = new DateTimeOffset(new DateTime(2021, 1, 27)),
				Title = "January 27, 2021 @ LearnTV"
			};

			var comments = ConvertPubbleToComments(ds, theContent);

			await Analyze(comments, logger);

		}

		private IEnumerable<AnalyzedComment> ConvertPubbleToComments(DataSet ds, Content theContent)
		{

			var tbl = ds.Tables["Questions"];
			var outList = new List<AnalyzedComment>();
			foreach (DataRow record in tbl.Rows)
			{

				var cellOne = record[0].ToString();
				if (string.IsNullOrEmpty(cellOne) || cellOne == "App ID") continue;

				outList.Add(new AnalyzedComment
				{
					EntryId = theContent.Id.ToString(),
					ContentId = cellOne,
					ContentType = theContent.ContentType,
					ContentSource = theContent.Source,
					ContentTitle = theContent.Title,
					ContentPublishDate = theContent.PublicationDate,
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
			[TimerTrigger("* * * * 5 *", RunOnStartup = true)] TimerInfo Timer,
			ILogger logger
		) {

			var ds = ReadExcelAsDataSet(@"C:\dev\TwitchChatArchive\Xamarin Blog comments.xlsx");
			var comments = ConvertBlogToComments(ds, "Xamarin@Blog");

			await Analyze(comments, logger);

		}

		private IEnumerable<AnalyzedComment> ConvertBlogToComments(DataSet ds, string blogSource)
		{

			// TODO: Pick up here

			var tbl = ds.Tables["in"];
			var outList = new List<AnalyzedComment>();
			foreach (DataRow record in tbl.Rows)
			{

				var cellOne = record[0].ToString();
				if (string.IsNullOrEmpty(cellOne) || cellOne == "comment_ID") continue;

				outList.Add(new AnalyzedComment
				{
					EntryId = cellOne,
					ContentId = record[11].ToString(),
					ContentType = ContentType.BlogPost,
					ContentSource = blogSource,
					ContentTitle = record[12].ToString(),
					ContentPublishDate = ParseGoofyExcelDateFormats(record[14].ToString()),
					CommentDate = ParseGoofyExcelDateFormats(record[5].ToString()),
					CommentId = Guid.NewGuid().ToString(),
					Text = record[6].ToString(),
					UserNameHash = record[2].GetHashCode().ToString()
				});;
			}

			return outList;

		}



#endif

		private DateTimeOffset ParseGoofyExcelDateFormats(string excelDate) {

			if (DateTimeOffset.TryParseExact(excelDate, "M/d/yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedMdyDate))
				return parsedMdyDate;

			if (DateTimeOffset.TryParseExact(excelDate, "dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDmyDate))
				return parsedDmyDate;

			throw new Exception("Not a known date format");

		}


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
