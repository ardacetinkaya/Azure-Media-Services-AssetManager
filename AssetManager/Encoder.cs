namespace AssetManager
{
	using AssetManager.Common;
	using Microsoft.Azure.EventGrid;
	using Microsoft.Azure.EventGrid.Models;
	using Microsoft.Azure.WebJobs;
	using Microsoft.Azure.WebJobs.Host;
	using Microsoft.WindowsAzure.MediaServices.Client;
	using Microsoft.WindowsAzure.Storage;
	using Microsoft.WindowsAzure.Storage.Blob;
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading.Tasks;

	public static class Encoder
	{
		//Media Service settings
		static readonly string _mediaServiceAPI = System.Configuration.ConfigurationManager.AppSettings["MediaServiceAPI"];
		static readonly string _tenant = System.Configuration.ConfigurationManager.AppSettings["MediaServiceAzureADTenant"];
		static readonly string _clientId = System.Configuration.ConfigurationManager.AppSettings["MediaServiceClientId"];
		static readonly string _clientSecret = System.Configuration.ConfigurationManager.AppSettings["MediaServiceClientSecret"];
		static readonly string _connectionString = System.Configuration.ConfigurationManager.AppSettings["MediaServiceStorageConnectionString"];

		//EventGrid settings
		static readonly string _eventTopicHost = System.Configuration.ConfigurationManager.AppSettings["EventGridTopicHost"];
		static readonly string _eventTopicKey = System.Configuration.ConfigurationManager.AppSettings["EventGridTopicKey"];

		//Notification endpoints
		static readonly string _webHookEndpoint = System.Configuration.ConfigurationManager.AppSettings["WebhookURL"];
		static readonly string _accessKey = System.Configuration.ConfigurationManager.AppSettings["WebhookAccessKey"];
		static readonly string _webHookEndpointName = "EncodingWebHook";

		static CloudMediaContext _mediaServiceContext = null;
		static CloudStorageAccount _storageAccount = null;


		[FunctionName("Encoder")]
		public static async Task RunAsync([BlobTrigger("assets/video/chapters/original/{name}", Connection = "")]CloudBlockBlob blob, string name, TraceWriter log)
		{
			try
			{
				var contentType = blob.Properties.ContentType;
				log.Info($"Content Type: {blob.Properties.ContentType}");

				//Just checking content type
				if (contentType.Equals("video/mp4"))
				{
					log.Info($"Valid content type. Starting to encode...");

					_mediaServiceContext = Helper.GenerateMediaContext(_tenant,_clientId,_clientSecret,_mediaServiceAPI);

					INotificationEndPoint endpoint = _mediaServiceContext.NotificationEndPoints.Where(e => e.Name == _webHookEndpointName).FirstOrDefault();

					if (endpoint == null)
					{
						byte[] keyBytes = Convert.FromBase64String(_accessKey);
						endpoint = _mediaServiceContext.NotificationEndPoints.Create(_webHookEndpointName, NotificationEndPointType.WebHook, _webHookEndpoint, keyBytes);
						log.Info("Notification endpoint is created.");
					}
					else
					{
						log.Info("Already have a notification endpoint.");
					}

					var asset = await CreateAsset(blob, name, log);

					var job = await CreateJob(name, asset, endpoint, log);

					if (job != null)
					{
						//Fire a custom event
						TopicCredentials topicCredentials = new TopicCredentials(_eventTopicKey);
						EventGridClient client = new EventGridClient(topicCredentials);

						List<EventGridEvent> eventsList = new List<EventGridEvent>();

						eventsList.Add(new EventGridEvent()
						{
							Id = Guid.NewGuid().ToString(),
							EventType = "MediaService.Assets.JobStartedEvent",
							Data = new JobEvent()
							{
								JobId = job.Id,
								EventType = "MediaService.Assets.JobStartedEvent"
							},
							EventTime = DateTime.Now,
							Subject = "Encoding",
							DataVersion = "2.0"
						});

						await client.PublishEventsAsync(new Uri(_eventTopicHost).Host, eventsList);
					}
				}
			}
			catch (Exception ex)
			{
				log.Error($"!!!ERROR!!!: {ex.Message}");
				throw ex;
			}

		}


		public static async Task<IAsset> CreateAsset(CloudBlockBlob blob, string assetName, TraceWriter log)
		{
			// Create a new asset. 
			var asset = _mediaServiceContext.Assets.Create(assetName, AssetCreationOptions.None);
			log.Info($"Created new asset {asset.Name}");

			IAccessPolicy writePolicy = _mediaServiceContext.AccessPolicies.Create("writePolicy", TimeSpan.FromHours(4), AccessPermissions.Write);
			ILocator destinationLocator = _mediaServiceContext.Locators.CreateLocator(LocatorType.Sas, asset, writePolicy);

			_storageAccount = CloudStorageAccount.Parse(_connectionString);
			CloudBlobClient destBlobStorage = _storageAccount.CreateCloudBlobClient();

			// Get the destination asset container reference
			string destinationContainerName = (new Uri(destinationLocator.Path)).Segments[1];

			CloudBlobContainer assetContainer = destBlobStorage.GetContainerReference(destinationContainerName);

			try
			{
				assetContainer.CreateIfNotExists();
				log.Info($"Created new container {destinationContainerName}");
			}
			catch (Exception ex)
			{
				log.Error($"!!!ERROR!!!: {ex.Message}");
			}
			//// Get hold of the destination blob
			CloudBlockBlob destinationBlob = assetContainer.GetBlockBlobReference(assetName);

			// Copy Blob
			try
			{
				using (var stream = await blob.OpenReadAsync())
				{
					await destinationBlob.UploadFromStreamAsync(stream);
				}

				log.Info("Copy Complete.");

				var assetFile = asset.AssetFiles.Create(assetName);
				assetFile.ContentFileSize = blob.Properties.Length;
				assetFile.IsPrimary = true;
				assetFile.Update();
				asset.Update();
			}
			catch (Exception ex)
			{
				log.Error(ex.Message);
				log.Info(ex.StackTrace);
				log.Info("Copy Failed.");
				throw;
			}

			destinationLocator.Delete();
			writePolicy.Delete();

			return asset;
		}

		public static async Task<IJob> CreateJob(string name, IAsset asset, INotificationEndPoint endpoint, TraceWriter log, string mediaProcessorName = "Media Encoder Standard", bool deletePreviousJobs = true)
		{

			if (string.IsNullOrEmpty(mediaProcessorName)) return null;

			if (asset == null)
			{
				log.Info("Invalid input asset for job.");
				return null;
			}

			if (deletePreviousJobs)
			{
				//Clean previous finished jobs, no need to keep them :)
				var finishedJobs = _mediaServiceContext.Jobs.Where(j => j.State == JobState.Finished).ToList();
				foreach (var item in finishedJobs)
				{
					await item.DeleteAsync();
				}
			}


			IJob job = _mediaServiceContext.Jobs.Create($"Encoding Job for - {name}");

			//A standart streaming processor
			var processor = _mediaServiceContext.MediaProcessors.Where(p => p.Name == mediaProcessorName).ToList().OrderBy(p => new Version(p.Version)).LastOrDefault();

			if (processor != null)
			{
				//Create job
				ITask task = job.Tasks.AddNew("Encode with Adaptive Streaming", processor, "Adaptive Streaming", TaskOptions.None);
				task.InputAssets.Add(asset);
				var outputAsset = task.OutputAssets.AddNew(name, AssetCreationOptions.None);

				if (endpoint != null)
				{
					task.TaskNotificationSubscriptions.AddNew(NotificationJobState.All, endpoint, true);
					log.Info("Notification endpoint is added.");
				}

				job.Submit();

				log.Info($"Encoding job is submitted. Job Id:{job.Id}");
				return job;
			}

			return null;


		}
	}
}
