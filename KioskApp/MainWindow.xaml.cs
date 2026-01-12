using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using KioskApp.Helpers;

namespace KioskApp;

/// <summary>
/// Main window for the OneRoom Health Kiosk application.
/// This partial class contains core fields, constructor, and initialization.
/// Other functionality is split across:
/// - MainWindow.Keyboard.cs - Keyboard hooks, hotkeys, accelerators
/// - MainWindow.Window.cs - Window configuration, positioning, monitor management
/// - MainWindow.WebView.cs - WebView2 initialization, setup, scripts, messages
/// - MainWindow.Debug.cs - Debug mode, exit handling, video/screensaver modes
/// - MainWindow.MediaDevices.cs - Camera/microphone device management
/// - MainWindow.Panels.cs - Navigation handlers, health panel, log viewer
/// </summary>
public sealed partial class MainWindow : Window
{
    #region Core Fields

    /// <summary>
    /// Application configuration loaded from config.json.
    /// </summary>
    private readonly KioskConfiguration _config;

    /// <summary>
    /// Video controller for video mode playback.
    /// </summary>
    private VideoController? _videoController;

    /// <summary>
    /// Current URL being displayed in the WebView.
    /// </summary>
    private string? _currentUrl = null;

    /// <summary>
    /// Indicates whether the application is currently in debug mode.
    /// </summary>
    private bool _isDebugMode = false;

    /// <summary>
    /// Indicates whether the application is in video mode (vs screensaver/WebView mode).
    /// </summary>
    private bool _isVideoMode = false;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes the main window with the provided configuration.
    /// </summary>
    /// <param name="config">The kiosk configuration loaded from config.json.</param>
    public MainWindow(KioskConfiguration config)
    {
        this.InitializeComponent();
        _config = config;
        Logger.Log("MainWindow constructor called");
        Logger.Log($"Log file is being written to: {Logger.LogFilePath}");

        // Load persisted media device preferences (camera/mic) so they apply across app restarts.
        LoadPersistedMediaDevicePreferences();

        // Subscribe to log events for real-time updates
        Logger.LogAdded += OnLogAdded;

        // Always start in screensaver mode (WebView visible)
        _isVideoMode = false;
        Logger.Log("Starting in screensaver mode (default)");

        // Initialize current monitor index from config
        _currentMonitorIndex = _config.Kiosk.TargetMonitorIndex;

        // Initialize video controller if video configuration exists (even if not enabled)
        if (_config.Kiosk.VideoMode != null)
        {
            _videoController = new VideoController(_config.Kiosk.VideoMode, _currentMonitorIndex);
            Logger.Log("Video controller initialized (available for hotkey activation)");
        }

        // Hook Activated event - do all initialization there when window is fully ready
        this.Activated += MainWindow_Activated;
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Handles the window Activated event to perform one-time initialization.
    /// </summary>
    private void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
    {
        // Only initialize once on first activation
        if (e.WindowActivationState != WindowActivationState.Deactivated && _appWindow == null)
        {
            Debug.WriteLine("==================== WINDOW ACTIVATION START ====================");
            Logger.Log("==================== WINDOW ACTIVATION START ====================");
            Debug.WriteLine("MainWindow_Activated event fired (first activation)");
            Logger.Log("MainWindow.Activated event fired");
            Logger.Log($"Video mode: {_isVideoMode}");
            Logger.Log($"Target monitor index: {_currentMonitorIndex} (1-based)");

            // Configure kiosk window after it's activated
            ConfigureAsKioskWindow();

            // Setup comprehensive keyboard handling
            SetupKeyboardHandling();

            // Initialize WebView2 asynchronously without blocking the Activated event
            _ = InitializeWebViewAsync();

            Logger.Log("==================== WINDOW ACTIVATION COMPLETE ====================");
        }
    }

    #endregion
}
