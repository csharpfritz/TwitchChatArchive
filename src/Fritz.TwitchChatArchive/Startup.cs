using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

[assembly: FunctionsStartup(typeof(Fritz.TwitchChatArchive.Startup))]

namespace Fritz.TwitchChatArchive
{
	public class Startup : FunctionsStartup
	{

		public override void Configure(IFunctionsHostBuilder builder)
		{

			builder.Services.AddHttpClient();

		}
	}
}
