using OneRoomHealth.Hardware.Abstractions;

namespace OneRoomHealth.Hardware.Modules.SystemAudio;

/// <summary>
/// Runtime state tracking for system audio.
/// </summary>
internal class SystemAudioDeviceState
{
    /// <summary>
    /// Current health status.
    /// </summary>
    public DeviceHealth Health { get; set; } = DeviceHealth.Offline;

    /// <summary>
    /// Last time state was updated (UTC).
    /// </summary>
    public DateTime? LastSeen { get; set; }

    /// <summary>
    /// Speaker volume (0-100).
    /// </summary>
    public int SpeakerVolume { get; set; } = 50;

    /// <summary>
    /// Speaker mute state.
    /// </summary>
    public bool SpeakerMuted { get; set; } = false;

    /// <summary>
    /// Microphone volume (0-100).
    /// </summary>
    public int MicrophoneVolume { get; set; } = 75;

    /// <summary>
    /// Microphone mute state.
    /// </summary>
    public bool MicrophoneMuted { get; set; } = false;

    /// <summary>
    /// Recent error messages.
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Computer name.
    /// </summary>
    public string ComputerName { get; set; } = Environment.MachineName;

    /// <summary>
    /// Platform info.
    /// </summary>
    public string Platform { get; set; } = Environment.OSVersion.ToString();
}

/// <summary>
/// Detailed status response for system audio.
/// </summary>
public class SystemAudioStatus
{
    public string Id { get; init; } = "0";
    public string Name { get; init; } = "System Audio";
    public DeviceHealth Health { get; set; }
    public DateTime? LastSeen { get; set; }
    public int SpeakerVolume { get; set; }
    public bool SpeakerMuted { get; set; }
    public int MicrophoneVolume { get; set; }
    public bool MicrophoneMuted { get; set; }
    public string? ComputerName { get; set; }
    public string? Platform { get; set; }
    public List<string> Errors { get; set; } = new();
}
