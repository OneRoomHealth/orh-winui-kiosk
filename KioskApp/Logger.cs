using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace KioskApp
{
	public static class Logger
	{
		private static readonly object Sync = new object();
		private static string _logFilePath = null;
		private static bool _initializationFailed = false;

		// Win32 MessageBox for critical logging errors
		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, int uType);

		/// <summary>
		/// Gets the current log file path.
		/// </summary>
		public static string LogFilePath => _logFilePath ?? InitializeLogPath();

		private static string InitializeLogPath()
		{
			if (_logFilePath != null || _initializationFailed)
				return _logFilePath ?? Path.Combine(Path.GetTempPath(), "kiosk.log");

			try
			{
				// Prefer packaged app LocalState if available, otherwise fallback to %LOCALAPPDATA%
				string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
				string appDir = Path.Combine(baseDir, "OneRoomHealthKiosk");
				
				// Create logs subdirectory to match configuration expectation
				string logsDir = Path.Combine(appDir, "logs");
				
				// Try to create the directory
				if (!Directory.Exists(logsDir))
				{
					Directory.CreateDirectory(logsDir);
					Debug.WriteLine($"Logger: Created log directory: {logsDir}");
				}
				
				_logFilePath = Path.Combine(logsDir, "kiosk.log");
				
				// Write initial log entry to verify file creation
				var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'");
				var line = $"[{timestamp}] Logger initialized. Log file: {_logFilePath}{Environment.NewLine}";
				File.AppendAllText(_logFilePath, line, Encoding.UTF8);
				
				Debug.WriteLine($"Logger: Log file initialized at: {_logFilePath}");
				return _logFilePath;
			}
			catch (Exception ex)
			{
				_initializationFailed = true;
				
				// Try to show error to user
				try
				{
					string errorMsg = $"Failed to initialize log file!\n\n" +
									 $"Error: {ex.Message}\n\n" +
									 $"Attempted path: {_logFilePath ?? "not set"}\n\n" +
									 $"Logs will be written to temp directory.";
					Debug.WriteLine($"Logger initialization error: {ex}");
					MessageBoxW(IntPtr.Zero, errorMsg, "Logging Error", 0x00000030); // MB_ICONWARNING
				}
				catch { }
				
				// Fallback to temp directory
				_logFilePath = Path.Combine(Path.GetTempPath(), "kiosk.log");
				return _logFilePath;
			}
		}

		public static void Log(string message)
		{
			try
			{
				if (string.IsNullOrEmpty(_logFilePath))
					InitializeLogPath();

				var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'");
				var line = $"[{timestamp}] {message}{Environment.NewLine}";
				
				// Also write to Debug output for immediate visibility
				Debug.WriteLine($"LOG: {message}");
				
				lock (Sync)
				{
					File.AppendAllText(LogFilePath, line, Encoding.UTF8);
				}
			}
			catch (Exception ex)
			{
				// Write to Debug output at least
				Debug.WriteLine($"Logger.Log error: {ex.Message} - Original message: {message}");
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
