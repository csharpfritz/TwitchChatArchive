using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Storage;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Fritz.TwitchChatArchive
{
	// Liveshare guestbook:
	// SurlyDev woz 'ere 24.11.2019 @ 16:06 GMT (from the UK) <- it was him :D
	// Dawid was here 24.11.2019 @ 16:07 - Greetings from the UK
	// ConnavarDev was here 24.11.2019 @ 17:07 CET - Hello from Czech Republic
	// SingingMexican was here 24.11.2019 @ 17:10!
	// Haxores was here. [TR]
	// Thanks for joining my Fritzbit Redemption gift for you all -- Janisku7 18:13 Finland time

	// No no we are very nice.
	// Thank you for redeeming!
	//

	public class StreamManagement
	{

		public const string TWITCH_SECRET = "DoTheTh1ng5";
		private readonly IHttpClientFactory _HttpClientFactory;
		private readonly IConfiguration _Configuration;

		public StreamManagement(IHttpClientFactory httpClientFactory, IConfiguration configuration)
		{
			_HttpClientFactory = httpClientFactory;
			_Configuration = configuration;
		}

		[FunctionName("ReceiveEndOfStream")]
		public async Task<HttpResponseMessage> EndOfStream(
		[HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
		[Blob("chatlog", FileAccess.ReadWrite, Connection ="TwitchChatStorage")] CloudBlobContainer container,
						ILogger log)
		{
			//We love you Fritz!
			// If you're reading this it's too late!!
			// parse query parameter

			var channelId = req.Query["channelId"].ToString();
			log.LogMetric("Query", 1, new Dictionary<string, object> { { "TwitchChannelId", channelId } });
			log.LogInformation($"ChannelId: {channelId}");

			var challenge = req.Query["hub.challenge"].ToString();
			if (!string.IsNullOrEmpty(challenge)) {

				log.LogInformation($"Successfully subscribed to channel {channelId}");

				return new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new StringContent(challenge)
				};
			}

			if (!string.IsNullOrEmpty(req.Query["userid"].ToString())) channelId = await GetChannelIdForUserName(req.Query["userid"].ToString());

			if (!(await VerifyPayloadSecret(req, log))) {
				log.LogError($"Invalid signature on request for ChannelId {channelId}");
				return null;
			} else {
				log.LogTrace($"Valid signature for ChannelId {channelId}");
			}

			var videoId = await GetLastVideoForChannel(channelId);
			log.LogInformation($"Found last video with id: {videoId}");

			var result  = await DownloadChatForVideo(videoId);
			await container.CreateIfNotExistsAsync();
			var blob = container.GetBlockBlobReference($"{videoId}.json");
			await blob.UploadTextAsync(JsonConvert.SerializeObject(result));

			return null;

		}

		[FunctionName("Subscribe")]
		public async Task Subscribe([QueueTrigger("twitch-channel-subscription", Connection = "TwitchChatStorage")] CloudQueueMessage msg,
																			ILogger logger)
		{

			var twitchEndPoint = "https://api.twitch.tv/helix/webhooks/hub"; // could end up like configuration["Twitch:HubEndpoint"]
#if DEBUG
			var leaseInSeconds = 0; // 864000 = 10 days
#else
			var leaseInSeconds = 864000; // = 10 days
#endif

			var channelId = await GetChannelIdForUserName(msg.AsString);
			var callbackUrl = new Uri(_Configuration["EndpointBaseUrl"]);

			var payload = new TwitchWebhookSubscriptionPayload
			{
				callback = new Uri(callbackUrl, $"?channelId={channelId}").ToString(),
				mode = "subscribe",
				topic = $"https://api.twitch.tv/helix/streams?user_id={channelId}",
				lease_seconds = leaseInSeconds,
				secret = TWITCH_SECRET
			};
			logger.LogDebug($"Posting with callback url: {payload.callback}");
			var stringPayload = JsonConvert.SerializeObject(payload);
			logger.Log(Microsoft.Extensions.Logging.LogLevel.Information, $"Subscribing to Twitch with payload: {stringPayload}");

			using (var client = GetHttpClient("https://api.twitch.tv/helix/webhooks/hub"))
			{

				var responseMessage = await client.PostAsync("", new StringContent(stringPayload, Encoding.UTF8, @"application/json"));
				if (!responseMessage.IsSuccessStatusCode)
				{
					var responseBody = await responseMessage.Content.ReadAsStringAsync();
					logger.Log(Microsoft.Extensions.Logging.LogLevel.Error, $"Error response body: {responseBody}");
				}


			}

		}

		private async Task<string> GetChannelIdForUserName(string userName)
		{


			var client = GetHttpClient("https://api.twitch.tv/helix/");

			var msg = client.GetAsync($"users?login={userName}");
			var body = await msg.Result.Content.ReadAsStringAsync();
			var obj = JObject.Parse(body);

			return obj["data"][0]["id"].ToString();

		}

		private async Task<string> GetLastVideoForChannel(string channelId)
		{

			using (var client = GetHttpClient($"https://api.twitch.tv/helix/")) {

				var msg = client.GetAsync($"videos?user_id={channelId}&first=10");
				var body = await msg.Result.Content.ReadAsStringAsync();
				var obj = JObject.Parse(body);

				for (var i=0; i<10; i++)
				{
					if (obj["data"][i]["type"].ToString() == "archive") {
						return obj["data"][i]["id"].ToString();
					}
				}

				return string.Empty;

			}


		}

		private async Task<bool> VerifyPayloadSecret(HttpRequest req, ILogger log)
		{

#if DEBUG
			return true;
#endif

			var signature = req.Headers["X-Hub-Signature"].ToString();
			//if (string.IsNullOrEmpty(signature))
			//{
			//	log.LogError("Twitch Signature header not found");
			//	return false;
			//}

			log.LogInformation($"Twitch Signature sent: {signature}");

			// TODO: HMAC verify
			var ourHashCalculation = string.Empty;
			if (req.Body.CanSeek)
			{
				using (var reader = new StreamReader(req.Body, Encoding.UTF8))
				{
					req.Body.Position = 0;
					var bodyContent = await reader.ReadToEndAsync();
					ourHashCalculation = CreateHmacHash(bodyContent, TWITCH_SECRET);
				}
			}

			log.LogInformation($"Our calculated signature: {ourHashCalculation}");
			return true;

			//return ourHashCalculation == signature;

		}

		private static string CreateHmacHash(string data, string key)
		{

			var keybytes = UTF8Encoding.UTF8.GetBytes(key);
			var dataBytes = UTF8Encoding.UTF8.GetBytes(data);

			var hmac = new HMACSHA256(keybytes);
			var hmacBytes = hmac.ComputeHash(dataBytes);

			return Convert.ToBase64String(hmacBytes);

		}

		private HttpClient GetHttpClient(string baseAddress) {

			var client = _HttpClientFactory.CreateClient();
			client.BaseAddress = new Uri(baseAddress);
			client.DefaultRequestHeaders.Add("Accept", @"application/json");
			client.DefaultRequestHeaders.Add("Accept", @"application/vnd.twitchtv.v5+json");
			client.DefaultRequestHeaders.Add("Client-Id", _Configuration["TwitchClientID"]);

			return client;

		}

		private async Task<IEnumerable<Comment>> DownloadChatForVideo(string videoId)
		{

			// Cheer 300 codingbandit 29/11/19 
			// Cheer 100 MattLeibow 29/11/19 

			var comments = new List<Comment>();

			using (var client  = GetHttpClient($"https://api.twitch.tv/v5/videos/{videoId}/comments")) {

				string rawString = await client.GetStringAsync($"");
				var rootData = JsonConvert.DeserializeObject<CommentsRootData>(rawString);
				while(!string.IsNullOrEmpty(rootData._next)) {
					comments.AddRange(rootData.comments);
					rootData = JsonConvert.DeserializeObject<CommentsRootData>(await client.GetStringAsync($"?cursor={rootData._next}"));
				}
				comments.AddRange(rootData.comments);

				return comments;

			}

		}

	}

}