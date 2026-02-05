using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Serilog.Core;
using Serilog.Events;

namespace KioskApp.Helpers;

/// <summary>
/// Log level enum for filtering.
/// </summary>
public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3
}

/// <summary>
/// Represents a unified log entry from any source.
/// </summary>
public class UnifiedLogEntry
{
    public DateTime Timestamp { get; init; }
    public LogLevel Level { get; init; }
    public string Module { get; init; } = "App";
    public string Message { get; init; } = string.Empty;
    public string? Exception { get; init; }

    public string FormattedMessage => $"[{Timestamp:HH:mm:ss.fff}] [{Level,-7}] [{Module,-12}] {Message}";

    public string LevelIcon => Level switch
    {
        LogLevel.Debug => "D",
        LogLevel.Info => "I",
        LogLevel.Warning => "W",
        LogLevel.Error => "E",
        _ => "?"
    };
}

/// <summary>
/// Unified logging service that aggregates logs from all sources (KioskApp Logger and Serilog).
/// Provides filtering by log level and module for the debug panel.
/// </summary>
public class UnifiedLogger : ILogEventSink
{
    private static UnifiedLogger? _instance;
    private static readonly object _lock = new();

    private readonly ConcurrentQueue<UnifiedLogEntry> _logBuffer = new();
    private readonly int _maxBufferSize;
    private LogLevel _minimumLevel = LogLevel.Debug;
    private readonly HashSet<string> _enabledModules = new();
    private bool _allModulesEnabled = true;

    /// <summary>
    /// Event fired when a new log entry is added.
    /// </summary>
    public event Action<UnifiedLogEntry>? LogAdded;

    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    public static UnifiedLogger Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new UnifiedLogger(5000);
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// Known module names for filtering.
    /// </summary>
    public static readonly string[] KnownModules = new[]
    {
        "App",
        "Display",
        "Camera",
        "Lighting",
        "SystemAudio",
        "Microphone",
        "Speaker",
        "Biamp",
        "Hardware",
        "HardwareAPI",
        "HealthMonitor",
        "WebView"
    };

    private UnifiedLogger(int maxBufferSize = 5000)
    {
        _maxBufferSize = maxBufferSize;

        // Subscribe to existing KioskApp logger
        Logger.LogAdded += OnKioskAppLog;
    }

    /// <summary>
    /// Handles logs from KioskApp Logger.
    /// </summary>
    private void OnKioskAppLog(string message)
    {
        // Parse the existing log format: [timestamp] message
        var level = LogLevel.Info;
        var module = "App";

        // Detect log level from message content
        if (message.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("EXCEPTION", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("FATAL", StringComparison.OrdinalIgnoreCase))
        {
            level = LogLevel.Error;
        }
        else if (message.Contains("WARN", StringComparison.OrdinalIgnoreCase))
        {
            level = LogLevel.Warning;
        }
        else if (message.Contains("DEBUG", StringComparison.OrdinalIgnoreCase))
        {
            level = LogLevel.Debug;
        }

        // Detect module from message content
        if (message.Contains("WebView", StringComparison.OrdinalIgnoreCase))
            module = "WebView";
        else if (message.Contains("Video", StringComparison.OrdinalIgnoreCase))
            module = "App";

        // Extract just the message without timestamp if present
        var cleanMessage = message;
        if (message.StartsWith("["))
        {
            var closeBracket = message.IndexOf(']');
            if (closeBracket > 0 && closeBracket < message.Length - 1)
            {
                cleanMessage = message[(closeBracket + 1)..].TrimStart();
            }
        }

        AddEntry(new UnifiedLogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Module = module,
            Message = cleanMessage
        });
    }

