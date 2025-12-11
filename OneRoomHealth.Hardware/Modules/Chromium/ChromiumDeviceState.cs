using OneRoomHealth.Hardware.Abstractions;
using OneRoomHealth.Hardware.Configuration;
using System.Diagnostics;

namespace OneRoomHealth.Hardware.Modules.Chromium;

/// <summary>
/// Runtime state tracking for a Chromium browser instance.
/// </summary>
internal class ChromiumDeviceState
{
    /// <summary>
    /// Device configuration from config.json.
    /// </summary>
    public required ChromiumInstanceConfig Config { get; init; }

    /// <summary>
    /// Current health status.
    /// </summary>
    public DeviceHealth Health { get; set; } = DeviceHealth.Offline;

    /// <summary>
    /// Last time the browser process was seen running (UTC).
    /// </summary>
    public DateTime? LastSeen { get; set; }

    /// <summary>
    /// The Chrome/Chromium process.
    /// </summary>
    public Process? Process { get; set; }

    /// <summary>
    /// CDP (Chrome DevTools Protocol) port.
    /// </summary>
    public int CdpPort { get; set; } = 9222;

    /// <summary>
    /// Current URL (best effort tracking).
    /// </summary>
    public string? CurrentUrl { get; set; }

    /// <summary>
    /// Process ID.
    /// </summary>
    public int? ProcessId => Process?.Id;

    /// <summary>
    /// Whether the browser is currently running.
    /// </summary>
    public bool IsRunning => Process != null && !Process.HasExited;

    /// <summary>
    /// Recent error messages.
    /// </summary>
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Detailed status response for a Chromium browser instance.
/// </summary>
public class ChromiumStatus
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public DeviceHealth Health { get; set; }
    public DateTime? LastSeen { get; set; }
    public bool IsRunning { get; set; }
    public int? ProcessId { get; set; }
    public string? CurrentUrl { get; set; }
    public string? ChromiumPath { get; set; }
    public string? UserDataDir { get; set; }
    public string DisplayMode { get; set; } = "kiosk";
    public List<string> Errors { get; set; } = new();
}
