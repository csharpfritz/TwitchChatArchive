using System;
using System.Configuration;
using System.Drawing.Text;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
		[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
						ILogger log)
		{
			//We love you Fritz!
			// If you're reading this it's too late!!
			// parse query parameter

			var channelId = req.Query["channelId"].ToString();

			// TODO: Trigger chat extract


			return null;

		}

		public async Task Test([QueueTrigger("twitch-channel-test", Connection = "TwitchChatStorage")] CloudQueueMessage msg,
																	ILogger logger)
		{

			logger.LogDebug("Completed");

		}


		public async Task Subscribe([QueueTrigger("twitch-channel-subscription", Connection = "TwitchChatStorage")] CloudQueueMessage msg,
																			ILogger logger)
		{
			// subscribe to csharpfritz

			var twitchEndPoint = "https://api.twitch.tv/helix/webhooks/hub"; // could end up like configuration["Twitch:HubEndpoint"]
			var leaseInSeconds = 0; // 86400 = 24 hours.
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
			var stringPayload = JsonConvert.SerializeObject(payload);
			logger.Log(LogLevel.Information, $"Subscribing to Twitch with payload: {stringPayload}");

			using (var client = _HttpClientFactory.CreateClient())
			{

				var responseMessage = await client.PostAsync(@"https://api.twitch.tv/helix/webhooks/hub", new StringContent(stringPayload, Encoding.UTF8, @"application/json"));
				if (!responseMessage.IsSuccessStatusCode)
				{
					var responseBody = await responseMessage.Content.ReadAsStringAsync();
					logger.Log(LogLevel.Error, $"Error response body: {responseBody}");
				}


			}

		}

		private async Task<string> GetChannelIdForUserName(string userName)
		{


			var client = _HttpClientFactory.CreateClient();
			client.BaseAddress = new Uri("https://api.twitch.tv/helix/");
			client.DefaultRequestHeaders.Add("Accept", @"application/json");

			var msg = client.GetAsync($"users?login={userName}");
			var body = await msg.Result.Content.ReadAsStringAsync();
			var obj = JObject.Parse(body);

			return obj["data"][0]["id"].ToString();

		}

	}
}