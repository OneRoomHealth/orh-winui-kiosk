using System;
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

namespace KioskApp;

/// <summary>
/// MainWindow partial class - Navigation handlers, hardware health panel, and log viewer.
/// </summary>
public sealed partial class MainWindow
{
    #region Navigation Handlers

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
    public void NavigateToUrl(string url)
    {
        try
        {
            if (KioskWebView?.CoreWebView2 == null)
            {
                Logger.Log($"Cannot navigate to {url}: WebView2 not initialized");
                return;
            }

            _currentUrl = url;
            KioskWebView.Source = new Uri(url);
            Logger.Log($"Navigating to: {url}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to navigate to {url}: {ex.Message}");
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

    private void HardwareHealthButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleHealthPanel();
    }

    private void ToggleHealthPanel()
    {
        _healthPanelVisible = !_healthPanelVisible;

        if (_healthPanelVisible)
        {
            // Hide log viewer if visible
            if (_logsVisible)
            {
                LogViewerPanel.Visibility = Visibility.Collapsed;
                _logsVisible = false;
            }

            HardwareHealthPanel.Visibility = Visibility.Visible;
            RefreshHealthDisplay();

            // Start refresh timer (every 2 seconds)
            _healthRefreshTimer = new Timer(
                _ => DispatcherQueue.TryEnqueue(() => RefreshHealthDisplay()),
                null,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(2));

            Logger.Log("Hardware health panel shown");
        }
        else
        {
            HardwareHealthPanel.Visibility = Visibility.Collapsed;
            _healthRefreshTimer?.Dispose();
            _healthRefreshTimer = null;
            Logger.Log("Hardware health panel hidden");
        }
    }

    private void RefreshHealthButton_Click(object sender, RoutedEventArgs e)
    {
        _ = RefreshHealthAsync();
    }

    private async Task RefreshHealthAsync()
    {
        var service = App.HealthVisualization;
        if (service == null)
        {
            HealthSummaryText.Text = "Service not available";
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
            HealthSummaryText.Text = "Service not available";
            return;
        }

        var modules = service.GetModuleHealthSummaries();
        var summary = service.SystemSummary;

        // Update summary text
        HealthSummaryText.Text = $"{summary.ActiveModules}/{summary.TotalModules} modules active | {summary.HealthyDevices}/{summary.TotalDevices} devices healthy";

        // Update footer
        HealthFooterText.Text = $"API: http://localhost:8081 | Uptime: {summary.UptimeDisplay}";
        HealthLastRefreshText.Text = $"Last refresh: {summary.LastUpdate.ToLocalTime():HH:mm:ss}";

        // Build module cards
        ModuleHealthCards.Children.Clear();
        foreach (var module in modules)
        {
            var card = CreateModuleHealthCard(module);
            ModuleHealthCards.Children.Add(card);
        }
    }

    private Border CreateModuleHealthCard(OneRoomHealth.Hardware.ViewModels.ModuleHealthViewModel module)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 43, 43, 43)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Width = 180,
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 59, 59, 59)),
            BorderThickness = new Thickness(1)
        };

        var stack = new StackPanel();

        // Header row
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameText = new TextBlock
        {
            Text = module.DisplayName,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 14
        };
        Grid.SetColumn(nameText, 0);

        // Health indicator color
        var healthColor = module.OverallHealth switch
        {
            OneRoomHealth.Hardware.ViewModels.ModuleHealthStatus.Healthy => Windows.UI.Color.FromArgb(255, 16, 124, 16),
            OneRoomHealth.Hardware.ViewModels.ModuleHealthStatus.Degraded => Windows.UI.Color.FromArgb(255, 255, 185, 0),
            OneRoomHealth.Hardware.ViewModels.ModuleHealthStatus.Unhealthy => Windows.UI.Color.FromArgb(255, 209, 52, 56),
            OneRoomHealth.Hardware.ViewModels.ModuleHealthStatus.Offline => Windows.UI.Color.FromArgb(255, 209, 52, 56),
            OneRoomHealth.Hardware.ViewModels.ModuleHealthStatus.Disabled => Windows.UI.Color.FromArgb(255, 121, 119, 117),
            _ => Windows.UI.Color.FromArgb(255, 121, 119, 117)
        };

        var healthIndicator = new TextBlock
        {
            Text = module.HealthIcon,
            FontSize = 16,
            Foreground = new SolidColorBrush(healthColor)
        };
        Grid.SetColumn(healthIndicator, 1);

        headerGrid.Children.Add(nameText);
        headerGrid.Children.Add(healthIndicator);
        stack.Children.Add(headerGrid);

        // Status summary
        var statusText = new TextBlock
        {
            Text = module.StatusSummary,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136)),
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0)
        };
        stack.Children.Add(statusText);

        // Device list (show first 3 devices)
        if (module.Devices.Count > 0)
        {
            var deviceStack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var device in module.Devices.Take(3))
            {
                var deviceGrid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
                deviceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                deviceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var deviceHealthColor = device.Health switch
                {
                    OneRoomHealth.Hardware.Abstractions.DeviceHealth.Healthy => Windows.UI.Color.FromArgb(255, 16, 124, 16),
                    OneRoomHealth.Hardware.Abstractions.DeviceHealth.Unhealthy => Windows.UI.Color.FromArgb(255, 255, 185, 0),
                    OneRoomHealth.Hardware.Abstractions.DeviceHealth.Offline => Windows.UI.Color.FromArgb(255, 209, 52, 56),
                    _ => Windows.UI.Color.FromArgb(255, 121, 119, 117)
                };

                var deviceIndicator = new TextBlock
                {
                    Text = device.HealthIcon,
                    Foreground = new SolidColorBrush(deviceHealthColor),
                    FontSize = 10,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                Grid.SetColumn(deviceIndicator, 0);

                var deviceName = new TextBlock
                {
                    Text = device.DeviceName,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 204, 204, 204)),
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(deviceName, 1);

                deviceGrid.Children.Add(deviceIndicator);
                deviceGrid.Children.Add(deviceName);
                deviceStack.Children.Add(deviceGrid);
            }

            if (module.Devices.Count > 3)
            {
                var moreText = new TextBlock
                {
                    Text = $"+{module.Devices.Count - 3} more...",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 102, 102, 102)),
                    FontSize = 10,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                deviceStack.Children.Add(moreText);
            }

            stack.Children.Add(deviceStack);
        }

        // Last update
        var updateText = new TextBlock
        {
            Text = $"Updated: {module.LastUpdateDisplay}",
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 102, 102, 102)),
            FontSize = 10,
            Margin = new Thickness(0, 8, 0, 0)
        };
        stack.Children.Add(updateText);

        // Error message if any
        if (!string.IsNullOrEmpty(module.LastError))
        {
            var errorText = new TextBlock
            {
                Text = module.LastError,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 209, 52, 56)),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            stack.Children.Add(errorText);
        }

        card.Child = stack;
        return card;
    }

    private void CloseHealthButton_Click(object sender, RoutedEventArgs e)
    {
        _healthPanelVisible = false;
        HardwareHealthPanel.Visibility = Visibility.Collapsed;
        _healthRefreshTimer?.Dispose();
        _healthRefreshTimer = null;
        Logger.Log("Hardware health panel closed");
    }

    #endregion

    #region Log Viewer

    private bool _logsVisible = false;

    private void ViewLogsButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleLogViewer();
    }

    private void ToggleLogViewer()
    {
        _logsVisible = !_logsVisible;

        if (_logsVisible)
        {
            // Hide health panel if visible
            if (_healthPanelVisible)
            {
                HardwareHealthPanel.Visibility = Visibility.Collapsed;
                _healthPanelVisible = false;
                _healthRefreshTimer?.Dispose();
                _healthRefreshTimer = null;
            }

            LogViewerPanel.Visibility = Visibility.Visible;
            RefreshLogDisplay();

            // Adjust WebView margin to make room for log viewer
            if (KioskWebView != null)
            {
                KioskWebView.Margin = new Thickness(0, 80, 0, 300);
            }

            Logger.Log("Log viewer shown");
        }
        else
        {
            LogViewerPanel.Visibility = Visibility.Collapsed;

            // Restore WebView margin (accounting for debug panel)
            if (KioskWebView != null)
            {
                KioskWebView.Margin = new Thickness(0, 80, 0, 0);
            }

            Logger.Log("Log viewer hidden");
        }
    }

    private void RefreshLogDisplay()
    {
        try
        {
            var logs = Logger.GetRecentLogs(500); // Get last 500 entries
            var logText = string.Join(Environment.NewLine, logs);

            LogContentTextBlock.Text = logText;
            LogCountTextBlock.Text = $"{logs.Count} log entries (showing last 500 of {Logger.GetLogs().Count} total)";

            // Auto-scroll to bottom
            if (LogViewerPanel.Visibility == Visibility.Visible)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ScrollToBottom();
                });
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
        // Update log display in real-time if viewer is visible
        if (_logsVisible && LogViewerPanel.Visibility == Visibility.Visible)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    // Append new log entry
                    if (!string.IsNullOrEmpty(LogContentTextBlock.Text))
                    {
                        LogContentTextBlock.Text += Environment.NewLine;
                    }
                    LogContentTextBlock.Text += logEntry;

                    // Update count
                    var totalLogs = Logger.GetLogs().Count;
                    var displayedLogs = LogContentTextBlock.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.None).Length;
                    LogCountTextBlock.Text = $"{displayedLogs} log entries (showing last 500 of {totalLogs} total)";

                    // Auto-scroll to bottom
                    ScrollToBottom();
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error in OnLogAdded: {ex.Message}");
                }
            });
        }
    }

    private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
    {
        LogContentTextBlock.Text = "";
        LogCountTextBlock.Text = "0 log entries";
        Logger.Log("Log viewer cleared by user");
    }

    private void CloseLogsButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleLogViewer();
    }

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
}
