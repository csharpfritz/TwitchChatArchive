using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Fritz.TwitchChatArchive.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Fritz.TwitchChatArchive
{
	public abstract class BaseFunction {

    public const string TWITCH_SECRET = "DoTheTh1ng5";
    protected const string TWITCH_CLIENTID_WEB = "kimne78kx3ncx6brgo4mv6wki5h1ko";


    protected BaseFunction(IHttpClientFactory httpClientFactory, IConfiguration configuration)
		{
			HttpClientFactory = httpClientFactory;
			Configuration = configuration;
		}

		protected IHttpClientFactory HttpClientFactory { get; }

		protected IConfiguration Configuration { get; }

    public string TwitchClientID { get { return Configuration["TwitchClientID"]; } }

    protected HttpClient GetPublicHttpClient(string baseAddress) {
      return GetHttpClient(baseAddress, TWITCH_CLIENTID_WEB, false);
    }

    protected HttpClient GetHttpClient(string baseAddress, string clientId = "", bool includeJson = true)
		{

      if (clientId == "") clientId = TwitchClientID;

			var client = HttpClientFactory.CreateClient();
			client.BaseAddress = new Uri(baseAddress);

      if (includeJson)
      {
        client.DefaultRequestHeaders.Add("Accept", @"application/json");
      }
			client.DefaultRequestHeaders.Add("Accept", @"application/vnd.twitchtv.v5+json");
			client.DefaultRequestHeaders.Add("Client-ID", clientId);

			return client;

		}

    protected async Task<string> GetChannelIdForUserName(string userName)
    {


      var client = GetHttpClient("https://api.twitch.tv/helix/");
      var token = await GetAccessToken();
      client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token.AccessToken}");

      string body = string.Empty;
      try
      {
        var msg = await client.GetAsync($"users?login={userName}");
        msg.EnsureSuccessStatusCode();
        body = await msg.Content.ReadAsStringAsync();
      } catch (HttpRequestException e) {
        throw;
      } catch {

        // Were we actually passed a channel id?

        if (await GetUserNameForChannelId(userName) != string.Empty)
          return userName;
        else
          return string.Empty;

      }

      var obj = JObject.Parse(body);
      return obj["data"][0]["id"].ToString();

    }

    protected async Task<string> GetUserNameForChannelId(string channelId)
    {


      var client = GetHttpClient("https://api.twitch.tv/helix/");
      var token = await GetAccessToken();
      client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token.AccessToken}");

      var body = await client.GetAsync($"users?id={channelId}")
        .ContinueWith(msg => msg.Result.Content.ReadAsStringAsync()).Result;
      var obj = JObject.Parse(body);

      return obj["data"][0]["login"].ToString();

    }

    protected async Task<string> GetLastVideoForChannel(string channelId)
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

    protected async Task<IEnumerable<string>> GetVideosForChannelSince(string channelId, DateTime since)
    {

      using (var client = GetHttpClient($"https://api.twitch.tv/helix/"))
      {

        var msg = client.GetAsync($"videos?user_id={channelId}&first=20");
        var body = await msg.Result.Content.ReadAsStringAsync();
        var obj = JObject.Parse(body);

        var outList = new List<string>();

        for (var i = 0; i < 20; i++)
        {
          if (obj["data"][i]["type"].ToString() == "archive")
          {

            var createdAt = DateTime.Parse(obj["data"][i]["created_at"].ToString());
            if (createdAt > since) outList.Add(obj["data"][i]["id"].ToString());
              
          }
        }

        return outList;

      }


    }

    protected async Task<bool> VerifyPayloadSecret(HttpRequest req, ILogger log)
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

    protected static string CreateHmacHash(string data, string key)
    {

      var keybytes = UTF8Encoding.UTF8.GetBytes(key);
      var dataBytes = UTF8Encoding.UTF8.GetBytes(data);

      var hmac = new HMACSHA256(keybytes);
      var hmacBytes = hmac.ComputeHash(dataBytes);

      return Convert.ToBase64String(hmacBytes);

    }

    protected async Task<AppAccessToken> GetAccessToken() {

      var clientId = Configuration["TwitchClientID"];
      var clientSecret = Configuration["TwitchClientSecret"];

      using (var client = GetHttpClient("https://id.twitch.tv")) {

        var result = await client.PostAsync($"/oauth2/token?client_id={clientId}&client_secret={clientSecret}&grant_type=client_credentials&scope=", new StringContent(""));

        result.EnsureSuccessStatusCode();

        return JsonConvert.DeserializeObject<AppAccessToken>(await result.Content.ReadAsStringAsync());

      }

    }

  }

}