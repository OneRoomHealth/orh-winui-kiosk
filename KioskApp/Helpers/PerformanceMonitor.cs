using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace KioskApp.Helpers;

/// <summary>
/// Performance snapshot at a point in time.
/// </summary>
public class PerformanceSnapshot
{
    public DateTime Timestamp { get; init; }
    public long WorkingSetBytes { get; init; }
    public long GcTotalMemoryBytes { get; init; }
    public long PrivateMemoryBytes { get; init; }
    public int Gen0Collections { get; init; }
    public int Gen1Collections { get; init; }
    public int Gen2Collections { get; init; }
    public double CpuUsagePercent { get; init; }
    public int ThreadCount { get; init; }
    public int HandleCount { get; init; }
    public TimeSpan Uptime { get; init; }

    public string WorkingSetMB => $"{WorkingSetBytes / (1024.0 * 1024.0):F1} MB";
    public string GcTotalMemoryMB => $"{GcTotalMemoryBytes / (1024.0 * 1024.0):F1} MB";
    public string PrivateMemoryMB => $"{PrivateMemoryBytes / (1024.0 * 1024.0):F1} MB";
}

/// <summary>
/// Module performance metrics.
/// </summary>
public class ModulePerformanceMetrics
{
    public string ModuleName { get; init; } = string.Empty;
    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
    public double AverageResponseTimeMs { get; set; }
    public double MaxResponseTimeMs { get; set; }
    public double MinResponseTimeMs { get; set; }
    public DateTime LastRequestTime { get; set; }

    private readonly List<double> _responseTimes = new();
    private readonly object _lock = new();

    public void RecordRequest(double responseTimeMs, bool success)
    {
        lock (_lock)
        {
            TotalRequests++;
            if (success)
                SuccessfulRequests++;
            else
                FailedRequests++;

            _responseTimes.Add(responseTimeMs);
            if (_responseTimes.Count > 100) // Keep last 100 samples
                _responseTimes.RemoveAt(0);

            AverageResponseTimeMs = _responseTimes.Average();
            MaxResponseTimeMs = _responseTimes.Max();
            MinResponseTimeMs = _responseTimes.Min();
            LastRequestTime = DateTime.UtcNow;
        }
    }

    public double SuccessRate => TotalRequests > 0 ? (SuccessfulRequests * 100.0 / TotalRequests) : 100.0;
}

/// <summary>
/// Monitors application performance and GC metrics.
/// </summary>
public class PerformanceMonitor : IDisposable
{
    private static PerformanceMonitor? _instance;
    private static readonly object _lock = new();

    private readonly Process _currentProcess;
    private readonly DateTime _startTime;
    private readonly Timer _sampleTimer;
    private readonly List<PerformanceSnapshot> _snapshots = new();
    private readonly Dictionary<string, ModulePerformanceMetrics> _moduleMetrics = new();
    private readonly int _maxSnapshots = 300; // 5 minutes at 1-second intervals

    private DateTime _lastCpuTime;
    private TimeSpan _lastTotalProcessorTime;

