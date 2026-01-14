using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using KioskApp.Helpers;
using OneRoomHealth.Hardware.ViewModels;

namespace KioskApp;

/// <summary>
/// MainWindow partial class - Debug mode, exit handling, and video/screensaver mode switching.
/// </summary>
public sealed partial class MainWindow
{
    #region Debug Mode State

    private enum DebugTab { Health, Logs, Performance }
    private DebugTab _activeTab = DebugTab.Health;
    private Timer? _debugModeRefreshTimer;
    private ModuleHealthViewModel? _selectedModule;
    private DateTime _debugModeStartTime;

    #endregion

    #region Debug Mode

    /// <summary>
    /// Toggles between debug mode and kiosk mode.
    /// </summary>
    private async Task ToggleDebugMode()
    {
        if (_isDebugMode)
        {
            await ExitDebugMode();
        }
        else
        {
            await EnterDebugMode();
        }
    }

    /// <summary>
    /// Enters debug mode: windows the application and enables developer tools.
    /// </summary>
    private async Task EnterDebugMode()
    {
        Logger.LogSecurityEvent("EnterDebugMode", "Entering debug mode");
        ShowStatus("DEBUG MODE", "Initializing debug mode...");

        try
        {
            // In video mode, stop video and show WebView
            if (_isVideoMode && _videoController != null)
            {
                await _videoController.StopAsync();
                var tcs = new TaskCompletionSource<bool>();
                DispatcherQueue.TryEnqueue(() =>
                {
                    KioskWebView.Visibility = Visibility.Visible;
                    tcs.SetResult(true);
                });
                await tcs.Task;
            }

            // Ensure WebView2 is initialized before showing debug panel
            if (KioskWebView == null)
            {
                Logger.Log("KioskWebView is null, cannot enter debug mode");
                ShowStatus("Error", "WebView2 control is not available");
                return;
            }

            if (KioskWebView.CoreWebView2 == null)
            {
                Logger.Log("WebView2 not initialized, initializing now...");
                ShowStatus("DEBUG MODE", "Initializing WebView2...");

                try
                {
                    await KioskWebView.EnsureCoreWebView2Async();
                    await SetupWebViewAsync();

                    // Navigate to default URL if we don't have a current URL
                    if (string.IsNullOrEmpty(_currentUrl))
                    {
                        _currentUrl = _config.Kiosk.DefaultUrl;
                        KioskWebView.Source = new Uri(_currentUrl);
                    }

                    Logger.Log("WebView2 initialized successfully");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to initialize WebView2: {ex.Message}");
                    ShowStatus("Error", $"Failed to initialize WebView2: {ex.Message}");
                    return;
                }
            }

            // Now WebView2 is guaranteed to be ready - show debug UI and configure window
            _debugModeStartTime = DateTime.UtcNow;
            var uiTcs = new TaskCompletionSource<bool>();
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    // Show new debug UI components
                    DebugModeContainer.Visibility = Visibility.Visible;
                    TabbedBottomPanel.Visibility = Visibility.Visible;
                    DebugStatusBar.Visibility = Visibility.Visible;

                    // Set default active tab
                    _activeTab = DebugTab.Health;
                    UpdateTabStyles();
                    ShowActiveTabContent();

                    // Update URL textbox with current URL
                    if (!string.IsNullOrEmpty(_currentUrl))
                    {
                        UrlTextBox.Text = _currentUrl;
                    }
                    else if (KioskWebView?.Source != null)
                    {
                        UrlTextBox.Text = KioskWebView.Source.ToString();
                    }

                    // Enable WebView2 developer features
                    if (KioskWebView?.CoreWebView2?.Settings != null)
                    {
                        var settings = KioskWebView.CoreWebView2.Settings;
                        settings.AreDevToolsEnabled = true;
                        settings.AreDefaultContextMenusEnabled = true;
                        settings.AreBrowserAcceleratorKeysEnabled = true;
                    }

                    // Clear stale camera/microphone dropdown data
                    _suppressMediaSelectionEvents = true;
                    try
                    {
                        var oldCameraCount = CameraSelector.Items.Count;
                        var oldMicCount = MicrophoneSelector.Items.Count;
                        CameraSelector.Items.Clear();
                        MicrophoneSelector.Items.Clear();
                        CameraSelector.SelectedIndex = -1;
                        MicrophoneSelector.SelectedIndex = -1;
                        Logger.Log($"[DEBUG MODE] Cleared stale dropdown data (was: {oldCameraCount} cameras, {oldMicCount} mics)");
                    }
                    finally
                    {
                        _suppressMediaSelectionEvents = false;
                    }

                    // Enumerate devices for dropdowns
                    _ = LoadAllMediaDevicesAsync();

                    // Window the application - remove fullscreen presenter
                    if (_appWindow?.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
                    {
                        _appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped);
                    }

                    // Restore window frame
                    var style = Win32Native.GetWindowLong(_hwnd, Win32Native.GWL_STYLE);
                    style |= Win32Native.WS_CAPTION | Win32Native.WS_THICKFRAME | Win32Native.WS_MINIMIZEBOX | Win32Native.WS_MAXIMIZEBOX | Win32Native.WS_SYSMENU;
                    Win32Native.SetWindowLong(_hwnd, Win32Native.GWL_STYLE, style);

                    // Remove topmost
                    var exStyle = Win32Native.GetWindowLong(_hwnd, Win32Native.GWL_EXSTYLE);
                    exStyle &= ~Win32Native.WS_EX_TOPMOST;
                    Win32Native.SetWindowLong(_hwnd, Win32Native.GWL_EXSTYLE, exStyle);

                    // Apply changes
                    Win32Native.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                        Win32Native.SWP_SHOWWINDOW | Win32Native.SWP_FRAMECHANGED | Win32Native.SWP_NOZORDER | Win32Native.SWP_NOSIZE | Win32Native.SWP_NOMOVE);

                    // Update window title
                    this.Title = "[DEBUG MODE] OneRoom Health Kiosk";

                    // Start refresh timer for debug UI updates
                    StartDebugModeRefreshTimer();

                    // Initial data refresh
                    RefreshHealthDisplay();
                    InitializeLogFilters();
                    RefreshLogDisplay();
                    RefreshPerformanceDisplay();
                    UpdateDebugModeStatusDisplays();

                    _isDebugMode = true;
                    Logger.Log("Debug mode enabled with new tabbed UI");

                    ShowStatus("DEBUG MODE", "Developer tools enabled. Press Ctrl+Shift+I to exit.");
                    _ = Task.Delay(2000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));

