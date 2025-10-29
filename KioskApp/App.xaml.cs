using Microsoft.UI.Xaml;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KioskApp;

public partial class App : Application
{
	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int MessageBoxW(System.IntPtr hWnd, string lpText, string lpCaption, int uType);

	private Window? m_window;
	private Task? _serverTask;

	public App()
	{
		Debug.WriteLine("App constructor called");
		this.InitializeComponent();
		Debug.WriteLine("InitializeComponent completed");
		
		// Catch unhandled exceptions to prevent silent crashes
		this.UnhandledException += App_UnhandledException;
		Debug.WriteLine("App constructor completed");
	}

	private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
	{
		// Log the exception
		Logger.Log($"UNHANDLED EXCEPTION: {e.Exception.Message}");
		Logger.Log(e.Exception.StackTrace ?? "<no stack>");
		
		try
		{
			MessageBoxW(System.IntPtr.Zero, $"An unrecoverable error occurred.\n\n{e.Exception.Message}", "Kiosk App Error", 0x00000010 /* MB_ICONERROR */);
		}
		catch { }

		// Prevent crash to allow log capture in some cases
		e.Handled = true;
	}

	protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
	{
		Debug.WriteLine("OnLaunched reached");
		try
		{
			Logger.Log("=== OneRoom Health Kiosk App Starting ===");
			Debug.WriteLine("Creating MainWindow...");
			
			m_window = new MainWindow();
			Debug.WriteLine("MainWindow created, calling Activate()...");
			
			m_window.Activate();
			Debug.WriteLine("Window activated");
			Logger.Log("MainWindow created and activated");

			// Start in-process localhost command server without blocking UI thread
			if (m_window is MainWindow mainWindow)
			{
				Debug.WriteLine("Starting LocalCommandServer...");
				_serverTask = LocalCommandServer.StartAsync(mainWindow);
				Logger.Log("LocalCommandServer start requested");
			}
			
			Debug.WriteLine("OnLaunched completed successfully");
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"EXCEPTION in OnLaunched: {ex.GetType().Name}: {ex.Message}");
			Debug.WriteLine($"Stack trace: {ex.StackTrace}");
			
			Logger.Log($"LAUNCH ERROR: {ex.Message}");
			Logger.Log(ex.StackTrace ?? "<no stack>");
			try
			{
				MessageBoxW(System.IntPtr.Zero, $"Failed to start Kiosk App.\n\n{ex.Message}", "Kiosk App Error", 0x00000010);
			}
			catch { }
			throw;
		}
	}
}