    /// <summary>
    /// ILogEventSink implementation for Serilog.
    /// </summary>
    public void Emit(LogEvent logEvent)
    {
        var level = logEvent.Level switch
        {
            LogEventLevel.Verbose => LogLevel.Debug,
            LogEventLevel.Debug => LogLevel.Debug,
            LogEventLevel.Information => LogLevel.Info,
            LogEventLevel.Warning => LogLevel.Warning,
            LogEventLevel.Error => LogLevel.Error,
            LogEventLevel.Fatal => LogLevel.Error,
            _ => LogLevel.Info
        };

        // Extract module name from properties or source context
        var module = "Hardware";
        if (logEvent.Properties.TryGetValue("SourceContext", out var sourceContext))
        {
            var source = sourceContext.ToString().Trim('"');
            // Extract module name from fully qualified name
            if (source.Contains("DisplayModule")) module = "Display";
            else if (source.Contains("CameraModule")) module = "Camera";
            else if (source.Contains("LightingModule")) module = "Lighting";
            else if (source.Contains("SystemAudioModule")) module = "SystemAudio";
            else if (source.Contains("MicrophoneModule")) module = "Microphone";
            else if (source.Contains("SpeakerModule")) module = "Speaker";
            else if (source.Contains("BiampModule")) module = "Biamp";
            // Note: BiampTelnetClient uses BiampModule's logger, so its logs are captured above
            else if (source.Contains("HardwareApiServer")) module = "HardwareAPI";
            else if (source.Contains("HardwareManager")) module = "Hardware";
            else if (source.Contains("HealthMonitor")) module = "HealthMonitor";
            else if (source.Contains("HealthVisualization")) module = "HealthMonitor";
        }

        // Check if ModuleName is in the message (structured logging)
        if (logEvent.Properties.TryGetValue("ModuleName", out var moduleName))
        {
            module = moduleName.ToString().Trim('"');
        }

        var message = logEvent.RenderMessage();
        var exception = logEvent.Exception?.ToString();

        AddEntry(new UnifiedLogEntry
        {
            Timestamp = logEvent.Timestamp.UtcDateTime,
            Level = level,
            Module = module,
            Message = message,
            Exception = exception
        });
    }

    /// <summary>
    /// Add a log entry directly.
    /// </summary>
    public void Log(LogLevel level, string module, string message, Exception? exception = null)
    {
        AddEntry(new UnifiedLogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Module = module,
            Message = message,
            Exception = exception?.ToString()
        });
    }

    private void AddEntry(UnifiedLogEntry entry)
    {
        _logBuffer.Enqueue(entry);

        // Maintain buffer size
        while (_logBuffer.Count > _maxBufferSize)
        {
            _logBuffer.TryDequeue(out _);
        }

        // Fire event if entry passes filters
        if (ShouldShow(entry))
        {
            LogAdded?.Invoke(entry);
        }
    }

    /// <summary>
    /// Check if an entry should be shown based on current filters.
    /// </summary>
    public bool ShouldShow(UnifiedLogEntry entry)
    {
        if (entry.Level < _minimumLevel)
            return false;

        if (!_allModulesEnabled && !_enabledModules.Contains(entry.Module))
            return false;

        return true;
    }

    /// <summary>
    /// Get all log entries that pass current filters.
    /// </summary>
    public IReadOnlyList<UnifiedLogEntry> GetFilteredLogs(int maxCount = 500)
    {
        return _logBuffer
            .Where(ShouldShow)
            .TakeLast(maxCount)
            .ToList();
    }

    /// <summary>
    /// Get all log entries regardless of filters.
    /// </summary>
    public IReadOnlyList<UnifiedLogEntry> GetAllLogs(int maxCount = 500)
    {
        return _logBuffer
            .TakeLast(maxCount)
            .ToList();
    }

    /// <summary>
    /// Set the minimum log level filter.
    /// </summary>
    public void SetMinimumLevel(LogLevel level)
    {
        _minimumLevel = level;
    }

    /// <summary>
    /// Get the current minimum log level.
    /// </summary>
    public LogLevel GetMinimumLevel() => _minimumLevel;

    /// <summary>
    /// Enable filtering for a specific module.
    /// </summary>
    public void EnableModule(string module)
    {
        _enabledModules.Add(module);
        _allModulesEnabled = false;
    }

    /// <summary>
    /// Disable filtering for a specific module.
    /// </summary>
    public void DisableModule(string module)
    {
        _enabledModules.Remove(module);
        if (_enabledModules.Count == 0)
            _allModulesEnabled = true;
    }

    /// <summary>
    /// Enable all modules (no filtering).
    /// </summary>
    public void EnableAllModules()
    {
        _allModulesEnabled = true;
        _enabledModules.Clear();
    }

    /// <summary>
    /// Get currently enabled modules.
    /// </summary>
    public IReadOnlySet<string> GetEnabledModules() => _enabledModules;

    /// <summary>
    /// Check if all modules are enabled.
    /// </summary>
    public bool AreAllModulesEnabled => _allModulesEnabled;

    /// <summary>
    /// Get statistics about the log buffer.
    /// </summary>
    public (int Total, int Debug, int Info, int Warning, int Error) GetStats()
    {
        var logs = _logBuffer.ToArray();
        return (
            logs.Length,
            logs.Count(l => l.Level == LogLevel.Debug),
            logs.Count(l => l.Level == LogLevel.Info),
            logs.Count(l => l.Level == LogLevel.Warning),
            logs.Count(l => l.Level == LogLevel.Error)
        );
    }

    /// <summary>
    /// Clear all logs.
    /// </summary>
    public void Clear()
    {
        while (_logBuffer.TryDequeue(out _)) { }
    }
}
