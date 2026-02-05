using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using KioskApp.Helpers;

namespace KioskApp;

/// <summary>
/// MainWindow partial class - Navigation handlers, hardware health panel, and log viewer.
/// </summary>
public sealed partial class MainWindow
{
    // Suppress module toggle event when programmatically changing the toggle
    private bool _suppressModuleToggleEvent = false;

    #region Navigation Handlers

    /// <summary>
    /// Gets the current URL being displayed in the WebView.
    /// Thread-safe property that can be accessed from any thread.
    /// </summary>
    public string? CurrentUrl => _currentUrl;

    /// <summary>
    /// Navigates to a URL asynchronously. Safe to call from any thread.
    /// Returns true if navigation was initiated successfully, false if WebView2
    /// is not initialized or if the dispatcher queue is unavailable.
    /// </summary>
    public Task<bool> NavigateToUrlAsync(string url)
    {
        var tcs = new TaskCompletionSource<bool>();

        try
        {
            bool enqueued = DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    bool success = NavigateToUrl(url);
                    tcs.SetResult(success);
                }
                catch (Exception ex)
                {
                    Logger.Log($"NavigateToUrlAsync failed: {ex.Message}");
                    tcs.SetResult(false);
                }
            });

            // If enqueueing failed (e.g., during shutdown), complete the task immediately
            if (!enqueued)
            {
                Logger.Log($"NavigateToUrlAsync: Failed to enqueue navigation to {url} - dispatcher queue unavailable");
                tcs.SetResult(false);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"NavigateToUrlAsync dispatch failed: {ex.Message}");
            tcs.SetResult(false);
        }

        return tcs.Task;
    }

    /// <summary>
    /// Handles URL textbox Enter key press.
    /// </summary>
    private void UrlTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            GoButton_Click(sender, null!);
        }
    }

    /// <summary>
    /// Navigates to the URL in the textbox.
    /// </summary>
    private void GoButton_Click(object sender, RoutedEventArgs e)
    {
        if (KioskWebView?.CoreWebView2 == null)
        {
            Logger.Log("Cannot navigate: WebView2 not initialized");
            ShowStatus("Error", "WebView2 is not ready. Please wait for initialization.");
            _ = Task.Delay(2000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));
            return;
        }

        if (!string.IsNullOrWhiteSpace(UrlTextBox?.Text))
        {
            var url = UrlTextBox.Text;
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                url = "https://" + url;
            }
            NavigateToUrl(url);
        }
    }

    /// <summary>
    /// Navigates to a URL.
    /// </summary>
    /// <returns>True if navigation was initiated, false if WebView2 is not initialized or an error occurred.</returns>
    public bool NavigateToUrl(string url)
    {
        try
        {
            if (KioskWebView?.CoreWebView2 == null)
            {
                Logger.Log($"Cannot navigate to {url}: WebView2 not initialized");
                return false;
            }

            _currentUrl = url;
            KioskWebView.Source = new Uri(url);
            Logger.Log($"Navigating to: {url}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to navigate to {url}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Navigate back.
    /// </summary>
    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (KioskWebView?.CoreWebView2 != null && KioskWebView.CanGoBack == true)
        {
            KioskWebView.GoBack();
        }
        else
        {
            Logger.Log("Cannot go back: WebView2 not initialized or no history");
        }
    }

    /// <summary>
    /// Navigate forward.
    /// </summary>
    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (KioskWebView?.CoreWebView2 != null && KioskWebView.CanGoForward == true)
        {
            KioskWebView.GoForward();
        }
        else
        {
            Logger.Log("Cannot go forward: WebView2 not initialized or no history");
        }
    }

    /// <summary>
    /// Refresh the current page.
    /// </summary>
    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (KioskWebView?.CoreWebView2 != null)
        {
            var currentSource = KioskWebView.Source?.ToString();

            // If source is about:blank or invalid, navigate to stored URL instead
            if (string.IsNullOrEmpty(currentSource) ||
                currentSource.Equals("about:blank", StringComparison.OrdinalIgnoreCase) ||
                !Uri.TryCreate(currentSource, UriKind.Absolute, out _))
            {
                if (!string.IsNullOrEmpty(_currentUrl))
                {
                    Logger.Log($"Reload: Current source is invalid ({currentSource}), navigating to stored URL: {_currentUrl}");
                    NavigateToUrl(_currentUrl);
                }
                else
                {
                    var defaultUrl = _config.Kiosk.DefaultUrl;
                    Logger.Log($"Reload: No valid URL, navigating to default: {defaultUrl}");
                    NavigateToUrl(defaultUrl);
                }
            }
            else
            {
                KioskWebView.Reload();
                Logger.Log($"Reloading current page: {currentSource}");
            }
        }
        else
        {
            Logger.Log("Cannot reload: WebView2 not initialized");
            ShowStatus("Error", "WebView2 is not ready. Please wait for initialization.");
            _ = Task.Delay(2000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));
        }
    }

    /// <summary>
    /// Open developer tools.
    /// </summary>
    private void DevToolsButton_Click(object sender, RoutedEventArgs e)
    {
        if (KioskWebView?.CoreWebView2 != null)
        {
            KioskWebView.CoreWebView2.OpenDevToolsWindow();
        }
        else
        {
            Logger.Log("Cannot open DevTools: WebView2 not initialized");
            ShowStatus("Error", "WebView2 is not ready. Please wait for initialization.");
            _ = Task.Delay(2000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));
        }
    }

    #endregion

    #region Hardware Health Panel

    private bool _healthPanelVisible = false;
    private Timer? _healthRefreshTimer;

    // Note: ToggleHealthPanel removed - now using tabbed interface in Debug.cs
    // The tab switching logic handles visibility and timer management

    private void RefreshHealthButton_Click(object sender, RoutedEventArgs e)
    {
        _ = RefreshHealthAsync();
    }

    private async Task RefreshHealthAsync()
    {
        var service = App.HealthVisualization;
        if (service == null)
        {
            HealthFooterText.Text = "Health visualization service not available";
            return;
        }

        await service.RefreshAsync();
        RefreshHealthDisplay();
    }

    private void RefreshHealthDisplay()
    {
        var service = App.HealthVisualization;
        if (service == null)
        {
            HealthFooterText.Text = "Health visualization service not available";
            return;
        }

        var modules = service.GetModuleHealthSummaries();
        var summary = service.SystemSummary;

        // Update footer with summary info
        HealthFooterText.Text = $"API: http://localhost:8081 | {summary.ActiveModules}/{summary.TotalModules} modules | Uptime: {summary.UptimeDisplay}";
        HealthLastRefreshText.Text = $"Last refresh: {summary.LastUpdate.ToLocalTime():HH:mm:ss}";

        // Build module cards using the new ItemsControl
        var cardList = new System.Collections.Generic.List<UIElement>();
        foreach (var module in modules)
        {
            var card = CreateModuleHealthCard(module);
            cardList.Add(card);
        }

        // Update the ItemsControl
        ModuleHealthItemsControl.ItemsSource = null;
        ModuleHealthItemsControl.Items.Clear();
        foreach (var card in cardList)
        {
            ModuleHealthItemsControl.Items.Add(card);
        }
    }

    private Border CreateModuleHealthCard(OneRoomHealth.Hardware.ViewModels.ModuleHealthViewModel module)
    {
        // New VS Code style colors
        var healthBorderColor = module.OverallHealth switch
        {
            OneRoomHealth.Hardware.ViewModels.ModuleHealthStatus.Healthy => Windows.UI.Color.FromArgb(255, 78, 201, 176),   // #4EC9B0 teal
            OneRoomHealth.Hardware.ViewModels.ModuleHealthStatus.Degraded => Windows.UI.Color.FromArgb(255, 220, 220, 170), // #DCDCAA yellow
            OneRoomHealth.Hardware.ViewModels.ModuleHealthStatus.Unhealthy => Windows.UI.Color.FromArgb(255, 244, 135, 113), // #F48771 coral
            OneRoomHealth.Hardware.ViewModels.ModuleHealthStatus.Offline => Windows.UI.Color.FromArgb(255, 244, 135, 113),
            OneRoomHealth.Hardware.ViewModels.ModuleHealthStatus.Disabled => Windows.UI.Color.FromArgb(255, 60, 60, 60),
            _ => Windows.UI.Color.FromArgb(255, 60, 60, 60)
        };

        var card = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 37, 38)), // #252526
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            MinWidth = 200,
            MinHeight = 120,
            BorderBrush = new SolidColorBrush(healthBorderColor),
            BorderThickness = new Thickness(1, 1, 1, 3), // Thicker bottom border for status indication
            Tag = module // Store module reference for click handler
        };

        // Make card clickable
        card.PointerPressed += (s, e) =>
        {
            if (s is Border b && b.Tag is OneRoomHealth.Hardware.ViewModels.ModuleHealthViewModel m)
            {
                ShowModuleDetail(m);
            }
        };

        // Hover effect
        card.PointerEntered += (s, e) =>
        {
            if (s is Border b)
            {
                b.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 45, 45)); // #2D2D2D
            }
        };
        card.PointerExited += (s, e) =>
        {
            if (s is Border b)
            {
                b.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 37, 38)); // #252526
            }
        };

        var stack = new StackPanel();

        // Header row with module icon and name
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Module type icon
        var moduleIcon = new TextBlock
        {
            Text = GetModuleIcon(module.ModuleName),
            FontSize = 16,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 133, 133, 133)), // #858585
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(moduleIcon, 0);

        var nameText = new TextBlock
        {
            Text = module.DisplayName,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 204, 204, 204)), // #CCCCCC
            FontSize = 13,
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(nameText, 1);

        // Health indicator
        var healthColor = module.OverallHealth switch
        {
            OneRoomHealth.Hardware.ViewModels.ModuleHealthStatus.Healthy => Windows.UI.Color.FromArgb(255, 78, 201, 176),
            OneRoomHealth.Hardware.ViewModels.ModuleHealthStatus.Degraded => Windows.UI.Color.FromArgb(255, 220, 220, 170),
            OneRoomHealth.Hardware.ViewModels.ModuleHealthStatus.Unhealthy => Windows.UI.Color.FromArgb(255, 244, 135, 113),
            OneRoomHealth.Hardware.ViewModels.ModuleHealthStatus.Offline => Windows.UI.Color.FromArgb(255, 244, 135, 113),
            OneRoomHealth.Hardware.ViewModels.ModuleHealthStatus.Disabled => Windows.UI.Color.FromArgb(255, 133, 133, 133),
            _ => Windows.UI.Color.FromArgb(255, 133, 133, 133)
        };

        var healthIndicator = new TextBlock
        {
            Text = module.HealthIcon,
            FontSize = 14,
            Foreground = new SolidColorBrush(healthColor),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(healthIndicator, 2);

        headerGrid.Children.Add(moduleIcon);
        headerGrid.Children.Add(nameText);
        headerGrid.Children.Add(healthIndicator);
        stack.Children.Add(headerGrid);

        // Status summary
        var statusText = new TextBlock
        {
            Text = module.StatusSummary,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 133, 133, 133)), // #858585
            FontSize = 11,
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            Margin = new Thickness(0, 6, 0, 0)
        };
        stack.Children.Add(statusText);

        // Device count summary
        if (module.DeviceCount > 0)
        {
            var deviceSummary = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), Spacing = 12 };

            if (module.HealthyCount > 0)
            {
                var healthyBadge = CreateDeviceCountBadge(module.HealthyCount, "healthy", Windows.UI.Color.FromArgb(255, 78, 201, 176));
                deviceSummary.Children.Add(healthyBadge);
            }
            if (module.UnhealthyCount > 0)
            {
                var unhealthyBadge = CreateDeviceCountBadge(module.UnhealthyCount, "unhealthy", Windows.UI.Color.FromArgb(255, 220, 220, 170));
                deviceSummary.Children.Add(unhealthyBadge);
            }
            if (module.OfflineCount > 0)
            {
                var offlineBadge = CreateDeviceCountBadge(module.OfflineCount, "offline", Windows.UI.Color.FromArgb(255, 244, 135, 113));
                deviceSummary.Children.Add(offlineBadge);
            }

            if (deviceSummary.Children.Count > 0)
            {
                stack.Children.Add(deviceSummary);
            }
        }

        // Last update
        var updateText = new TextBlock
        {
            Text = $"Updated: {module.LastUpdateDisplay}",
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 102, 102, 102)), // #666666
            FontSize = 10,
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            Margin = new Thickness(0, 8, 0, 0)
        };
        stack.Children.Add(updateText);

        // Error message if any (truncated)
        if (!string.IsNullOrEmpty(module.LastError))
        {
            var errorText = new TextBlock
            {
                Text = module.LastError.Length > 50 ? module.LastError.Substring(0, 47) + "..." : module.LastError,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 244, 135, 113)), // #F48771
                FontSize = 10,
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 4, 0, 0)
            };
            stack.Children.Add(errorText);
        }

        // "Click to expand" hint
        var hintText = new TextBlock
        {
            Text = "Click for details \u2192",
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 86, 156, 214)), // #569CD6 accent blue
            FontSize = 10,
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            Margin = new Thickness(0, 8, 0, 0),
            Opacity = 0.7
        };
        stack.Children.Add(hintText);

        card.Child = stack;
        return card;
    }

    private static Border CreateDeviceCountBadge(int count, string label, Windows.UI.Color color)
    {
        var badge = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(40, color.R, color.G, color.B)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 2)
        };

        var text = new TextBlock
        {
            Text = $"{count} {label}",
            Foreground = new SolidColorBrush(color),
            FontSize = 10,
            FontFamily = new FontFamily("Cascadia Code, Consolas")
        };

        badge.Child = text;
        return badge;
    }

    private static string GetModuleIcon(string moduleName)
    {
        return moduleName.ToLowerInvariant() switch
        {
            "display" => "\U0001F4FA",      // TV
            "camera" => "\U0001F4F7",       // Camera
            "lighting" => "\U0001F4A1",     // Light bulb
            "microphone" => "\U0001F3A4",   // Microphone
            "speaker" => "\U0001F50A",      // Speaker
            "systemaudio" => "\U0001F3B5",  // Musical note
            "biamp" => "\U0001F399",        // Studio microphone (video conferencing codec)
            _ => "\U0001F50C"               // Electric plug (default)
        };
    }

    // Note: CloseHealthButton_Click removed - tabs are managed in Debug.cs

    /// <summary>
    /// Handles individual module toggle switches.
    /// Enables/disables hardware modules dynamically.
    /// </summary>
    private async void ModuleToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle) return;

        // Skip if we're programmatically setting the toggle
        if (_suppressModuleToggleEvent)
        {
            return;
        }

        var moduleName = toggle.Tag?.ToString() ?? "";
        var isEnabled = toggle.IsOn;

        // Check if we're in Hardware API mode
        if (!App.IsHardwareApiMode)
        {
            Logger.Log($"Module toggle ignored for {moduleName} - not in Hardware API mode");
            // Reset the toggle to OFF since modules can't be enabled in Navigate mode
            _suppressModuleToggleEvent = true;
            try
            {
                toggle.IsOn = false;
            }
            finally
            {
                _suppressModuleToggleEvent = false;
            }
            ShowStatus("Not Available", "Module toggles only work in Hardware API mode");
            _ = Task.Delay(2000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));
            return;
        }

        Logger.Log($"Module toggle: {moduleName} -> {(isEnabled ? "ON" : "OFF")}");
        ShowStatus(isEnabled ? "Enabling" : "Disabling", $"{moduleName} module...");

        try
        {
            var hardwareManager = App.Services?.GetService(typeof(OneRoomHealth.Hardware.Services.HardwareManager))
                as OneRoomHealth.Hardware.Services.HardwareManager;

            if (hardwareManager == null)
            {
                Logger.Log("HardwareManager not available");
                ShowStatus("Error", "Hardware manager not available");
                _ = Task.Delay(2000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));
                return;
            }

            if (isEnabled)
            {
                // Get the module from DI
                var module = moduleName switch
                {
                    "Display" => App.Services?.GetService(typeof(OneRoomHealth.Hardware.Modules.Display.DisplayModule)) as OneRoomHealth.Hardware.Abstractions.IHardwareModule,
                    "Camera" => App.Services?.GetService(typeof(OneRoomHealth.Hardware.Modules.Camera.CameraModule)) as OneRoomHealth.Hardware.Abstractions.IHardwareModule,
                    "Lighting" => App.Services?.GetService(typeof(OneRoomHealth.Hardware.Modules.Lighting.LightingModule)) as OneRoomHealth.Hardware.Abstractions.IHardwareModule,
                    "SystemAudio" => App.Services?.GetService(typeof(OneRoomHealth.Hardware.Modules.SystemAudio.SystemAudioModule)) as OneRoomHealth.Hardware.Abstractions.IHardwareModule,
                    "Microphone" => App.Services?.GetService(typeof(OneRoomHealth.Hardware.Modules.Microphone.MicrophoneModule)) as OneRoomHealth.Hardware.Abstractions.IHardwareModule,
                    "Speaker" => App.Services?.GetService(typeof(OneRoomHealth.Hardware.Modules.Speaker.SpeakerModule)) as OneRoomHealth.Hardware.Abstractions.IHardwareModule,
                    "Biamp" => App.Services?.GetService(typeof(OneRoomHealth.Hardware.Modules.Biamp.BiampModule)) as OneRoomHealth.Hardware.Abstractions.IHardwareModule,
                    _ => null
                };

                if (module != null)
                {
                    // Register, initialize, and start monitoring
                    hardwareManager.RegisterModule(module);
                    var initSuccess = await module.InitializeAsync();
                    if (initSuccess)
                    {
                        await module.StartMonitoringAsync();
                        Logger.Log($"{moduleName} module initialized and monitoring started");
                        ShowStatus("Enabled", $"{moduleName} module is now active");
                    }
                    else
                    {
                        Logger.Log($"{moduleName} module initialization returned false");
                        ShowStatus("Warning", $"{moduleName} module initialization incomplete");
                    }
                }
                else
                {
                    Logger.Log($"Module {moduleName} not found in DI container");
                    ShowStatus("Error", $"Module {moduleName} not found");
                }
            }
            else
            {
                // Shutdown the specific module (stops monitoring and removes from manager)
                await hardwareManager.ShutdownModuleAsync(moduleName);
                Logger.Log($"{moduleName} module shut down");
                ShowStatus("Disabled", $"{moduleName} module stopped");
            }

            // Refresh the health display
            await RefreshHealthAsync();
            _ = Task.Delay(1500).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));
        }
        catch (Exception ex)
        {
            Logger.Log($"Error toggling {moduleName} module: {ex.Message}");
            ShowStatus("Error", $"Failed to toggle {moduleName}: {ex.Message}");
            _ = Task.Delay(3000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));
        }
    }

    #endregion

    #region Log Viewer

    private bool _logsVisible = false;
    private LogLevel _currentLogLevel = LogLevel.Debug;
    private string? _currentModuleFilter = null;

    // Note: ToggleLogViewer removed - now using tabbed interface in Debug.cs

    private void InitializeLogFilters()
    {
        // Initialize log level filter if not already done
        if (LogLevelSelector != null && LogLevelSelector.Items.Count == 0)
        {
            LogLevelSelector.Items.Add(new ComboBoxItem { Content = "Debug", Tag = LogLevel.Debug });
            LogLevelSelector.Items.Add(new ComboBoxItem { Content = "Info", Tag = LogLevel.Info });
            LogLevelSelector.Items.Add(new ComboBoxItem { Content = "Warning", Tag = LogLevel.Warning });
            LogLevelSelector.Items.Add(new ComboBoxItem { Content = "Error", Tag = LogLevel.Error });
            LogLevelSelector.SelectedIndex = 0; // Debug by default
        }

        // Initialize module filter if not already done
        if (ModuleFilterSelector != null && ModuleFilterSelector.Items.Count == 0)
        {
            ModuleFilterSelector.Items.Add(new ComboBoxItem { Content = "All Modules", Tag = (string?)null });
            foreach (var module in UnifiedLogger.KnownModules)
            {
                ModuleFilterSelector.Items.Add(new ComboBoxItem { Content = module, Tag = module });
            }
            ModuleFilterSelector.SelectedIndex = 0; // All modules by default
        }
    }

    private void LogLevelSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LogLevelSelector?.SelectedItem is ComboBoxItem item && item.Tag is LogLevel level)
        {
            _currentLogLevel = level;
            UnifiedLogger.Instance.SetMinimumLevel(level);
            RefreshLogDisplay();
        }
    }

    private void ModuleFilterSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModuleFilterSelector?.SelectedItem is ComboBoxItem item)
        {
            _currentModuleFilter = item.Tag as string;

            if (_currentModuleFilter == null)
            {
                UnifiedLogger.Instance.EnableAllModules();
            }
            else
            {
                UnifiedLogger.Instance.EnableAllModules();
                // Filter to just this module by checking in ShouldShow
            }
            RefreshLogDisplay();
        }
    }

    private void RefreshLogDisplay()
    {
        try
        {
            var allLogs = UnifiedLogger.Instance.GetAllLogs(1000);
            var filteredLogs = allLogs
                .Where(l => l.Level >= _currentLogLevel)
                .Where(l => _currentModuleFilter == null || l.Module == _currentModuleFilter)
                .TakeLast(500)
                .ToList();

            var sb = new StringBuilder();
            foreach (var log in filteredLogs)
            {
                sb.AppendLine(log.FormattedMessage);
                if (!string.IsNullOrEmpty(log.Exception))
                {
                    sb.AppendLine($"    Exception: {log.Exception}");
                }
            }

            LogContentTextBlock.Text = sb.ToString();

            // Update statistics
            var stats = UnifiedLogger.Instance.GetStats();
            LogCountTextBlock.Text = $"Showing {filteredLogs.Count} of {stats.Total} | D:{stats.Debug} I:{stats.Info} W:{stats.Warning} E:{stats.Error}";

            // Auto-scroll to bottom
            if (LogsTabContent.Visibility == Visibility.Visible)
            {
                DispatcherQueue.TryEnqueue(ScrollToBottom);
            }
        }
        catch (Exception ex)
        {
            LogContentTextBlock.Text = $"Error loading logs: {ex.Message}";
            Logger.Log($"Error in RefreshLogDisplay: {ex.Message}");
        }
    }

    private void ScrollToBottom()
    {
        try
        {
            if (LogScrollViewer != null)
            {
                LogScrollViewer.ChangeView(null, double.MaxValue, null);
            }
        }
        catch { }
    }

    private void OnLogAdded(string logEntry)
    {
        // Legacy handler for original Logger - kept for compatibility
        // The unified logger will also receive this via its subscription
    }

    private void OnUnifiedLogAdded(UnifiedLogEntry entry)
    {
        // Update log display in real-time if viewer is visible and entry passes filters
        if (!_logsVisible || LogsTabContent.Visibility != Visibility.Visible)
            return;

        if (entry.Level < _currentLogLevel)
            return;

        if (_currentModuleFilter != null && entry.Module != _currentModuleFilter)
            return;

        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                // Append new log entry
                if (!string.IsNullOrEmpty(LogContentTextBlock.Text))
                {
                    LogContentTextBlock.Text += Environment.NewLine;
                }
                LogContentTextBlock.Text += entry.FormattedMessage;

                if (!string.IsNullOrEmpty(entry.Exception))
                {
                    LogContentTextBlock.Text += Environment.NewLine + $"    Exception: {entry.Exception}";
                }

                // Update statistics
                var stats = UnifiedLogger.Instance.GetStats();
                var displayedLines = LogContentTextBlock.Text.Split(Environment.NewLine).Length;
                LogCountTextBlock.Text = $"Showing ~{displayedLines} of {stats.Total} | D:{stats.Debug} I:{stats.Info} W:{stats.Warning} E:{stats.Error}";

                // Auto-scroll to bottom
                ScrollToBottom();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error in OnUnifiedLogAdded: {ex.Message}");
            }
        });
    }

    private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
    {
        UnifiedLogger.Instance.Clear();
        LogContentTextBlock.Text = "";
        LogCountTextBlock.Text = "0 log entries";
        Logger.Log("Log viewer cleared by user");
    }

    // Note: CloseLogsButton_Click removed - tabs are managed in Debug.cs

    private void CopyLogsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = LogContentTextBlock?.Text ?? "";

            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            Clipboard.Flush();

            Logger.Log($"Log viewer copied to clipboard ({text.Length} chars)");
            ShowStatus("Copied", $"Copied {text.Length} characters");
            _ = Task.Delay(1200).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to copy logs to clipboard: {ex.Message}");
            ShowStatus("Copy Failed", ex.Message);
            _ = Task.Delay(2000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));
        }
    }

    #endregion

    #region Performance/GC Panel

    private bool _perfPanelVisible = false;

    // Note: TogglePerformancePanel removed - now using tabbed interface in Debug.cs

    private void OnPerformanceSnapshot(PerformanceSnapshot snapshot)
    {
        if (!_perfPanelVisible || PerfTabContent.Visibility != Visibility.Visible)
            return;

        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                UpdatePerformanceUI(snapshot);
            }
            catch (Exception ex)
            {
                Logger.Log($"Error updating performance UI: {ex.Message}");
            }
        });
    }

    private void RefreshPerformanceDisplay()
    {
        var snapshot = PerformanceMonitor.Instance.GetLatestSnapshot();
        if (snapshot != null)
        {
            UpdatePerformanceUI(snapshot);
        }
    }

    private void UpdatePerformanceUI(PerformanceSnapshot snapshot)
    {
        // Uptime
        PerfUptimeText.Text = $"Uptime: {PerformanceMonitor.Instance.GetUptimeFormatted()}";

        // Memory stats
        PerfWorkingSet.Text = snapshot.WorkingSetMB;
        PerfGcMemory.Text = snapshot.GcTotalMemoryMB;

        // Memory bar (visual indicator - percentage of 1GB max assumed)
        var memoryPercent = Math.Min(100, (snapshot.WorkingSetBytes / (1024.0 * 1024.0 * 1024.0)) * 100);
        PerfMemoryBar.Width = PerfMemoryBar.Parent is Grid parent ? (parent.ActualWidth * memoryPercent / 100) : 0;

        // Memory pressure with color
        var pressure = PerformanceMonitor.Instance.GetMemoryPressureStatus();
        PerfMemoryPressure.Text = pressure;
        PerfMemoryPressure.Foreground = pressure switch
        {
            "Critical" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 244, 135, 113)), // #F48771
            "High" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 220, 170)),     // #DCDCAA
            "Moderate" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 220, 170)), // #DCDCAA
            _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 78, 201, 176))            // #4EC9B0 (Healthy teal)
        };

        // GC collections
        PerfGen0.Text = snapshot.Gen0Collections.ToString();
        PerfGen1.Text = snapshot.Gen1Collections.ToString();
        PerfGen2.Text = snapshot.Gen2Collections.ToString();

        // Process stats
        PerfCpuUsage.Text = $"{snapshot.CpuUsagePercent:F1}%";
        PerfThreadCount.Text = snapshot.ThreadCount.ToString();
        PerfHandleCount.Text = snapshot.HandleCount.ToString();
    }

    private void ForceGCButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (before, after, freed) = PerformanceMonitor.Instance.ForceGarbageCollection();
            PerfLastGcResult.Text = $"Freed {freed / 1024.0 / 1024.0:F2} MB\n" +
                                    $"Before: {before / 1024.0 / 1024.0:F2} MB\n" +
                                    $"After: {after / 1024.0 / 1024.0:F2} MB";
            PerfLastGcResult.Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 76, 175, 80));

            // Refresh display immediately after GC
            RefreshPerformanceDisplay();
        }
        catch (Exception ex)
        {
            PerfLastGcResult.Text = $"GC failed: {ex.Message}";
            PerfLastGcResult.Foreground = new SolidColorBrush(Colors.Red);
            Logger.Log($"Force GC failed: {ex.Message}");
        }
    }

    // Note: ClosePerfButton_Click removed - tabs are managed in Debug.cs

    #endregion
}