                    uiTcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error configuring debug mode UI: {ex.Message}");
                    ShowStatus("Debug Mode Error", ex.Message);
                    uiTcs.SetException(ex);
                }
            });
            await uiTcs.Task;
        }
        catch (Exception ex)
        {
            Logger.Log($"Error entering debug mode: {ex.Message}");
            ShowStatus("Error", $"Failed to enter debug mode: {ex.Message}");
        }
    }

    /// <summary>
    /// Exits debug mode: returns to fullscreen kiosk mode.
    /// </summary>
    private async Task ExitDebugMode()
    {
        Logger.LogSecurityEvent("ExitDebugMode", "Exiting debug mode");

        // Stop the debug mode refresh timer
        StopDebugModeRefreshTimer();

        await DispatcherQueue.EnqueueAsync(() =>
        {
            try
            {
                // Disable developer features
                if (KioskWebView?.CoreWebView2?.Settings != null)
                {
                    var settings = KioskWebView.CoreWebView2.Settings;
                    settings.AreDevToolsEnabled = false;
                    settings.AreDefaultContextMenusEnabled = false;
                    settings.AreBrowserAcceleratorKeysEnabled = false;
                }

                // Unsubscribe from log updates if logs tab was active
                if (_logsVisible)
                {
                    UnifiedLogger.Instance.LogAdded -= OnUnifiedLogAdded;
                    _logsVisible = false;
                }

                // Unsubscribe from performance updates
                PerformanceMonitor.Instance.SnapshotTaken -= OnPerformanceSnapshot;

                // Stop health refresh timer
                _healthRefreshTimer?.Dispose();
                _healthRefreshTimer = null;

                // Hide all debug UI components
                DebugModeContainer.Visibility = Visibility.Collapsed;
                TabbedBottomPanel.Visibility = Visibility.Collapsed;
                DebugStatusBar.Visibility = Visibility.Collapsed;
                ModuleDetailPanel.Visibility = Visibility.Collapsed;

                // Reset WebView margin
                if (KioskWebView != null)
                    KioskWebView.Margin = new Thickness(0);

                // Update window title
                this.Title = "OneRoom Health Kiosk";

                // Set presenter to Overlapped
                if (_appWindow != null)
                {
                    _appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped);
                    Logger.Log("Set presenter to Overlapped before configuration");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error in ExitDebugMode UI cleanup: {ex.Message}");
            }
        });

        // Wait for presenter change to take effect
        await Task.Delay(300);

        // Now restore kiosk window configuration
        await DispatcherQueue.EnqueueAsync(() =>
        {
            try
            {
                ConfigureAsKioskWindow();

                // Reset WebView to fill window
                if (_appWindow != null && KioskWebView != null)
                {
                    KioskWebView.Width = double.NaN;
                    KioskWebView.Height = double.NaN;
                    KioskWebView.HorizontalAlignment = HorizontalAlignment.Stretch;
                    KioskWebView.VerticalAlignment = VerticalAlignment.Stretch;
                    KioskWebView.Margin = new Thickness(0);
                    Logger.Log("Reset WebView size to Auto/Stretch");
                }

                // Force layout update
                if (this.Content is UIElement content)
                {
                    content.UpdateLayout();
                }
                if (KioskWebView != null)
                {
                    KioskWebView.UpdateLayout();
                    Logger.Log("Forced WebView layout update");
                }

                // Re-run ConfigureAsKioskWindow after delay to ensure proper sizing
                _ = Task.Delay(200).ContinueWith(async _ =>
                {
                    await DispatcherQueue.EnqueueAsync(() =>
                    {
                        ConfigureAsKioskWindow();
                        Logger.Log("Re-ran ConfigureAsKioskWindow after delay to ensure proper sizing");

                        if (this.Content is UIElement content2)
                        {
                            content2.UpdateLayout();
                        }
                        if (KioskWebView != null)
                        {
                            KioskWebView.UpdateLayout();
                            Logger.Log("Forced final layout update");
                        }
                    });
                });

                // In video mode, hide WebView and restart video
                if (_isVideoMode && _videoController != null)
                {
                    if (KioskWebView != null)
                        KioskWebView.Visibility = Visibility.Collapsed;
                    _ = _videoController.InitializeAsync();
                }
                else
                {
                    // Trigger a resize event so the content adapts to the new window size
                    _ = Task.Delay(300).ContinueWith(_ =>
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (KioskWebView?.CoreWebView2 != null)
                            {
                                _ = KioskWebView.CoreWebView2.ExecuteScriptAsync("window.dispatchEvent(new Event('resize'));").AsTask();
                                Logger.Log("[DEBUG EXIT] Triggered resize event in web content (no navigation/reload)");
                            }
                        });
                    });
                }

                _isDebugMode = false;
                Logger.Log("Debug mode disabled, returned to kiosk mode");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error exiting debug mode: {ex.Message}");
            }
        });
    }

    #endregion

    #region Debug Mode Timer

    private void StartDebugModeRefreshTimer()
    {
        _debugModeRefreshTimer = new Timer(
            _ => DispatcherQueue.TryEnqueue(() =>
            {
                if (_isDebugMode)
                {
                    UpdateDebugModeStatusDisplays();
                }
            }),
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
    }

    private void StopDebugModeRefreshTimer()
    {
        _debugModeRefreshTimer?.Dispose();
        _debugModeRefreshTimer = null;
    }

    private void UpdateDebugModeStatusDisplays()
    {
        try
        {
            // Update uptime displays
            var uptime = DateTime.UtcNow - _debugModeStartTime;
            var uptimeText = FormatUptime(uptime);

            TitleBarUptimeText.Text = $"Uptime: {uptimeText}";
            PerfUptimeText.Text = $"Uptime: {PerformanceMonitor.Instance.GetUptimeFormatted()}";

            // Update API status
            var apiConnected = App.HardwareApiServer?.IsRunning ?? false;
            TitleBarApiStatusIcon.Foreground = new SolidColorBrush(
                apiConnected ? ColorHelper.FromArgb(255, 78, 201, 176) : ColorHelper.FromArgb(255, 244, 135, 113));
            TitleBarApiEndpoint.Text = $"localhost:{_config.HttpApi.Port}";

            // Update module count in status bar
            var service = App.HealthVisualization;
            if (service != null)
            {
                var summary = service.SystemSummary;
                StatusBarModuleCount.Text = $"\U0001F50C {summary.ActiveModules}/{summary.TotalModules} modules connected";

                // Update health issue badge
                var issueCount = summary.TotalDevices - summary.HealthyDevices;
                if (issueCount > 0)
                {
                    HealthIssueBadge.Visibility = Visibility.Visible;
                    HealthIssueBadgeText.Text = issueCount == 1 ? "1 issue" : $"{issueCount} issues";
                }
                else
                {
                    HealthIssueBadge.Visibility = Visibility.Collapsed;
                }
            }

            // Update last refresh time
            StatusBarLastRefresh.Text = $"Last refresh: {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            Logger.Log($"Error updating debug mode status: {ex.Message}");
        }
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalHours >= 1)
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        if (uptime.TotalMinutes >= 1)
            return $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";
        return $"{(int)uptime.TotalSeconds}s";
    }

    #endregion

    #region Tab Switching

    private void HealthTabButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab(DebugTab.Health);
    }

    private void LogsTabButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab(DebugTab.Logs);
    }

    private void PerfTabButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab(DebugTab.Performance);
    }

    private void SwitchToTab(DebugTab tab)
    {
        // Unsubscribe from previous tab's updates
        if (_activeTab == DebugTab.Logs && tab != DebugTab.Logs)
        {
            UnifiedLogger.Instance.LogAdded -= OnUnifiedLogAdded;
            _logsVisible = false;
        }
        if (_activeTab == DebugTab.Performance && tab != DebugTab.Performance)
        {
            PerformanceMonitor.Instance.SnapshotTaken -= OnPerformanceSnapshot;
            _perfPanelVisible = false;
        }
        if (_activeTab == DebugTab.Health && tab != DebugTab.Health)
        {
            _healthRefreshTimer?.Dispose();
            _healthRefreshTimer = null;
            _healthPanelVisible = false;
        }

        _activeTab = tab;
        UpdateTabStyles();
        ShowActiveTabContent();

        // Subscribe to new tab's updates
        switch (tab)
        {
            case DebugTab.Health:
                _healthPanelVisible = true;
                RefreshHealthDisplay();
                _healthRefreshTimer = new Timer(
                    _ => DispatcherQueue.TryEnqueue(() => RefreshHealthDisplay()),
                    null,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(2));
                break;

            case DebugTab.Logs:
                _logsVisible = true;
                UnifiedLogger.Instance.LogAdded += OnUnifiedLogAdded;
                InitializeLogFilters();
                RefreshLogDisplay();
                break;

            case DebugTab.Performance:
                _perfPanelVisible = true;
                PerformanceMonitor.Instance.SnapshotTaken += OnPerformanceSnapshot;
                RefreshPerformanceDisplay();
                break;
        }

        // Hide module detail panel when switching tabs
        ModuleDetailPanel.Visibility = Visibility.Collapsed;
        _selectedModule = null;

        Logger.Log($"Switched to {tab} tab");
    }

    private void UpdateTabStyles()
    {
        // Reset all tabs to inactive style
        HealthTabButton.Style = (Style)Application.Current.Resources["DebugTabButtonStyle"];
        LogsTabButton.Style = (Style)Application.Current.Resources["DebugTabButtonStyle"];
        PerfTabButton.Style = (Style)Application.Current.Resources["DebugTabButtonStyle"];

        // Set active tab style
        switch (_activeTab)
        {
            case DebugTab.Health:
                HealthTabButton.Style = (Style)Application.Current.Resources["DebugTabButtonActiveStyle"];
                break;
            case DebugTab.Logs:
                LogsTabButton.Style = (Style)Application.Current.Resources["DebugTabButtonActiveStyle"];
                break;
            case DebugTab.Performance:
                PerfTabButton.Style = (Style)Application.Current.Resources["DebugTabButtonActiveStyle"];
                break;
        }
    }

    private void ShowActiveTabContent()
    {
        HealthTabContent.Visibility = _activeTab == DebugTab.Health ? Visibility.Visible : Visibility.Collapsed;
        LogsTabContent.Visibility = _activeTab == DebugTab.Logs ? Visibility.Visible : Visibility.Collapsed;
        PerfTabContent.Visibility = _activeTab == DebugTab.Performance ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshPanelButton_Click(object sender, RoutedEventArgs e)
    {
        switch (_activeTab)
        {
            case DebugTab.Health:
                _ = RefreshHealthAsync();
                break;
            case DebugTab.Logs:
                RefreshLogDisplay();
                break;
            case DebugTab.Performance:
                RefreshPerformanceDisplay();
                break;
        }
        Logger.Log($"Manual refresh triggered for {_activeTab} tab");
    }

    #endregion

    #region Export Diagnostics

    private async void ExportDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Logger.Log("Starting diagnostics export...");
            ShowStatus("Exporting", "Creating diagnostics bundle...");

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var exportPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"ORH_Diagnostics_{timestamp}.zip");

            await Task.Run(() =>
            {
                using var archive = ZipFile.Open(exportPath, ZipArchiveMode.Create);

                // Export configuration
                try
                {
                    var configPath = ConfigurationManager.GetConfigPath();
                    if (File.Exists(configPath))
                    {
                        archive.CreateEntryFromFile(configPath, "config.json");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to export config: {ex.Message}");
                }

                // Export logs
                try
                {
                    var logsDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "OneRoomHealthKiosk", "logs");

                    if (Directory.Exists(logsDir))
                    {
                        foreach (var logFile in Directory.GetFiles(logsDir, "*.log"))
                        {
                            var entryName = Path.Combine("logs", Path.GetFileName(logFile));
                            archive.CreateEntryFromFile(logFile, entryName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to export logs: {ex.Message}");
                }

                // Export health snapshot
                try
                {
                    var service = App.HealthVisualization;
                    if (service != null)
                    {
                        var snapshot = new
                        {
                            Timestamp = DateTime.UtcNow,
                            SystemSummary = service.SystemSummary,
                            Modules = service.GetModuleHealthSummaries()
                        };

                        var healthEntry = archive.CreateEntry("health_snapshot.json");
                        using var stream = healthEntry.Open();
                        using var writer = new StreamWriter(stream);
                        writer.Write(JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to export health snapshot: {ex.Message}");
                }

                // Export system info
                try
                {
                    var systemInfo = new
                    {
                        MachineName = Environment.MachineName,
                        OSVersion = Environment.OSVersion.ToString(),
                        ProcessorCount = Environment.ProcessorCount,
                        DotNetVersion = Environment.Version.ToString(),
                        WorkingSet = Environment.WorkingSet / 1024 / 1024,
                        Timestamp = DateTime.UtcNow
                    };

                    var sysEntry = archive.CreateEntry("system_info.json");
                    using var stream = sysEntry.Open();
                    using var writer = new StreamWriter(stream);
                    writer.Write(JsonSerializer.Serialize(systemInfo, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to export system info: {ex.Message}");
                }

                // Export recent unified logs
                try
                {
                    var recentLogs = UnifiedLogger.Instance.GetAllLogs(500);
                    var logsEntry = archive.CreateEntry("recent_logs.txt");
                    using var stream = logsEntry.Open();
                    using var writer = new StreamWriter(stream);
                    foreach (var log in recentLogs)
                    {
                        writer.WriteLine(log.FormattedMessage);
                        if (!string.IsNullOrEmpty(log.Exception))
                        {
                            writer.WriteLine($"    Exception: {log.Exception}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to export recent logs: {ex.Message}");
                }
            });

            Logger.Log($"Diagnostics exported to: {exportPath}");
            ShowStatus("Export Complete", $"Saved to Desktop: ORH_Diagnostics_{timestamp}.zip");
            _ = Task.Delay(3000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to export diagnostics: {ex.Message}");
            ShowStatus("Export Failed", ex.Message);
            _ = Task.Delay(3000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));
        }
    }

    #endregion

    #region Reset WebView

    private async void ResetWebViewButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Logger.Log("Resetting WebView to default URL...");
            ShowStatus("Resetting", "Navigating to default URL...");

            if (KioskWebView?.CoreWebView2 != null)
            {
                // Clear browsing data
                await KioskWebView.CoreWebView2.Profile.ClearBrowsingDataAsync();
                Logger.Log("Browsing data cleared");

                // Navigate to default URL
                _currentUrl = _config.Kiosk.DefaultUrl;
                KioskWebView.Source = new Uri(_currentUrl);
                UrlTextBox.Text = _currentUrl;

                Logger.Log($"WebView reset to: {_currentUrl}");
                ShowStatus("Reset Complete", "WebView navigated to default URL");
                _ = Task.Delay(2000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));
            }
            else
            {
                Logger.Log("Cannot reset WebView: CoreWebView2 not initialized");
                ShowStatus("Error", "WebView2 is not ready");
                _ = Task.Delay(2000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to reset WebView: {ex.Message}");
            ShowStatus("Reset Failed", ex.Message);
            _ = Task.Delay(2000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));
        }
    }

    #endregion

    #region Module Detail Panel

    private void ShowModuleDetail(ModuleHealthViewModel module)
    {
        _selectedModule = module;

        // Update header
        DetailModuleName.Text = $"{module.DisplayName} Details";
        DetailModuleStatus.Text = module.HealthIcon;
        DetailModuleStatus.Foreground = new SolidColorBrush(GetHealthColor(module.OverallHealth));

        // Populate properties
        DetailPropertiesPanel.Children.Clear();
        AddPropertyRow("Status", module.StatusSummary);
        AddPropertyRow("Enabled", module.IsEnabled ? "Yes" : "No");
        AddPropertyRow("Initialized", module.IsInitialized ? "Yes" : "No");
        AddPropertyRow("Monitoring", module.IsMonitoring ? "Yes" : "No");
        AddPropertyRow("Device Count", module.DeviceCount.ToString());
        AddPropertyRow("Healthy", module.HealthyCount.ToString());
        AddPropertyRow("Unhealthy", module.UnhealthyCount.ToString());
        AddPropertyRow("Offline", module.OfflineCount.ToString());
        AddPropertyRow("Last Update", module.LastUpdateDisplay);

        if (!string.IsNullOrEmpty(module.LastError))
        {
            AddPropertyRow("Last Error", module.LastError, isError: true);
        }

        // Populate device list
        foreach (var device in module.Devices)
        {
            AddPropertyRow($"  {device.DeviceName}", $"{device.Health} ({device.ResponseTimeDisplay})",
                isError: device.Health == OneRoomHealth.Hardware.Abstractions.DeviceHealth.Offline);
        }

        // Populate events
        DetailEventsPanel.Children.Clear();
        if (module.RecentEvents.Count == 0)
        {
            AddEventRow("No recent events", "", Colors.Gray);
        }
        else
        {
            foreach (var evt in module.RecentEvents)
            {
                AddEventRow(evt.TimestampDisplay, evt.ChangeDescription, GetHealthColor(evt.NewHealth));
            }
        }

        ModuleDetailPanel.Visibility = Visibility.Visible;
        Logger.Log($"Showing detail panel for module: {module.ModuleName}");
    }

    private void AddPropertyRow(string label, string value, bool isError = false)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 133, 133, 133)),
            FontSize = 11,
            FontFamily = new FontFamily("Cascadia Code, Consolas")
        };

        var valueBlock = new TextBlock
        {
            Text = value,
            Foreground = new SolidColorBrush(isError
                ? ColorHelper.FromArgb(255, 244, 135, 113)
                : ColorHelper.FromArgb(255, 204, 204, 204)),
            FontSize = 11,
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(valueBlock, 1);

        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
        DetailPropertiesPanel.Children.Add(grid);
    }

    private void AddEventRow(string time, string description, Windows.UI.Color color)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        stack.Children.Add(new TextBlock
        {
            Text = time,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 133, 133, 133)),
            FontSize = 11,
            FontFamily = new FontFamily("Cascadia Code, Consolas")
        });

        stack.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = new SolidColorBrush(color),
            FontSize = 11,
            FontFamily = new FontFamily("Cascadia Code, Consolas")
        });

        DetailEventsPanel.Children.Add(stack);
    }

    private static Windows.UI.Color GetHealthColor(ModuleHealthStatus status)
    {
        return status switch
        {
            ModuleHealthStatus.Healthy => ColorHelper.FromArgb(255, 78, 201, 176),
            ModuleHealthStatus.Degraded => ColorHelper.FromArgb(255, 220, 220, 170),
            ModuleHealthStatus.Unhealthy => ColorHelper.FromArgb(255, 244, 135, 113),
            ModuleHealthStatus.Offline => ColorHelper.FromArgb(255, 244, 135, 113),
            _ => ColorHelper.FromArgb(255, 133, 133, 133)
        };
    }

    private static Windows.UI.Color GetHealthColor(OneRoomHealth.Hardware.Abstractions.DeviceHealth health)
    {
        return health switch
        {
            OneRoomHealth.Hardware.Abstractions.DeviceHealth.Healthy => ColorHelper.FromArgb(255, 78, 201, 176),
            OneRoomHealth.Hardware.Abstractions.DeviceHealth.Unhealthy => ColorHelper.FromArgb(255, 220, 220, 170),
            OneRoomHealth.Hardware.Abstractions.DeviceHealth.Offline => ColorHelper.FromArgb(255, 244, 135, 113),
            _ => ColorHelper.FromArgb(255, 133, 133, 133)
        };
    }

    private async void DetailReconnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedModule == null) return;

        try
        {
            Logger.Log($"Attempting to refresh module: {_selectedModule.ModuleName}");
            ShowStatus("Refreshing", $"Checking {_selectedModule.DisplayName} devices...");

            // Get the hardware manager to access the module
            var manager = App.Services?.GetService(typeof(OneRoomHealth.Hardware.Services.HardwareManager))
                as OneRoomHealth.Hardware.Services.HardwareManager;

            if (manager != null)
            {
                var module = manager.GetModule<OneRoomHealth.Hardware.Abstractions.IHardwareModule>(_selectedModule.ModuleName);
                if (module != null)
                {
                    // Call GetDevicesAsync to trigger the module to refresh its device list
                    var devices = await module.GetDevicesAsync();
                    Logger.Log($"Module {_selectedModule.ModuleName} returned {devices.Count} devices");
                }
            }

            // Refresh the health visualization service to update the UI
            var healthService = App.HealthVisualization;
            if (healthService != null)
            {
                await healthService.RefreshAsync();
                Logger.Log($"Health visualization refreshed for {_selectedModule.ModuleName}");
            }

            // Refresh the health display
            RefreshHealthDisplay();

            // Re-show the detail panel with updated data if the module is still selected
            if (_selectedModule != null)
            {
                var updatedModule = healthService?.GetModuleHealth(_selectedModule.ModuleName);
                if (updatedModule != null)
                {
                    ShowModuleDetail(updatedModule);
                }
            }

            ShowStatus("Refresh Complete", $"{_selectedModule?.DisplayName ?? "Module"} status updated");
            _ = Task.Delay(2000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to refresh module: {ex.Message}");
            ShowStatus("Refresh Failed", ex.Message);
            _ = Task.Delay(2000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));
        }
    }

    private void CloseDetailButton_Click(object sender, RoutedEventArgs e)
    {
        ModuleDetailPanel.Visibility = Visibility.Collapsed;
        _selectedModule = null;
        Logger.Log("Module detail panel closed");
    }

    #endregion

    #region Video/Screensaver Mode

    /// <summary>
    /// Switches from screensaver mode to video mode or toggles videos if already in video mode.
    /// </summary>
    private async Task SwitchToVideoMode()
    {
        if (_isVideoMode)
        {
            // Already in video mode - toggle between videos
            Logger.Log("Already in video mode - toggling video source");
            if (_videoController != null)
            {
                await _videoController.HandleFlicButtonPressAsync();
            }
            return;
        }

        Logger.Log("========== SWITCHING TO VIDEO MODE ==========");
        _isVideoMode = true;

        try
        {
            // Hide the WebView
            await Task.Run(() =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (KioskWebView != null)
                    {
                        KioskWebView.Visibility = Visibility.Collapsed;
                        Logger.Log("WebView hidden for video mode");
                    }
                });
            });

            // Start playing the carescape video
            if (_videoController != null)
            {
                await _videoController.HandleFlicButtonPressAsync();
                Logger.Log("Video playback started");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error switching to video mode: {ex.Message}");
            _isVideoMode = false;
        }
    }

    /// <summary>
    /// Switches from video mode to screensaver mode.
    /// </summary>
    private async Task SwitchToScreensaverMode()
    {
        if (!_isVideoMode)
        {
            Logger.Log("Already in screensaver mode");
            return;
        }

        Logger.Log("========== SWITCHING TO SCREENSAVER MODE ==========");
        _isVideoMode = false;

        try
        {
            // Stop any playing video
            if (_videoController != null)
            {
                await _videoController.StopAsync();
                Logger.Log("Video stopped");
            }

            // Set presenter to Overlapped before configuring
            if (_appWindow != null)
            {
                if (_appWindow.Presenter.Kind != Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped)
                {
                    _appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped);
                    Logger.Log("Set presenter to Overlapped mode");
                }
                await Task.Delay(100);
            }

            // Configure the window
            ConfigureAsKioskWindow();
            Logger.Log("Window configured for fullscreen kiosk mode");

            // Reset WebView dimensions to auto/stretch
            if (_appWindow != null && KioskWebView != null)
            {
                KioskWebView.Width = double.NaN;
                KioskWebView.Height = double.NaN;
                KioskWebView.HorizontalAlignment = HorizontalAlignment.Stretch;
                KioskWebView.VerticalAlignment = VerticalAlignment.Stretch;
                Logger.Log("Set WebView sizing to Auto/Stretch");
            }

            // Show the WebView and navigate to screensaver URL
            await Task.Run(() =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (KioskWebView != null)
                    {
                        KioskWebView.Visibility = Visibility.Visible;
                        KioskWebView.UpdateLayout();
                        Logger.Log("WebView shown for screensaver mode");

                        // Navigate to the screensaver URL
                        if (!string.IsNullOrEmpty(_currentUrl))
                        {
                            KioskWebView.Source = new Uri(_currentUrl);
                            Logger.Log($"Navigating back to: {_currentUrl}");
                        }
                        else if (!string.IsNullOrEmpty(_config.Kiosk.DefaultUrl))
                        {
                            _currentUrl = _config.Kiosk.DefaultUrl;
                            KioskWebView.Source = new Uri(_currentUrl);
                            Logger.Log($"Navigating to default URL: {_currentUrl}");
                        }

                        // Re-run ConfigureAsKioskWindow after delay
                        _ = Task.Delay(200).ContinueWith(async _ =>
                        {
                            await DispatcherQueue.EnqueueAsync(() =>
                            {
                                ConfigureAsKioskWindow();
                                Logger.Log("Re-ran ConfigureAsKioskWindow after delay (screensaver transition)");
                                if (KioskWebView != null)
                                {
                                    KioskWebView.UpdateLayout();
                                }
                            });
                        });

                        // Force a resize event
                        _ = Task.Delay(500).ContinueWith(_ =>
                        {
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                if (KioskWebView?.CoreWebView2 != null)
                                {
                                    _ = KioskWebView.CoreWebView2.ExecuteScriptAsync("window.dispatchEvent(new Event('resize'));").AsTask();
                                    Logger.Log("Triggered resize event in screensaver content");
                                }
                            });
                        });
                    }
                });
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"Error switching to screensaver mode: {ex.Message}");
        }
    }

    #endregion

    #region Exit Handling

    /// <summary>
    /// Handles the exit request with password protection.
    /// </summary>
    private async Task HandleExitRequest()
    {
        Logger.LogSecurityEvent("ExitRequested", "User initiated exit request");

        var dialog = new ContentDialog
        {
            Title = "Exit Kiosk Mode",
            Content = new PasswordBox
            {
                PlaceholderText = "Enter exit password",
                Width = 300
            },
            PrimaryButtonText = "Exit",
            CloseButtonText = "Cancel",
            XamlRoot = this.Content.XamlRoot,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var passwordBox = (PasswordBox)dialog.Content;
            var enteredPassword = passwordBox.Password;

            bool isValid = false;

            // Support both plain text passwords and SHA256 hashes for backward compatibility
            if (string.IsNullOrEmpty(_config.Exit.PasswordHash))
            {
                // Default to admin123 if no password is set
                Logger.Log("Password hash is empty, using default password");
                isValid = enteredPassword == "admin123";
            }
            else if (_config.Exit.PasswordHash.Length < 64)  // SHA256 hashes are 64 hex characters
            {
                // Treat as plain text password
                Logger.Log("Treating password as plain text");
                isValid = enteredPassword == _config.Exit.PasswordHash;
            }
            else
            {
                // Treat as SHA256 hash
                Logger.Log("Treating password as SHA256 hash");
                isValid = SecurityHelper.ValidatePassword(enteredPassword, _config.Exit.PasswordHash);
            }

            if (isValid)
            {
                Logger.LogSecurityEvent("ExitAuthorized", "Correct password provided, exiting application");
                await CleanupAndExit();
            }
            else
            {
                Logger.LogSecurityEvent("ExitDenied", "Incorrect password provided");
                ShowStatus("Access Denied", "Incorrect password");
                await Task.Delay(2000);
                HideStatus();
            }
        }
    }

    /// <summary>
    /// Performs cleanup and exits the application.
    /// </summary>
    private async Task CleanupAndExit()
    {
        try
        {
            Logger.Log("========== APPLICATION EXIT START ==========");

            // Stop debug mode timers
            StopDebugModeRefreshTimer();
            _healthRefreshTimer?.Dispose();

            // Clean up ACS call and media tracks before closing WebView
            if (KioskWebView?.CoreWebView2 != null)
            {
                try
                {
                    // First, try to hang up any active ACS call
                    Logger.Log("Attempting to hang up ACS call...");
                    await KioskWebView.CoreWebView2.ExecuteScriptAsync(@"
                        (async () => {
                            try {
                                if (window.call && typeof window.call.hangUp === 'function') {
                                    console.log('[ORH EXIT] Hanging up ACS call...');
                                    await window.call.hangUp();
                                    console.log('[ORH EXIT] ACS call hung up successfully');
                                } else if (window.currentCall && typeof window.currentCall.hangUp === 'function') {
                                    console.log('[ORH EXIT] Hanging up currentCall...');
                                    await window.currentCall.hangUp();
                                    console.log('[ORH EXIT] currentCall hung up successfully');
                                } else {
                                    console.log('[ORH EXIT] No ACS call object found to hang up');
                                }
                            } catch (e) {
                                console.log('[ORH EXIT] Error hanging up ACS call: ' + e);
                            }
                        })();
                    ");
                    Logger.Log("ACS hangup attempted");
                    await Task.Delay(300);

                    // Stop local media tracks
                    Logger.Log("Stopping local media tracks...");
                    await KioskWebView.CoreWebView2.ExecuteScriptAsync(@"
                        (() => {
                            try {
                                if (typeof window.__orhStopLocalTracks === 'function') {
                                    const stopped = window.__orhStopLocalTracks();
                                    console.log('[ORH EXIT] Stopped ' + stopped + ' local media track(s)');
                                }
                                if (window.__orhPeerConnections) {
                                    window.__orhPeerConnections.forEach(pc => {
                                        try {
                                            if (pc && pc.getSenders) {
                                                pc.getSenders().forEach(s => {
                                                    try {
                                                        if (s && s.track) s.track.stop();
                                                    } catch (e) {}
                                                });
                                            }
                                        } catch (e) {}
                                    });
                                    console.log('[ORH EXIT] Stopped tracks from peer connections');
                                }
                                document.querySelectorAll('video').forEach(v => {
                                    try {
                                        if (v.srcObject && v.srcObject.getTracks) {
                                            v.srcObject.getTracks().forEach(t => t.stop());
                                            console.log('[ORH EXIT] Stopped tracks from video element');
                                        }
                                        v.srcObject = null;
                                    } catch (e) {}
                                });
                            } catch (e) {
                                console.log('[ORH EXIT] Error stopping tracks: ' + e);
                            }
                        })();
                    ");
                    Logger.Log("Local media tracks stopped");

                    // Navigate to screensaver URL to destroy the ACS page context
                    var screensaverUrl = _config.Kiosk.DefaultUrl;
                    Logger.Log($"Navigating to screensaver to destroy ACS context: {screensaverUrl}");
                    KioskWebView.Source = new Uri(screensaverUrl);
                    await Task.Delay(500);
                    Logger.Log("ACS context destroyed, media resources released");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to cleanup media (non-fatal): {ex.Message}");
                }
            }

            // Stop media preference sync timer
            if (_mediaPreferenceSyncTimer != null)
            {
                Logger.Log("Stopping media preference sync timer...");
                _mediaPreferenceSyncTimer.Tick -= MediaPreferenceSyncTimer_Tick;
                _mediaPreferenceSyncTimer.Stop();
                _mediaPreferenceSyncTimer = null;
                Logger.Log("Media preference sync timer stopped");
            }

            // Stop video controller if active
            if (_videoController != null)
            {
                Logger.Log("Stopping video controller...");
                await _videoController.StopAsync();
                _videoController.Dispose();
                Logger.Log("Video controller stopped");
            }

            // Unhook keyboard hook
            RemoveKeyboardHook();

            Logger.Log("Closing application window...");

            // Use dispatcher to ensure we're on the UI thread
            await Task.Run(() =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    this.Close();
                    Logger.Log("Exiting application process...");
                    Environment.Exit(0);
                });
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"Error during cleanup: {ex.Message}");
            Environment.Exit(1);
        }
    }

    #endregion
}
