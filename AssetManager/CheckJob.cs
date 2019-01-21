namespace AssetManager
{
	using Microsoft.Azure.WebJobs;
	using Microsoft.Azure.WebJobs.Host;
	using Microsoft.WindowsAzure.MediaServices.Client;
	using Newtonsoft.Json;
	using System;
	using System.Linq;
	using System.Net;
	using System.Net.Http;
	using System.Text;
	using System.Threading.Tasks;

	public static class CheckJob
	{
		static readonly string _mediaServiceAPI = System.Configuration.ConfigurationManager.AppSettings["MediaServiceAPI"];
		static readonly string _tenant = System.Configuration.ConfigurationManager.AppSettings["MediaServiceAzureADTenant"];
		static readonly string _clientId = System.Configuration.ConfigurationManager.AppSettings["MediaServiceClientId"];
		static readonly string _clientSecret = System.Configuration.ConfigurationManager.AppSettings["MediaServiceClientSecret"];

		static CloudMediaContext _mediaServiceContext = null;

		[FunctionName("CheckJob")]
		public static async Task<object> Run([HttpTrigger(WebHookType = "genericJson")]HttpRequestMessage req, TraceWriter log)
		{
			log.Info($"Webhook was triggered!");

			string jsonContent = await req.Content.ReadAsStringAsync();
			if (string.IsNullOrEmpty(jsonContent))
			{
				return req.CreateResponse(HttpStatusCode.BadRequest, new
				{
					error = "Invalid request"
				});
			}

			dynamic data = JsonConvert.DeserializeObject(jsonContent);

			if (data.jobId == null)
			{
				return req.CreateResponse(HttpStatusCode.BadRequest, new
				{
					error = "Please pass the job ID in the request body."
				});
			}

			IJob job = null;

			var startTime = string.Empty;
			var endTime = string.Empty;
			var errorMessage = new StringBuilder();
			var runningDuration = string.Empty;
			var isRunning = true;
			var isSuccessful = true;
			var urlForClientStreaming = string.Empty;

			try
			{

				_mediaServiceContext = Helper.GenerateMediaContext(_tenant, _clientId, _clientSecret, _mediaServiceAPI);


				string jobid = (string)data.jobId;
				job = _mediaServiceContext.Jobs.Where(j => j.Id == jobid).FirstOrDefault();

				if (job == null)
				{
					log.Info($"Job is not found. JobID: {jobid}");

					return req.CreateResponse(HttpStatusCode.InternalServerError, new
					{
						error = "Job is not found."
					});
				}

				log.Info($"Job {job.Id} status is {job.State}.");

				if (job.State == JobState.Error || job.State == JobState.Canceled)
				{
					foreach (var taskenum in job.Tasks)
					{
						foreach (var details in taskenum.ErrorDetails)
						{
							errorMessage.AppendLine($"{taskenum.Name} : {details.Message}");
						}
					}
				}

				startTime = job.StartTime?.ToString("o");
				endTime = job.EndTime?.ToString("o");
				runningDuration = job.RunningDuration == null ? string.Empty : job.RunningDuration.ToString();
				isRunning = !(job.State == JobState.Finished || job.State == JobState.Canceled || job.State == JobState.Error);
				isSuccessful = (job.State == JobState.Finished);

				if (!isRunning)
				{
					urlForClientStreaming = await GenerateStreamURL(job, log);
				}

				return req.CreateResponse(HttpStatusCode.OK, new
				{
					jobState = job.State,
					errorText = errorMessage.ToString(),
					startTime = startTime,
					endTime = endTime,
					runningDuration = runningDuration,
					isRunning = isRunning.ToString(),
					isSuccessful = isSuccessful.ToString(),
					progress = job.GetOverallProgress(),
					streamURL = urlForClientStreaming
				});
			}
			catch (Exception ex)
			{
				string message = ex.Message;
				log.Info($"!!!ERROR!!!: {message}");
				return req.CreateResponse(HttpStatusCode.InternalServerError, new { error = message });
			}
		}

		public static async Task<string> GenerateStreamURL(IJob job, TraceWriter log)
		{
			string url = string.Empty;
			var outputAsset = _mediaServiceContext.Assets.Where(a => a.Id == job.OutputMediaAssets[0].Id).FirstOrDefault();
			var inputAsset = _mediaServiceContext.Assets.Where(a => a.Id == job.InputMediaAssets[0].Id).FirstOrDefault();

			if (outputAsset != null)
			{
				IAccessPolicy readPolicy = _mediaServiceContext.AccessPolicies.Create("readPolicy", TimeSpan.FromDays(365 * 10), AccessPermissions.Read);
				ILocator outputLocator = _mediaServiceContext.Locators.CreateLocator(LocatorType.OnDemandOrigin, outputAsset, readPolicy);

				var manifestFile = outputAsset.AssetFiles.Where(f => f.Name.ToLower().EndsWith(".ism")).FirstOrDefault();

				if (manifestFile != null)
				{
					url = outputLocator.Path + manifestFile.Name + "/manifest(format=m3u8-aapl)";
					
					log.Info($"Stream URL: {url}");

					await inputAsset.DeleteAsync();
				}
			}
			return url;
		}

	}
}
