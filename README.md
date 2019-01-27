# Azure Media Services 
### "Encoding a video asset, publish the video as on-demand stream content..."


This is a simple repository for Azure Media Services asset management. In this repository you can find some simple Azure Fuctions examples to process video assets in Azure Media Services. 

> For simplicity and demostration these are simplified for demostration of main functions.

To have learn about Azure Media Services, check https://docs.microsoft.com/en-us/azure/media-services/latest/

## Scenario
The scnenario is simple and easy; some video files should be streamable to web/mobile platforms. To be able to do this, a video file has to be uploaded to Azure Media Service.

When a video file(*.mp4*) is uploaded to Azure Media Services' storage account or some other Azure Storage account, an Azure Fuction(*Encoder.cs*) is triggered. The function prepares the aaset and an encoing job and than start the job. When the encoding job is finished, the output assets are ready to be published. So when the encoding job is finished, with a notification message delivery method(webhook) of jobs in Azure Media Services, another Azure Function is triggered(*ListenEvent.cs*)

This function generates some publish policies and attached them to output assets and make the assets as streamable content. After these Azure Functions execution, the video asset is ready for streaming.

As another example there is a web trigerred Azure Function(*CheckJob.cs*) that checks an Azure Media Services job status with job id. If you can have Azure Media Services' job info, you can check its' status and do some operations according to job status.

> In a real application scenario Azure Event Grid is a very good sidekick for Azure Media Services. There is also small example to publish az Azure Event Grid event.



