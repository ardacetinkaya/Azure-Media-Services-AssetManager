namespace AssetManager
{
    using AssetManager.Common;
    using Microsoft.Azure.WebJobs;
    using Microsoft.Azure.WebJobs.Host;
    using Microsoft.WindowsAzure.MediaServices.Client;
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading.Tasks;

    public static class ListenEvent
	{
		static readonly string _mediaServiceAPI = System.Configuration.ConfigurationManager.AppSettings["MediaServiceAPI"];
		static readonly string _tenant = System.Configuration.ConfigurationManager.AppSettings["MediaServiceAzureADTenant"];
		static readonly string _clientId = System.Configuration.ConfigurationManager.AppSettings["MediaServiceClientId"];
		static readonly string _clientSecret = System.Configuration.ConfigurationManager.AppSettings["MediaServiceClientSecret"];

		static CloudMediaContext _mediaServiceContext = null;

		[FunctionName("ListenEvent")]
		public static async Task<HttpResponseMessage> Run([HttpTrigger(WebHookType = "genericJson")]HttpRequestMessage req, TraceWriter log)
		{
			log.Info($"HTTP trigger function processed a request. RequestUri={req.RequestUri}");

			Task<byte[]> taskForRequestBody = req.Content.ReadAsByteArrayAsync();
			byte[] requestBody = await taskForRequestBody;

			if (req.Headers.TryGetValues("ms-signature", out IEnumerable<string> values) && requestBody != null)
			{
				string notification = Encoding.UTF8.GetString(requestBody);

				NotificationEvent notificationEvent = JsonConvert.DeserializeObject<NotificationEvent>(notification);

				string jobState = (string)notificationEvent.Properties.Where(j => j.Key == "NewState").FirstOrDefault().Value;
				string jobId = (string)notificationEvent.Properties.Where(j => j.Key == "JobId").FirstOrDefault().Value;

				log.Info($"Job state: {jobId} -  {jobState}");

				if (jobState == "Finished")
				{
					_mediaServiceContext = Helper.GenerateMediaContext(_tenant, _clientId, _clientSecret, _mediaServiceAPI);

					var job = _mediaServiceContext.Jobs.Where(j => j.Id == jobId).FirstOrDefault();

					if (job != null)
					{
						var outputAsset = _mediaServiceContext.Assets.Where(a => a.Id == job.OutputMediaAssets[0].Id).FirstOrDefault();
						var inputAsset = _mediaServiceContext.Assets.Where(a => a.Id == job.InputMediaAssets[0].Id).FirstOrDefault();

						if (outputAsset != null)
						{

							IAccessPolicy readPolicy = _mediaServiceContext.AccessPolicies.Where(p => p.Name == "readPolicy").FirstOrDefault();

							if (readPolicy == null)
							{
								//Stream for 10 years :) 
								readPolicy = _mediaServiceContext.AccessPolicies.Create("readPolicy", TimeSpan.FromDays(365 * 10), AccessPermissions.Read);
							}

							ILocator outputLocator = _mediaServiceContext.Locators.CreateLocator(LocatorType.OnDemandOrigin, outputAsset, readPolicy);

							var manifestFile = outputAsset.AssetFiles.Where(f => f.Name.ToLower().EndsWith(".ism")).FirstOrDefault();

							if (manifestFile != null)
							{
								string streamingUrl = $"{outputLocator.Path}{manifestFile.Name}/manifest(format=m3u8-aapl)";

								log.Info($"Job is finished: {jobId}");

                                //Do some DB updates/operations
								//await DataRepository.UpdateStreamContent();
								//await DataRepository.UpdateJobStatus();

								log.Info($"Stream URL and status is updated for job: {jobId} - {streamingUrl}");

								await inputAsset.DeleteAsync();

							}

						}
					}
					else
					{
						log.Info($"No job is found with id: {job.Id}");
					}
				}

				return req.CreateResponse(HttpStatusCode.OK, string.Empty);

			}

			return req.CreateResponse(HttpStatusCode.BadRequest, "Invalid request.");
		}
	}
}
