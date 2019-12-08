using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fritz.TwitchChatArchive.Data
{

	[Serializable]
	public class AppAccessToken
	{
		private readonly DateTime _CreateTime;

		public AppAccessToken()
		{
			_CreateTime = DateTime.Now;
		}

		[JsonProperty("access_token")]
		public string AccessToken { get; set; }

		[JsonProperty("refresh_token")]
		public string RefreshToken { get; set; }

		[JsonProperty("expires_in")]
		public int ExpiresInSeconds { get; set; }

		public DateTime ExpiresDateTime {  get { return _CreateTime.AddSeconds(ExpiresInSeconds);  } }

		[JsonProperty("scope")]
		public string[] Scope { get; set; }

		[JsonProperty("token_type")]
		public string TokenType { get; set; }

	}

}
