namespace AssetManager
{
    using Microsoft.WindowsAzure.MediaServices.Client;
    using System;

    public class Helper
	{

		public static CloudMediaContext GenerateMediaContext(string tenant,string clientId,string clientSecret,string apiUri)
		{
			AzureAdTokenCredentials tokenCredentials = new AzureAdTokenCredentials(tenant, new AzureAdClientSymmetricKey(clientId, clientSecret), AzureEnvironments.AzureCloudEnvironment);
			AzureAdTokenProvider tokenProvider = new AzureAdTokenProvider(tokenCredentials);

			CloudMediaContext mediaServiceContext = new CloudMediaContext(new Uri(apiUri), tokenProvider);

			return mediaServiceContext;
		}

	}
}
