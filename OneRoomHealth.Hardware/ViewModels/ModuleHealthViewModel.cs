using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OneRoomHealth.Hardware.Abstractions;

namespace OneRoomHealth.Hardware.ViewModels;

/// <summary>
/// Overall module health status.
/// </summary>
public enum ModuleHealthStatus
{
    NotImplemented,  // Module not yet ported
    Disabled,        // Configured as disabled
    Initializing,    // Currently initializing
    Healthy,         // All devices healthy
    Degraded,        // Some devices unhealthy
    Unhealthy,       // All devices unhealthy/offline
    Offline          // Module failed or not responding
}

/// <summary>
/// ViewModel for a hardware module's health status in the visualization panel.
/// </summary>
public class ModuleHealthViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isEnabled;
    private bool _isInitialized;
    private bool _isMonitoring;
    private int _deviceCount;
    private int _healthyCount;
    private int _unhealthyCount;
    private int _offlineCount;
    private ModuleHealthStatus _overallHealth;
    private DateTime _lastUpdate;
    private string? _lastError;
    private bool _isExpanded;

    public string ModuleName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string IconGlyph { get; init; } = "\uE8B7"; // Default device icon
    public string Description { get; init; } = string.Empty;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public bool IsInitialized
    {
        get => _isInitialized;
        set => SetProperty(ref _isInitialized, value);
    }

    public bool IsMonitoring
    {
        get => _isMonitoring;
        set => SetProperty(ref _isMonitoring, value);
    }

    public int DeviceCount
    {
        get => _deviceCount;
        set
        {
            if (SetProperty(ref _deviceCount, value))
                OnPropertyChanged(nameof(StatusSummary));
        }
    }

    public int HealthyCount
    {
        get => _healthyCount;
        set
        {
            if (SetProperty(ref _healthyCount, value))
                OnPropertyChanged(nameof(StatusSummary));
        }
    }

    public int UnhealthyCount
    {
        get => _unhealthyCount;
        set
        {
            if (SetProperty(ref _unhealthyCount, value))
                OnPropertyChanged(nameof(StatusSummary));
        }
    }

    public int OfflineCount
    {
        get => _offlineCount;
        set
        {
            if (SetProperty(ref _offlineCount, value))
                OnPropertyChanged(nameof(StatusSummary));
        }
    }

    public ModuleHealthStatus OverallHealth
    {
        get => _overallHealth;
        set
        {
            if (SetProperty(ref _overallHealth, value))
            {
                OnPropertyChanged(nameof(HealthIcon));
                OnPropertyChanged(nameof(HealthColorHex));
            }
        }
    }

    public DateTime LastUpdate
    {
        get => _lastUpdate;
        set
        {
            if (SetProperty(ref _lastUpdate, value))
                OnPropertyChanged(nameof(LastUpdateDisplay));
        }
    }

    public string? LastError
    {
        get => _lastError;
        set => SetProperty(ref _lastError, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public ObservableCollection<DeviceHealthViewModel> Devices { get; } = new();
    public ObservableCollection<HealthEventViewModel> RecentEvents { get; } = new();

    // Computed properties
    public string StatusSummary
    {
        get
        {
            if (!IsEnabled) return "Disabled";
            if (!IsInitialized) return "Not Initialized";
            if (DeviceCount == 0) return "No devices";
            if (HealthyCount == DeviceCount) return $"{DeviceCount} device{(DeviceCount > 1 ? "s" : "")} healthy";
            return $"{HealthyCount}/{DeviceCount} healthy";
        }
    }

    public string HealthIcon => OverallHealth switch
    {
        ModuleHealthStatus.Healthy => "\u25CF",     // Filled circle
        ModuleHealthStatus.Degraded => "\u25D0",    // Half circle
        ModuleHealthStatus.Unhealthy => "\u25CB",   // Empty circle
        ModuleHealthStatus.Offline => "\u25CB",     // Empty circle
        ModuleHealthStatus.Disabled => "\u2014",    // Em dash
        ModuleHealthStatus.NotImplemented => "\u2014",
        _ => "\u25CB"
    };

    public string HealthColorHex => OverallHealth switch
    {
        ModuleHealthStatus.Healthy => "#107C10",    // Green
        ModuleHealthStatus.Degraded => "#FFB900",   // Yellow
        ModuleHealthStatus.Unhealthy => "#D13438",  // Red
        ModuleHealthStatus.Offline => "#D13438",    // Red
        ModuleHealthStatus.Disabled => "#797775",   // Gray
        ModuleHealthStatus.NotImplemented => "#EDEBE9", // Light gray
        _ => "#797775"
    };

    public string LastUpdateDisplay
    {
        get
        {
            if (LastUpdate == default) return "Never";
            var elapsed = DateTime.UtcNow - LastUpdate;
            if (elapsed.TotalSeconds < 5) return "Just now";
            if (elapsed.TotalSeconds < 60) return $"{(int)elapsed.TotalSeconds}s ago";
            if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
            return $"{(int)elapsed.TotalHours}h ago";
        }
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// ViewModel for an individual device's health status.
/// </summary>
public class DeviceHealthViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private DeviceHealth _health;
    private bool _isEnabled;
    private DateTime _lastSeen;
    private TimeSpan? _lastResponseTime;
    private string? _lastError;

    public string DeviceId { get; init; } = string.Empty;
    public string DeviceName { get; init; } = string.Empty;
    public string ModuleName { get; init; } = string.Empty;
    public string DeviceType { get; init; } = string.Empty;

    public DeviceHealth Health
    {
        get => _health;
        set
        {
            if (_health != value)
            {
                _health = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HealthIcon));
                OnPropertyChanged(nameof(HealthColorHex));
            }
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime LastSeen
    {
        get => _lastSeen;
        set
        {
            if (_lastSeen != value)
            {
                _lastSeen = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LastSeenDisplay));
            }
        }
    }

    public TimeSpan? LastResponseTime
    {
        get => _lastResponseTime;
        set
        {
            if (_lastResponseTime != value)
            {
                _lastResponseTime = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ResponseTimeDisplay));
            }
        }
    }

    public string? LastError
    {
        get => _lastError;
        set
        {
            if (_lastError != value)
            {
                _lastError = value;
                OnPropertyChanged();
            }
        }
    }

    // Module-specific properties
    public Dictionary<string, object> Properties { get; } = new();

    // Computed properties
    public string HealthIcon => Health switch
    {
        DeviceHealth.Healthy => "\u25CF",   // Filled circle
        DeviceHealth.Unhealthy => "\u25D0", // Half circle
        DeviceHealth.Offline => "\u25CB",   // Empty circle
        _ => "\u25CB"
    };

    public string HealthColorHex => Health switch
    {
        DeviceHealth.Healthy => "#107C10",  // Green
        DeviceHealth.Unhealthy => "#FFB900", // Yellow
        DeviceHealth.Offline => "#D13438",  // Red
        _ => "#797775"
    };

    public string LastSeenDisplay
    {
        get
        {
            if (LastSeen == default) return "Never";
            var elapsed = DateTime.UtcNow - LastSeen;
            if (elapsed.TotalSeconds < 5) return "Just now";
            if (elapsed.TotalSeconds < 60) return $"{(int)elapsed.TotalSeconds}s ago";
            if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
            return $"{(int)elapsed.TotalHours}h ago";
        }
    }

    public string ResponseTimeDisplay => LastResponseTime.HasValue
        ? $"{LastResponseTime.Value.TotalMilliseconds:F0}ms"
        : "N/A";

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// ViewModel for health change events.
/// </summary>
public class HealthEventViewModel
{
    public DateTime Timestamp { get; init; }
    public string DeviceId { get; init; } = string.Empty;
    public string DeviceName { get; init; } = string.Empty;
    public string ModuleName { get; init; } = string.Empty;
    public DeviceHealth PreviousHealth { get; init; }
    public DeviceHealth NewHealth { get; init; }
    public string? Message { get; init; }

