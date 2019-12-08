using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Fritz.TwitchChatArchive.Data;
using Fritz.TwitchChatArchive.Messages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Azure.WebJobs;

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

	public class StreamManagement : BaseFunction
	{
		public StreamManagement(IHttpClientFactory httpClientFactory, IConfiguration configuration) : base(httpClientFactory, configuration)
		{
		}

		[FunctionName("ReceiveEndOfStream")]
		public HttpResponseMessage EndOfStream(
		[HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
						ILogger log,
		[ServiceBus("EndOfStream", Connection = "ServiceBusConnectionString", EntityType = EntityType.Topic)]
						out CompletedStream completedStream)
		{
			//We love you Fritz!
			// If you're reading this it's too late!!
			// parse query parameter

			completedStream = CompletedStream.Empty;

			var channelId = req.Query["channelId"].ToString();
			log.LogMetric("Query", 1, new Dictionary<string, object> { { "TwitchChannelId", channelId } });
			log.LogInformation($"ChannelId: {channelId}");

			var challenge = req.Query["hub.challenge"].ToString();
			if (!string.IsNullOrEmpty(challenge))
			{

				log.LogInformation($"Successfully subscribed to channel {channelId}");

				return new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new StringContent(challenge)
				};
			}

			if (!string.IsNullOrEmpty(req.Query["userid"].ToString())) channelId = GetChannelIdForUserName(req.Query["userid"].ToString()).GetAwaiter().GetResult();

			if (!(VerifyPayloadSecret(req, log).GetAwaiter().GetResult()))
			{
				log.LogError($"Invalid signature on request for ChannelId {channelId}");
				return null;
			}
			else
			{
				log.LogTrace($"Valid signature for ChannelId {channelId}");
			}

			var videoId = GetLastVideoForChannel(channelId).GetAwaiter().GetResult();
			log.LogInformation($"Found last video with id: {videoId}");

			completedStream.ChannelId = channelId;
			completedStream.VideoId = videoId;

			return new HttpResponseMessage(HttpStatusCode.OK);

		}

		[FunctionName("Subscribe")]
		public async Task Subscribe([QueueTrigger("twitch-channel-subscription", Connection = "TwitchChatStorage")] CloudQueueMessage msg,
																			ILogger logger)
		{

			var twitchEndPoint = "https://api.twitch.tv/helix/webhooks/hub"; // could end up like configuration["Twitch:HubEndpoint"]
																																			 //#if DEBUG
																																			 //			var leaseInSeconds = 0; // 864000 = 10 days
																																			 //#else
			var leaseInSeconds = 864000; // = 10 days
																	 //#endif

			var channelId = await GetChannelIdForUserName(msg.AsString);
			var callbackUrl = new Uri(Configuration["EndpointBaseUrl"]);

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

			using (var client = GetHttpClient(twitchEndPoint))
			{

				var responseMessage = await client.PostAsync("", new StringContent(stringPayload, Encoding.UTF8, @"application/json"));
				if (!responseMessage.IsSuccessStatusCode)
				{
					var responseBody = await responseMessage.Content.ReadAsStringAsync();
					logger.Log(Microsoft.Extensions.Logging.LogLevel.Error, $"Error response body: {responseBody}");
				}
				else
				{

					var sub = new CurrentSubscription
					{
						ChannelId = channelId,
						ExpirationDateTimeUtc = DateTime.UtcNow.AddSeconds(leaseInSeconds).AddDays(-1)
					};
					var repo = new CurrentSubscriptionsRepository(Configuration);
					await repo.AddSubscription(sub);

				}


			}

		}

		[FunctionName("ScheduledResubscribe")]
		public async Task ScheduledResubscribe([TimerTrigger("0 2 */2 * * *", RunOnStartup = true)]TimerInfo timer,
			[Queue("twitch-channel-subscription", Connection = "TwitchChatStorage")] CloudQueue queue,
			ILogger logger)
		{

			var repo = new CurrentSubscriptionsRepository(Configuration);

			var currentSubscriptions = await repo.GetExpiringSubscriptions();
			foreach (var item in currentSubscriptions)
			{
				await queue.AddMessageAsync(new CloudQueueMessage(item.ChannelId));
				await repo.RemoveSubscription(item);
			}

	}

	[FunctionName("CurrentWebhookSubscriptions")]
	public async Task<IActionResult> GetCurrentWebhookSubscriptions(
		[HttpTrigger(AuthorizationLevel.Function, methods: new string[] { "get" })]HttpRequest req, ILogger logger
	)
	{

		var token = await GetAccessToken();

		using (var client = GetHttpClient("https://api.twitch.tv"))
		{

			client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token.AccessToken}");

			var result = await client.GetAsync("/helix/webhooks/subscriptions");
			result.EnsureSuccessStatusCode();

			var subs = JObject.Parse(await result.Content.ReadAsStringAsync());

			return new JsonResult(subs);

		}

	}
}

}