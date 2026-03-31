using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OneRoomHealth.Hardware.Abstractions;
using OneRoomHealth.Hardware.Modules.Firefly;

namespace KioskApp;

/// <summary>
/// MainWindow partial class - Medical Devices (Firefly otoscope) debug tab.
/// </summary>
public sealed partial class MainWindow
{
    #region Fields

    private bool _fireflyPanelVisible = false;

    #endregion

    #region Refresh

    private async Task RefreshFireflyDevicesAsync()
    {
        try
        {
            var fireflyModule = App.Services?.GetService(typeof(FireflyModule)) as FireflyModule;

            if (fireflyModule == null)
            {
                DispatcherQueue.TryEnqueue(() => RenderFireflyUnavailable("Firefly module is not registered. Enable Hardware API mode first."));
                return;
            }

            if (!fireflyModule.IsEnabled)
            {
                DispatcherQueue.TryEnqueue(() => RenderFireflyUnavailable("Firefly module is disabled. Set hardware.firefly.enabled = true in config.json."));
                return;
            }

            var devices = await fireflyModule.GetDevicesAsync();

            // Fetch detailed status for each device
            var statuses = new List<FireflyDeviceStatus?>();
            foreach (var device in devices)
            {
                var statusObj = await fireflyModule.GetDeviceStatusAsync(device.Id);
                statuses.Add(statusObj as FireflyDeviceStatus);
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                FireflyDeviceListPanel.Children.Clear();

                if (devices.Count == 0)
                {
                    RenderFireflyEmpty();
                }
                else
                {
                    for (int i = 0; i < devices.Count; i++)
                    {
                        var device = devices[i];
                        var status = statuses[i];
                        FireflyDeviceListPanel.Children.Add(BuildFireflyDeviceCard(device, status));
                    }
                }

                FireflyLastRefreshText.Text = $"Refreshed: {DateTime.Now:HH:mm:ss}";
            });

            Logger.Log($"[MEDICAL DEVICES] Refreshed — {devices.Count} Firefly device(s)");
        }
        catch (Exception ex)
        {
            Logger.Log($"[MEDICAL DEVICES] Error refreshing Firefly devices: {ex.Message}");
            DispatcherQueue.TryEnqueue(() => RenderFireflyUnavailable($"Error: {ex.Message}"));
        }
    }

    private async void RefreshFireflyButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshFireflyDevicesAsync();
    }

    #endregion

    #region Rendering

    private void RenderFireflyUnavailable(string message)
    {
        FireflyDeviceListPanel.Children.Clear();
        FireflyDeviceListPanel.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 133, 133, 133)),
            FontSize = 12,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 8, 4, 0)
        });
        FireflyLastRefreshText.Text = $"Refreshed: {DateTime.Now:HH:mm:ss}";
    }

    private void RenderFireflyEmpty()
    {
        FireflyDeviceListPanel.Children.Add(new TextBlock
        {
            Text = "No Firefly devices detected. Connect a Firefly UVC otoscope camera (VID 0x21CD).",
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 133, 133, 133)),
            FontSize = 12,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 8, 4, 0)
        });
    }

    private Border BuildFireflyDeviceCard(DeviceInfo device, FireflyDeviceStatus? status)
    {
        var healthColor = device.Health switch
        {
            DeviceHealth.Healthy   => ColorHelper.FromArgb(255, 78, 201, 176),   // teal
            DeviceHealth.Unhealthy => ColorHelper.FromArgb(255, 220, 220, 170),  // yellow
            DeviceHealth.Offline   => ColorHelper.FromArgb(255, 244, 135, 113),  // red
            _                      => ColorHelper.FromArgb(255, 133, 133, 133)   // grey
        };

        var healthLabel = device.Health switch
        {
            DeviceHealth.Healthy   => "Healthy",
            DeviceHealth.Unhealthy => "Unhealthy",
            DeviceHealth.Offline   => "Offline",
            _                      => "Unknown"
        };

        // Card border
        var card = new Border
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 37, 37, 38)),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 10, 12, 10)
        };

        var content = new StackPanel { Spacing = 6 };

        // Header row: health dot + device name
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        headerRow.Children.Add(new TextBlock
        {
            Text = "\u25CF",
            Foreground = new SolidColorBrush(healthColor),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });
        headerRow.Children.Add(new TextBlock
        {
            Text = status?.FriendlyName ?? device.Name,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 204, 204, 204)),
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        if (status?.Model is { Length: > 0 } model)
        {
            headerRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 60, 60, 60)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                Child = new TextBlock
                {
                    Text = model,
                    Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 156, 220, 254)),
                    FontSize = 10,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas")
                }
            });
        }
        content.Children.Add(headerRow);

        // Property rows
        AddFireflyPropertyRow(content, "Status", healthLabel, healthColor);
        AddFireflyPropertyRow(content, "Device ID", device.Id);

        if (status != null)
        {
            AddFireflyPropertyRow(content, "Connected", status.IsConnected ? "Yes" : "No");
            AddFireflyPropertyRow(content, "Captures", status.CaptureCount.ToString());

            if (status.LastCaptureAt.HasValue)
                AddFireflyPropertyRow(content, "Last Capture", status.LastCaptureAt.Value.ToLocalTime().ToString("HH:mm:ss"));

            AddFireflyPropertyRow(content, "Last Seen", status.LastSeen.ToLocalTime().ToString("HH:mm:ss"));
        }

        card.Child = content;
        return card;
    }

    private static void AddFireflyPropertyRow(StackPanel parent, string label, string value,
        Windows.UI.Color? valueColor = null)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 133, 133, 133)),
            FontSize = 11,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas")
        });

        var valueBlock = new TextBlock
        {
            Text = value,
            Foreground = new SolidColorBrush(valueColor ?? ColorHelper.FromArgb(255, 204, 204, 204)),
            FontSize = 11,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas"),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(valueBlock, 1);
        row.Children.Add(valueBlock);

        parent.Children.Add(row);
    }

    #endregion
}
