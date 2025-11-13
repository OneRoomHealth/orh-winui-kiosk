using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace KioskApp
{
	public static class Logger
	{
		private static readonly object Sync = new object();
		private static readonly string LogFilePath = InitializeLogPath();

		private static string InitializeLogPath()
		{
			try
			{
				// Prefer packaged app LocalState if available, otherwise fallback to %LOCALAPPDATA%
				string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
				string appDir = Path.Combine(baseDir, "OneRoomHealthKiosk");
				Directory.CreateDirectory(appDir);
				return Path.Combine(appDir, "kiosk.log");
			}
			catch
			{
				return Path.Combine(Path.GetTempPath(), "kiosk.log");
			}
		}

		public static void Log(string message)
		{
			try
			{
				var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'");
				var line = $"[{timestamp}] {message}{Environment.NewLine}";
				lock (Sync)
				{
					File.AppendAllText(LogFilePath, line, Encoding.UTF8);
				}
			}
			catch
			{
				// Swallow logging errors
			}
		}

		/// <summary>
		/// Logs a security event with additional context for audit trail.
		/// These events are logged both to file and Windows Event Log.
		/// </summary>
		public static void LogSecurityEvent(string eventType, string details)
		{
			try
			{
				var auditEntry = new
				{
					Timestamp = DateTime.UtcNow,
					EventType = eventType,
					User = Environment.UserName,
					Machine = Environment.MachineName,
					Details = details
				};

				var auditJson = JsonSerializer.Serialize(auditEntry);

				// Log to file with AUDIT prefix
				Log($"AUDIT: {auditJson}");

				// Also log to Windows Event Log if possible
				try
				{
					// Create event source if it doesn't exist (requires admin on first run)
					const string sourceName = "OneRoomHealthKiosk";
					const string logName = "Application";

					if (!EventLog.SourceExists(sourceName))
					{
						EventLog.CreateEventSource(sourceName, logName);
					}

					EventLog.WriteEntry(
						sourceName,
						auditJson,
						EventLogEntryType.Information,
						1000); // Event ID 1000 for security events
				}
				catch
				{
					// Swallow event log errors - continue with file logging
				}
			}
			catch
			{
				// Swallow audit logging errors
			}
		}
	}
}
