
namespace AssetManager.Common
{
	using System;
	using System.Collections.Generic;

	internal class JobEvent
	{
		public string EventType { get; set; }
		public string JobId { get; set; }
	}

	internal enum NotificationEventType
	{
		None = 0,
		JobStateChange = 1,
		NotificationEndPointRegistration = 2,
		NotificationEndPointUnregistration = 3,
		TaskStateChange = 4,
		TaskProgress = 5
	}

	internal sealed class NotificationEvent
	{
		public string MessageVersion { get; set; }
		public string ETag { get; set; }
		public NotificationEventType EventType { get; set; }
		public DateTime TimeStamp { get; set; }
		public IDictionary<string, string> Properties { get; set; }
	}
}
