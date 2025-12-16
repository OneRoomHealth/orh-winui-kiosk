using Microsoft.UI.Xaml;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using OneRoomHealth.Hardware.Services;
using OneRoomHealth.Hardware.Modules.Display;
using OneRoomHealth.Hardware.Modules.Chromium;
using OneRoomHealth.Hardware.Modules.Camera;
using OneRoomHealth.Hardware.Modules.Lighting;
using OneRoomHealth.Hardware.Modules.SystemAudio;
using OneRoomHealth.Hardware.Modules.Microphone;
using OneRoomHealth.Hardware.Modules.Speaker;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace KioskApp;

public partial class App : Application
{
	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int MessageBoxW(System.IntPtr hWnd, string lpText, string lpCaption, int uType);

	private Window? m_window;
	private ServiceProvider? _serviceProvider;
	private HardwareApiServer? _hardwareApiServer;
	private HealthMonitorService? _healthMonitorService;
	private CancellationTokenSource? _servicesCts;

	public App()
	{
		Debug.WriteLine("App constructor called");
		
		try
		{
			this.InitializeComponent();
			Debug.WriteLine("InitializeComponent completed");
		}
		catch (Exception ex)
		{
			string errorMsg = $"FATAL: InitializeComponent failed!\n\n{ex.GetType().Name}\n{ex.Message}\n\nStack:\n{ex.StackTrace}";
			MessageBoxW(IntPtr.Zero, errorMsg, "App Initialization Error", 0x00000010);
			throw;
		}
		
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

	protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
	{
		Debug.WriteLine("OnLaunched reached");

		try
		{
			// First thing - try to initialize logger and report its path
			Debug.WriteLine($"Logger path: {Logger.LogFilePath}");

			Logger.Log("=== OneRoom Health Kiosk App Starting ===");
			Logger.Log($"Log file location: {Logger.LogFilePath}");
			Logger.Log($"Current user: {Environment.UserName}");
			Logger.Log($"App directory: {Directory.GetCurrentDirectory()}");

			Debug.WriteLine("Loading configuration...");

			// Load configuration
			var config = ConfigurationManager.Load();
			Logger.Log("Configuration loaded");

			// Set up dependency injection and services
			await InitializeServicesAsync(config);

			Debug.WriteLine("Creating MainWindow...");
			m_window = new MainWindow(config);
			Debug.WriteLine("MainWindow created, calling Activate()...");

			m_window.Activate();
			Debug.WriteLine("Window activated");
			Logger.Log("MainWindow created and activated");

			// Start local navigation command server (port 8787) so external tools can instruct the kiosk to navigate.
			// Endpoint: POST http://127.0.0.1:8787/navigate  { "url": "https://example.com" }
			// (Runs in background; StartAsync internally handles "access denied" and "port in use" cases.)
			if (m_window is MainWindow mainWindow)
			{
				_ = LocalCommandServer.StartAsync(mainWindow);
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

	private async Task InitializeServicesAsync(KioskConfiguration config)
	{
		Debug.WriteLine("Initializing services...");
		Logger.Log("Initializing hardware services...");

		// Configure Serilog
		var logPath = Environment.ExpandEnvironmentVariables(config.Logging.Path);
		var logFile = Path.Combine(logPath, "hardware.log");

		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Debug()
			.WriteTo.Console()
			.WriteTo.File(logFile,
				rollingInterval: RollingInterval.Day,
				fileSizeLimitBytes: config.Logging.MaxSizeKb * 1024,
				retainedFileCountLimit: config.Logging.MaxFiles)
			.CreateLogger();

		Logger.Log($"Serilog configured, hardware logs at: {logFile}");

		// Build service container
		var services = new ServiceCollection();

		// Add logging
		services.AddLogging(builder =>
		{
			builder.ClearProviders();
			builder.AddSerilog(Log.Logger);
		});

		// Add HttpClient for hardware modules
		services.AddSingleton<HttpClient>();

		// Register hardware modules
		if (config.Hardware.Displays != null && config.Hardware.Displays.Enabled)
		{
			services.AddSingleton(sp =>
			{
				var logger = sp.GetRequiredService<ILogger<DisplayModule>>();
				var httpClient = sp.GetRequiredService<HttpClient>();
				return new DisplayModule(logger, config.Hardware.Displays, httpClient);
			});
			Logger.Log("DisplayModule registered");
		}

		if (config.Hardware.Chromium != null && config.Hardware.Chromium.Enabled)
		{
			services.AddSingleton(sp =>
			{
				var logger = sp.GetRequiredService<ILogger<ChromiumModule>>();
				var httpClient = sp.GetRequiredService<HttpClient>();
				return new ChromiumModule(logger, config.Hardware.Chromium, httpClient);
			});
			Logger.Log("ChromiumModule registered");
		}

		// Register Camera module
		if (config.Hardware.Cameras != null && config.Hardware.Cameras.Enabled)
		{
			services.AddSingleton(sp =>
			{
				var logger = sp.GetRequiredService<ILogger<CameraModule>>();
				return new CameraModule(logger, config.Hardware.Cameras);
			});
			Logger.Log("CameraModule registered");
		}

		// Register Lighting module
		if (config.Hardware.Lighting != null && config.Hardware.Lighting.Enabled)
		{
			services.AddSingleton(sp =>
			{
				var logger = sp.GetRequiredService<ILogger<LightingModule>>();
				return new LightingModule(logger, config.Hardware.Lighting);
			});
			Logger.Log("LightingModule registered");
		}

		// Register SystemAudio module
		if (config.Hardware.SystemAudio != null && config.Hardware.SystemAudio.Enabled)
		{
			services.AddSingleton(sp =>
			{
				var logger = sp.GetRequiredService<ILogger<SystemAudioModule>>();
				return new SystemAudioModule(logger, config.Hardware.SystemAudio);
			});
			Logger.Log("SystemAudioModule registered");
		}

		// Register Microphone module
		if (config.Hardware.Microphones != null && config.Hardware.Microphones.Enabled)
		{
			services.AddSingleton(sp =>
			{
				var logger = sp.GetRequiredService<ILogger<MicrophoneModule>>();
				var httpClient = sp.GetRequiredService<HttpClient>();
				return new MicrophoneModule(logger, config.Hardware.Microphones, httpClient);
			});
			Logger.Log("MicrophoneModule registered");
		}

		// Register Speaker module
		if (config.Hardware.Speakers != null && config.Hardware.Speakers.Enabled)
		{
			services.AddSingleton(sp =>
			{
				var logger = sp.GetRequiredService<ILogger<SpeakerModule>>();
				var httpClient = sp.GetRequiredService<HttpClient>();
				return new SpeakerModule(logger, config.Hardware.Speakers, httpClient);
			});
			Logger.Log("SpeakerModule registered");
		}

		// Register hardware services
		services.AddSingleton<HardwareManager>();
		services.AddSingleton<HealthMonitorService>();

		// Register API server with configured port
		services.AddSingleton(sp =>
		{
			var logger = sp.GetRequiredService<ILogger<HardwareApiServer>>();
			var hwManager = sp.GetRequiredService<HardwareManager>();
			return new HardwareApiServer(logger, hwManager, config.HttpApi.Port);
		});

		// Build the service provider
		_serviceProvider = services.BuildServiceProvider();

		Logger.Log("Service provider built");

		// Initialize hardware manager
		var hardwareManager = _serviceProvider.GetRequiredService<HardwareManager>();

		// Register hardware modules with the manager
		var displayModule = _serviceProvider.GetService<DisplayModule>();
		if (displayModule != null)
		{
			hardwareManager.RegisterModule(displayModule);
			Logger.Log("DisplayModule registered with HardwareManager");
		}

		var chromiumModule = _serviceProvider.GetService<ChromiumModule>();
		if (chromiumModule != null)
		{
			hardwareManager.RegisterModule(chromiumModule);
			Logger.Log("ChromiumModule registered with HardwareManager");
		}

		var cameraModule = _serviceProvider.GetService<CameraModule>();
		if (cameraModule != null)
		{
			hardwareManager.RegisterModule(cameraModule);
			Logger.Log("CameraModule registered with HardwareManager");
		}

		var lightingModule = _serviceProvider.GetService<LightingModule>();
		if (lightingModule != null)
		{
			hardwareManager.RegisterModule(lightingModule);
			Logger.Log("LightingModule registered with HardwareManager");
		}

		var systemAudioModule = _serviceProvider.GetService<SystemAudioModule>();
		if (systemAudioModule != null)
		{
			hardwareManager.RegisterModule(systemAudioModule);
			Logger.Log("SystemAudioModule registered with HardwareManager");
		}

		var microphoneModule = _serviceProvider.GetService<MicrophoneModule>();
		if (microphoneModule != null)
		{
			hardwareManager.RegisterModule(microphoneModule);
			Logger.Log("MicrophoneModule registered with HardwareManager");
		}

		var speakerModule = _serviceProvider.GetService<SpeakerModule>();
		if (speakerModule != null)
		{
			hardwareManager.RegisterModule(speakerModule);
			Logger.Log("SpeakerModule registered with HardwareManager");
		}

		// Initialize all registered modules
		await hardwareManager.InitializeAllModulesAsync();

		Logger.Log("Hardware manager initialized");

		// Start API server if enabled
		if (config.HttpApi.Enabled)
		{
			_hardwareApiServer = _serviceProvider.GetRequiredService<HardwareApiServer>();
			await _hardwareApiServer.StartAsync();
			Logger.Log($"Hardware API server started on port {config.HttpApi.Port}");
		}

		// Start health monitoring
		_servicesCts = new CancellationTokenSource();
		_healthMonitorService = _serviceProvider.GetRequiredService<HealthMonitorService>();
		_ = _healthMonitorService.StartAsync(_servicesCts.Token);

		Logger.Log("Health monitoring service started");
		Logger.Log("All hardware services initialized successfully");
	}

	public async Task ShutdownServicesAsync()
	{
		Logger.Log("Shutting down hardware services...");

		try
		{
			// Stop health monitoring
			if (_healthMonitorService != null && _servicesCts != null)
			{
				_servicesCts.Cancel();
				await _healthMonitorService.StopAsync(CancellationToken.None);
				Logger.Log("Health monitoring stopped");
			}

			// Stop API server
			if (_hardwareApiServer != null)
			{
				await _hardwareApiServer.StopAsync();
				Logger.Log("API server stopped");
			}

			// Shutdown hardware manager
			if (_serviceProvider != null)
			{
				var hardwareManager = _serviceProvider.GetService<HardwareManager>();
				if (hardwareManager != null)
				{
					await hardwareManager.ShutdownAllModulesAsync();
					Logger.Log("Hardware manager shutdown complete");
				}
			}

			// Dispose service provider
			_serviceProvider?.Dispose();
			_servicesCts?.Dispose();

			// Flush and close Serilog
			Log.CloseAndFlush();

			Logger.Log("All services shut down successfully");
		}
		catch (Exception ex)
		{
			Logger.Log($"Error during shutdown: {ex.Message}");
		}
	}
}

