using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Fritz.TwitchChatArchive
{
	public static class DownloadVideo
	{

			public const string QUEUE_DownloadVideoLink = "downloadvideo";
			public const string QUEUE_ChunkList = "downloadvideo-chunkqueue";
			public const string QUEUE_AssembleChunks = "downloadvideo-assemble";
			private const string BLOB_DownloadChunks = "download-chunks";
			private const string BLOB_TwitchArchive = "twitch-archive";

			[FunctionName("Configure")]
			public static async Task<IActionResult> Run(
					[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
					ILogger log)
			{
				log.LogInformation("C# HTTP trigger function processed a request.");

				string name = req.Query["name"];

				string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
				dynamic data = JsonConvert.DeserializeObject(requestBody);
				name = name ?? data?.name;

				await Configure();

				return name != null
						? (ActionResult)new OkObjectResult($"Hello, {name}")
						: new BadRequestObjectResult("Please pass a name on the query string or in the request body");
			}

			[FunctionName("DownloadVideo")]
			public static async Task GetVideo(
				[QueueTrigger(QUEUE_DownloadVideoLink, Connection = "TwitchChatStorage")] string queueItem,
				ILogger log)
			{

				var clientId = Environment.GetEnvironmentVariable("TwitchClientId"); //CloudConfigurationManager.GetSetting("TwitchClientId");
				var returnVal = JObject.Parse(await GetTokenFromTwitchForVideo(clientId, queueItem));
				var sig = returnVal["sig"];
				var token = returnVal["token"];

				var qualityUrl = await GetQualityUrl(clientId, queueItem, sig, token);
				var chunkList = await GetChunks(clientId, qualityUrl);
				var baseUrl = qualityUrl.Substring(0, qualityUrl.LastIndexOf('/')) + "/";

				var account = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("AzureWebJobsStorage"));
				var client = account.CreateCloudQueueClient();
				var queue = client.GetQueueReference(QUEUE_ChunkList);
				var chunklistCount = chunkList.Count();
				Parallel.ForEach(chunkList, c =>
				{
					queue.AddMessageAsync(new CloudQueueMessage(baseUrl + c + " || " + queueItem + "-" + c + " || " + chunklistCount));
				});


				log.LogInformation(qualityUrl);

			}

			[FunctionName("DownloadVideoChunk")]
			public static async Task DownloadVideoChunk(
				[QueueTrigger(QUEUE_ChunkList, Connection = "TwitchChatStorage")] string chunkUrlItem,
				ILogger log)
			{

				var clientId = Environment.GetEnvironmentVariable("TwitchClientId"); //CloudConfigurationManager.GetSetting("TwitchClientId");
				var parts = chunkUrlItem.Split(" || ");

				var account = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("TwitchChatStorage"));
				var blobClient = account.CreateCloudBlobClient();
				var container = blobClient.GetContainerReference(BLOB_DownloadChunks);
				var blob = container.GetBlockBlobReference(parts[1]);

				using (var client = new HttpClient())
				{

					client.DefaultRequestHeaders.Add("Client-ID", clientId);

					var message = await client.GetAsync(parts[0]);
					message.EnsureSuccessStatusCode();

					await blob.UploadFromStreamAsync(await message.Content.ReadAsStreamAsync());

				}

				await CheckAllChunksDownloaded(parts[1], int.Parse(parts[2]));

			}

			[FunctionName("AssembleVideoChunks")]
			public static async Task AssembleVideoChunks(
			[QueueTrigger(QUEUE_AssembleChunks, Connection = "TwitchChatStorage")] string fileNameCount,
			ILogger log)
			{

				var parts = fileNameCount.Split(" || ");

				var account = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("TwitchChatStorage"));
				var client = account.CreateCloudBlobClient();
				var container = client.GetContainerReference(BLOB_DownloadChunks);

				var saveContainer = client.GetContainerReference(BLOB_TwitchArchive);
				var outBlob = saveContainer.GetBlockBlobReference(parts[0] + ".mp4");

				var blockList = new List<string>();
				var count = int.Parse(parts[1]);
				for (var i = 0; i < count; i++)
				{
					var blob = container.GetBlockBlobReference(parts[0] + "-" + i + ".ts");
					if (blob == null)
					{
						container.GetBlockBlobReference(parts[0] + "-" + i + "-muted.ts");
					}
					var mem = new MemoryStream();
					Console.WriteLine($"Downloading part {i}");
					await blob.DownloadToStreamAsync(mem);

					Console.WriteLine($"Writing {mem.Length} bytes");
					mem.Position = 0;
					var md5 = CalculateMD5(mem);
					mem.Position = 0;
					var blockId = Convert.ToBase64String(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N")));
					blockList.Add(blockId);
					await outBlob.PutBlockAsync(blockId, mem, md5);
					await blob.DeleteAsync();
				}

				await outBlob.PutBlockListAsync(blockList);

			}

			private static async Task CheckAllChunksDownloaded(string filename, int totalChunks)
			{

				var baseFileName = filename.Split('-')[0];

				var account = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("TwitchChatStorage"));
				var blobClient = account.CreateCloudBlobClient();
				var container = blobClient.GetContainerReference(BLOB_DownloadChunks);

				BlobContinuationToken token = null;
				var results = new List<IListBlobItem>();
				do
				{
					var response = await container.ListBlobsSegmentedAsync(token);
					results.AddRange(response.Results);
					token = response.ContinuationToken;
				} while (token != null);

				if (results.Count(r => r.Uri.LocalPath.Contains(baseFileName)) == totalChunks)
				{
					var client = account.CreateCloudQueueClient();
					var queue = client.GetQueueReference(QUEUE_AssembleChunks);
					await queue.AddMessageAsync(new CloudQueueMessage(baseFileName + " || " + totalChunks));
				}

			}

			private static async Task<string[]> GetChunks(string clientId, string qualityUrl)
			{
				using (var client = new HttpClient())
				{

					client.DefaultRequestHeaders.Add("Client-ID", clientId);

					var message = await client.GetAsync(qualityUrl);
					message.EnsureSuccessStatusCode();

					return (await message.Content.ReadAsStringAsync()).Split('\n').Where(i => i != "" && !i.StartsWith("#")).ToArray();

				}
			}

			private static async Task<string> GetQualityUrl(string clientId, string queueItem, JToken sig, JToken token)
			{
				using (var client = new HttpClient())
				{

					client.DefaultRequestHeaders.Add("Client-ID", clientId);
					client.DefaultRequestHeaders.Add("Accept", "application/vnd.twitchtv.v3+json");

					var message = await client.GetAsync($"http://usher.justin.tv/vod/{queueItem}?nauthsig={sig}&nauth={token}");
					message.EnsureSuccessStatusCode();

					var contents = await message.Content.ReadAsStringAsync();
					var firstUrl = contents.Split('\n').First(i => i.StartsWith("http"));

					return firstUrl;

				}

			}

			private static async Task<string> GetTokenFromTwitchForVideo(string clientId, string queueItem)
			{

				using (var client = new HttpClient())
				{

					client.DefaultRequestHeaders.Add("Client-ID", clientId);
					client.DefaultRequestHeaders.Add("Accept", "application/vnd.twitchtv.v3+json");

					var message = await client.GetAsync($"https://api.twitch.tv/api/vods/{queueItem}/access_token?as3=t");
					message.EnsureSuccessStatusCode();

					return await message.Content.ReadAsStringAsync();

				}

			}

			private static async Task Configure()
			{

				var account = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("AzureWebJobsStorage"));
				var client = account.CreateCloudQueueClient();
				var queue = client.GetQueueReference(QUEUE_DownloadVideoLink);
				await queue.CreateIfNotExistsAsync();

				queue = client.GetQueueReference(QUEUE_ChunkList);
				await queue.CreateIfNotExistsAsync();

				queue = client.GetQueueReference(QUEUE_AssembleChunks);
				await queue.CreateIfNotExistsAsync();

				var blobClient = account.CreateCloudBlobClient();
				var container = blobClient.GetContainerReference(BLOB_DownloadChunks);
				await container.CreateIfNotExistsAsync();

				container = blobClient.GetContainerReference(BLOB_TwitchArchive);
				await container.CreateIfNotExistsAsync();


			}

			private static string CalculateMD5(Stream stream)
			{

				using (var md5 = MD5.Create())
				{
					var hashBytes = md5.ComputeHash(stream);
					return Convert.ToBase64String(hashBytes, Base64FormattingOptions.None);
				}

			}


	}
}
