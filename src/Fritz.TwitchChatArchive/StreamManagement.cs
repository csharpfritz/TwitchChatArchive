using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
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

  public class StreamManagement : BaseFunction
  {

    public const string TWITCH_SECRET = "DoTheTh1ng5";

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
        [Table("CurrentSubscriptions", Connection = "TwitchChatStorage")]CloudTable cloudTable,
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
          await cloudTable.ExecuteAsync(TableOperation.InsertOrReplace(sub));

        }


      }

    }

    [FunctionName("ScheduledResubscribe")]
    public async Task ScheduledResubscribe([TimerTrigger("0 2 */2 * * *", RunOnStartup = true)]TimerInfo timer,
      [Table("CurrentSubscriptions", Connection = "TwitchChatStorage")]CloudTable cloudTable,
      [Queue("twitch-channel-subscription", Connection = "TwitchChatStorage")] CloudQueue queue,
      ILogger logger)
    {

      var partitionKey = DateTime.UtcNow.ToString("yyyyMMdd");
      var query = new TableQuery<CurrentSubscription>
      {
        FilterString = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, partitionKey)
      };

      TableContinuationToken token = null;
      while (true)
      {
        var results = await cloudTable.ExecuteQuerySegmentedAsync<CurrentSubscription>(query.Take(10), token);
        if (results.Results.Count == 0) break;

        foreach (var item in results.Results)
        {
          await queue.AddMessageAsync(new CloudQueueMessage(item.ChannelId));
          await cloudTable.ExecuteAsync(TableOperation.Delete(item));
        }

        if (results.ContinuationToken != null)
        {
          token = results.ContinuationToken;
        } else
        {
          break;
        }

      }

    }

    private async Task<string> GetChannelIdForUserName(string userName)
    {


      var client = GetHttpClient("https://api.twitch.tv/helix/");

      var body = await client.GetAsync($"users?login={userName}")
        .ContinueWith(msg => msg.Result.Content.ReadAsStringAsync()).Result;
      var obj = JObject.Parse(body);

      return obj["data"][0]["id"].ToString();

    }

    private async Task<string> GetLastVideoForChannel(string channelId)
    {

      using (var client = GetHttpClient($"https://api.twitch.tv/helix/"))
      {

        var msg = client.GetAsync($"videos?user_id={channelId}&first=10");
        var body = await msg.Result.Content.ReadAsStringAsync();
        var obj = JObject.Parse(body);

        for (var i = 0; i < 10; i++)
        {
          if (obj["data"][i]["type"].ToString() == "archive")
          {
            return obj["data"][i]["id"].ToString();
          }
        }

        return string.Empty;

      }


    }

    private async Task<bool> VerifyPayloadSecret(HttpRequest req, ILogger log)
    {

      //#if DEBUG
      //			return true;
      //#endif

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

  }

}