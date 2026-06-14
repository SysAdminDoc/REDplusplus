using System;
using System.Diagnostics;

namespace RED
{
	internal static class EventLogWriter
	{
		private const string SourceName = "RED++";
		private const string LogName = "Application";

		internal static void WriteRunSummary(string scanPaths, string deleteMode,
			int emptyDirs, int emptyFiles, int deleted, int failed, int exitCode)
		{
			try
			{
				if (!EventLog.SourceExists(SourceName))
				{
					EventLog.CreateEventSource(SourceName, LogName);
				}

				// Folder names reach the Event Viewer here; neutralize bidi/zero-width/
				// control characters as the console and log lines already do.
				scanPaths = RED.Helper.RedAssist.SanitizeDisplay(scanPaths);

				string message = string.Format(
					"RED++ scan completed.\r\n" +
					"Paths: {0}\r\n" +
					"Mode: {1}\r\n" +
					"Empty directories found: {2}\r\n" +
					"Empty files found: {3}\r\n" +
					"Deleted: {4}\r\n" +
					"Failed: {5}\r\n" +
					"Exit code: {6}",
					scanPaths, deleteMode, emptyDirs, emptyFiles, deleted, failed, exitCode);

				EventLogEntryType entryType = (failed > 0 || exitCode == 1)
					? EventLogEntryType.Warning
					: EventLogEntryType.Information;

				EventLog.WriteEntry(SourceName, message, entryType, 1000);
			}
			catch
			{
				// Event source registration requires admin on first run.
				// Fail silently — the run log and exit code are still available.
			}
		}
	}
}
