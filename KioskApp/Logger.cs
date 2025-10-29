using System;
using System.IO;
using System.Text;

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
	}
}
