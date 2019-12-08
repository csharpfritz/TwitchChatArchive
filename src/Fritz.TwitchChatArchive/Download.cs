using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Fritz.TwitchChatArchive.Data;
using Fritz.TwitchChatArchive.Messages;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

namespace Fritz.TwitchChatArchive
{
	public class Download : BaseFunction
	{
		private readonly IHttpClientFactory _HttpClientFactory;
		private readonly IConfiguration _Configuration;

		public Download(IHttpClientFactory httpClientFactory, IConfiguration configuration) : base(httpClientFactory, configuration)
		{
		}


		[FunctionName("DownloadChat")]
		public async Task DownloadChat(
		[ServiceBusTrigger("EndOfStream", "downloadchat", Connection = "ServiceBusConnectionString")]CompletedStream completedStream,
		[Blob("chatlog", FileAccess.ReadWrite, Connection = "TwitchChatStorage")] CloudBlobContainer container,
			ILogger log)
		{

			if (string.IsNullOrEmpty(completedStream.VideoId)) return;

			var downloadTask = DownloadChatForVideo(completedStream.VideoId);
			Task.WaitAll(downloadTask, container.CreateIfNotExistsAsync());
			var blob = container.GetBlockBlobReference($"{completedStream.VideoId}.json");
			await blob.UploadTextAsync(JsonConvert.SerializeObject(downloadTask.Result));

			log.LogInformation($"C# ServiceBus topic trigger function processed message: {completedStream}");

		}

		private async Task<IEnumerable<Comment>> DownloadChatForVideo(string videoId)
		{

			// Cheer 300 codingbandit 29/11/19 
			// Cheer 100 MattLeibow 29/11/19 

			var comments = new List<Comment>();

			using (var client = GetHttpClient($"https://api.twitch.tv/v5/videos/{videoId}/comments"))
			{

				string rawString = await client.GetStringAsync($"");
				var rootData = JsonConvert.DeserializeObject<CommentsRootData>(rawString);
				while (!string.IsNullOrEmpty(rootData._next))
				{
					comments.AddRange(rootData.comments);
					rootData = JsonConvert.DeserializeObject<CommentsRootData>(await client.GetStringAsync($"?cursor={rootData._next}"));
				}
				comments.AddRange(rootData.comments);

				return comments;

			}

		}



	}
}
