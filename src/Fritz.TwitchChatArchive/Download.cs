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
using Microsoft.WindowsAzure.Storage.Queue;
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
		[ServiceBusTrigger("EndOfStream", "downloadchat", Connection = "ServiceBusConnectionString")] CompletedStream completedStream,
		[Blob("chatlog", FileAccess.ReadWrite, Connection = "TwitchChatStorage")] CloudBlobContainer container,
			ILogger log)
		{

			if (string.IsNullOrEmpty(completedStream.VideoId))
			{
				log.LogError($"Unable to downloadchat - no VideoId submitted for {completedStream.ChannelName}");
				return;
			}

			log.LogInformation($"Attempting to download chat for {completedStream.ChannelName} - VideoId: {completedStream.VideoId}");

			var downloadTask = DownloadChatForVideo(completedStream.VideoId);
			var getTitleAndDateTask = GetTitleForVideo(completedStream.VideoId);
			Task.WaitAll(downloadTask, getTitleAndDateTask, container.CreateIfNotExistsAsync());

			var titleAndDate = getTitleAndDateTask.Result;
			var fileName = $"{titleAndDate.publishDate.ToString("yyyyMMdd")}_{titleAndDate.title}.json";
			var blob = container.GetBlockBlobReference(fileName);
			await blob.UploadTextAsync(JsonConvert.SerializeObject(downloadTask.Result));

			var client = GetHttpClient($"https://lemon-bush-027f2e90f.azurestaticapps.net");
			_ = client.GetAsync($"/api/youtubechat?twitchid={fileName}");

			log.LogInformation($"C# ServiceBus topic trigger function processed message: {completedStream}");

		}

		[FunctionName("DownloadChatByVideoId")]
		public async Task DownloadChatById(
		[QueueTrigger("chattodownload-byvideoid", Connection = "TwitchChatStorage")] CloudQueueMessage msg,
		[Blob("chatlog", FileAccess.ReadWrite, Connection = "TwitchChatStorage")] CloudBlobContainer container,
			ILogger log)
		{

			var videoId = msg.AsString.Trim();

			var downloadTask = DownloadChatForVideo(videoId);
			var getTitleAndDateTask = GetTitleForVideo(videoId);
			Task.WaitAll(downloadTask, getTitleAndDateTask, container.CreateIfNotExistsAsync());

			var titleAndDate = getTitleAndDateTask.Result;
			var blob = container.GetBlockBlobReference($"{titleAndDate.publishDate.ToString("yyyyMMdd")}_{titleAndDate.title}.json");
			await blob.UploadTextAsync(JsonConvert.SerializeObject(downloadTask.Result));

			log.LogInformation($"Downloaded chat for video with id: {msg.AsString}");

		}

		private async Task<(string title, DateTime publishDate)> GetTitleForVideo(string videoId)
		{
			using (var client = GetHttpClient($"https://api.twitch.tv/kraken/videos/{videoId}"))
			{

				string rawString = await client.GetStringAsync($"");
				var rootData = JsonConvert.DeserializeObject<VideoDetail>(rawString);
				return (rootData.title, rootData.published_at);

			}
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