    /// <summary>
    /// Event fired when a new performance snapshot is taken.
    /// </summary>
    public event Action<PerformanceSnapshot>? SnapshotTaken;

    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    public static PerformanceMonitor Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new PerformanceMonitor();
                }
            }
            return _instance;
        }
    }

    private PerformanceMonitor()
    {
        _currentProcess = Process.GetCurrentProcess();
        _startTime = DateTime.UtcNow;
        _lastCpuTime = DateTime.UtcNow;
        _lastTotalProcessorTime = _currentProcess.TotalProcessorTime;

        // Sample every second
        _sampleTimer = new Timer(
            _ => TakeSnapshot(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1));
    }

    private void TakeSnapshot()
    {
        try
        {
            _currentProcess.Refresh();

            // Calculate CPU usage
            var currentCpuTime = DateTime.UtcNow;
            var currentTotalProcessorTime = _currentProcess.TotalProcessorTime;
            var cpuUsedMs = (currentTotalProcessorTime - _lastTotalProcessorTime).TotalMilliseconds;
            var totalMsPassed = (currentCpuTime - _lastCpuTime).TotalMilliseconds;
            var cpuUsagePercent = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed) * 100;

            _lastCpuTime = currentCpuTime;
            _lastTotalProcessorTime = currentTotalProcessorTime;

            var snapshot = new PerformanceSnapshot
            {
                Timestamp = DateTime.UtcNow,
                WorkingSetBytes = _currentProcess.WorkingSet64,
                GcTotalMemoryBytes = GC.GetTotalMemory(false),
                PrivateMemoryBytes = _currentProcess.PrivateMemorySize64,
                Gen0Collections = GC.CollectionCount(0),
                Gen1Collections = GC.CollectionCount(1),
                Gen2Collections = GC.CollectionCount(2),
                CpuUsagePercent = Math.Min(100, Math.Max(0, cpuUsagePercent)),
                ThreadCount = _currentProcess.Threads.Count,
                HandleCount = _currentProcess.HandleCount,
                Uptime = DateTime.UtcNow - _startTime
            };

            lock (_snapshots)
            {
                _snapshots.Add(snapshot);
                while (_snapshots.Count > _maxSnapshots)
                    _snapshots.RemoveAt(0);
            }

            SnapshotTaken?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            Logger.Log($"PerformanceMonitor error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get the most recent snapshot.
    /// </summary>
    public PerformanceSnapshot? GetLatestSnapshot()
    {
        lock (_snapshots)
        {
            return _snapshots.LastOrDefault();
        }
    }

    /// <summary>
    /// Get recent snapshots for trend analysis.
    /// </summary>
    public IReadOnlyList<PerformanceSnapshot> GetRecentSnapshots(int count = 60)
    {
        lock (_snapshots)
        {
            return _snapshots.TakeLast(count).ToList();
        }
    }

    /// <summary>
    /// Get or create metrics for a module.
    /// </summary>
    public ModulePerformanceMetrics GetModuleMetrics(string moduleName)
    {
        lock (_moduleMetrics)
        {
            if (!_moduleMetrics.TryGetValue(moduleName, out var metrics))
            {
                metrics = new ModulePerformanceMetrics { ModuleName = moduleName };
                _moduleMetrics[moduleName] = metrics;
            }
            return metrics;
        }
    }

    /// <summary>
    /// Get all module metrics.
    /// </summary>
    public IReadOnlyList<ModulePerformanceMetrics> GetAllModuleMetrics()
    {
        lock (_moduleMetrics)
        {
            return _moduleMetrics.Values.ToList();
        }
    }

    /// <summary>
    /// Record a module request for performance tracking.
    /// </summary>
    public void RecordModuleRequest(string moduleName, double responseTimeMs, bool success)
    {
        var metrics = GetModuleMetrics(moduleName);
        metrics.RecordRequest(responseTimeMs, success);
    }

    /// <summary>
    /// Force a garbage collection and return memory stats.
    /// </summary>
    public (long Before, long After, long Freed) ForceGarbageCollection()
    {
        var before = GC.GetTotalMemory(false);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var after = GC.GetTotalMemory(true);
        var freed = before - after;

        Logger.Log($"GC forced: {before / 1024.0 / 1024.0:F1}MB -> {after / 1024.0 / 1024.0:F1}MB (freed {freed / 1024.0 / 1024.0:F1}MB)");

        return (before, after, freed);
    }

    /// <summary>
    /// Get memory pressure status.
    /// </summary>
    public string GetMemoryPressureStatus()
    {
        var memoryInfo = GC.GetGCMemoryInfo();
        var loadPercent = (double)memoryInfo.MemoryLoadBytes / memoryInfo.HighMemoryLoadThresholdBytes * 100;

        if (loadPercent > 90) return "Critical";
        if (loadPercent > 75) return "High";
        if (loadPercent > 50) return "Moderate";
        return "Normal";
    }

    /// <summary>
    /// Get uptime as formatted string.
    /// </summary>
    public string GetUptimeFormatted()
    {
        var uptime = DateTime.UtcNow - _startTime;
        if (uptime.TotalDays >= 1)
            return $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m";
        if (uptime.TotalHours >= 1)
            return $"{uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";
        return $"{uptime.Minutes}m {uptime.Seconds}s";
    }

    public void Dispose()
    {
        _sampleTimer.Dispose();
        _currentProcess.Dispose();
    }
}