    public string TimestampDisplay => Timestamp.ToLocalTime().ToString("HH:mm:ss");

    public string ChangeDescription => $"{PreviousHealth} \u2192 {NewHealth}";

    public string ChangeColorHex => NewHealth switch
    {
        DeviceHealth.Healthy => "#107C10",
        DeviceHealth.Unhealthy => "#FFB900",
        DeviceHealth.Offline => "#D13438",
        _ => "#797775"
    };
}

/// <summary>
/// Aggregated system health summary.
/// </summary>
public class SystemHealthSummary : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private int _totalModules;
    private int _activeModules;
    private int _healthyModules;
    private int _totalDevices;
    private int _healthyDevices;
    private DateTime _lastUpdate;
    private TimeSpan _uptime;
    private string _apiEndpoint = "http://localhost:8081";

    public int TotalModules
    {
        get => _totalModules;
        set { _totalModules = value; OnPropertyChanged(); }
    }

    public int ActiveModules
    {
        get => _activeModules;
        set { _activeModules = value; OnPropertyChanged(); }
    }

    public int HealthyModules
    {
        get => _healthyModules;
        set { _healthyModules = value; OnPropertyChanged(); }
    }

    public int TotalDevices
    {
        get => _totalDevices;
        set { _totalDevices = value; OnPropertyChanged(); }
    }

    public int HealthyDevices
    {
        get => _healthyDevices;
        set { _healthyDevices = value; OnPropertyChanged(); }
    }

    public DateTime LastUpdate
    {
        get => _lastUpdate;
        set { _lastUpdate = value; OnPropertyChanged(); }
    }

    public TimeSpan Uptime
    {
        get => _uptime;
        set { _uptime = value; OnPropertyChanged(); OnPropertyChanged(nameof(UptimeDisplay)); }
    }

    public string ApiEndpoint
    {
        get => _apiEndpoint;
        set { _apiEndpoint = value; OnPropertyChanged(); }
    }

    public string UptimeDisplay
    {
        get
        {
            if (Uptime.TotalHours >= 1)
                return $"{(int)Uptime.TotalHours}h {Uptime.Minutes}m";
            if (Uptime.TotalMinutes >= 1)
                return $"{(int)Uptime.TotalMinutes}m {Uptime.Seconds}s";
            return $"{(int)Uptime.TotalSeconds}s";
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
