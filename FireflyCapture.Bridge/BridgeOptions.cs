namespace FireflyCapture.Bridge;

/// <summary>
/// Configuration options for the Firefly bridge process.
/// Bound from appsettings.json under the "Bridge" key.
/// </summary>
public sealed class BridgeOptions
{
    /// <summary>Port the HTTP server listens on. Default: 5200.</summary>
    public int Port { get; set; } = 5200;

    /// <summary>
    /// Path to SnapDll.dll. Relative paths are resolved from the exe directory.
    /// Default: "SnapDll.dll" (same directory as the bridge exe).
    /// </summary>
    public string SnapDllPath { get; set; } = "SnapDll.dll";

    /// <summary>
    /// How often to poll <c>IsButtonpress()</c> in milliseconds. Default: 10ms.
    /// Lower values reduce button-press latency but increase CPU usage.
    /// </summary>
    public int PollingIntervalMs { get; set; } = 10;
}
