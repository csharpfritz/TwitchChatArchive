using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;

namespace Fritz.TwitchChatArchive
{
	public abstract class BaseFunction {

		protected BaseFunction(IHttpClientFactory httpClientFactory, IConfiguration configuration)
		{
			HttpClientFactory = httpClientFactory;
			Configuration = configuration;
		}

		protected IHttpClientFactory HttpClientFactory { get; }
		protected IConfiguration Configuration { get; }

		protected HttpClient GetHttpClient(string baseAddress)
		{

			var client = HttpClientFactory.CreateClient();
			client.BaseAddress = new Uri(baseAddress);
			client.DefaultRequestHeaders.Add("Accept", @"application/json");
			client.DefaultRequestHeaders.Add("Accept", @"application/vnd.twitchtv.v5+json");
			client.DefaultRequestHeaders.Add("Client-Id", Configuration["TwitchClientID"]);
			client.DefaultRequestHeaders.Add("Authorization", $"Bearer {Configuration["TwitchClientSecret"]}");

			return client;

		}


	}

}