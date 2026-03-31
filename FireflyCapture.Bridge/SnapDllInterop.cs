using System.Runtime.InteropServices;

namespace FireflyCapture.Bridge;

/// <summary>
/// Managed wrapper for the 32-bit native SnapDll.dll.
/// This class MUST run inside a 32-bit (x86) process — the DLL cannot be
/// loaded by a 64-bit host. Load the library once at startup via the
/// constructor and dispose when the application exits.
/// </summary>
/// <remarks>
/// Confirmed exports (PE32 ordinal table):
///   Ordinal 1 — IsButtonpress   (CallingConvention.StdCall, returns bool)
///   Ordinal 2 — ReleaseButton   (CallingConvention.StdCall, void)
/// The DLL also references SnapShut.txt in %TEMP% for internal state;
/// no action is needed on the managed side for that file.
/// </remarks>
public sealed class SnapDllInterop : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate bool IsButtonpressDelegate();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void ReleaseButtonDelegate();

    private readonly ILogger<SnapDllInterop> _logger;
    private IntPtr _libraryHandle = IntPtr.Zero;
    private IsButtonpressDelegate? _isButtonpress;
    private ReleaseButtonDelegate? _releaseButton;
    private bool _disposed;

    /// <summary>
    /// Whether the native library was loaded and both exports resolved successfully.
    /// If false all method calls are no-ops.
    /// </summary>
    public bool IsLoaded { get; private init; }

    /// <param name="dllPath">
    /// Absolute or relative path to SnapDll.dll.
    /// Relative paths are resolved from the process working directory.
    /// </param>
    /// <param name="logger">Logger for interop diagnostics.</param>
    public SnapDllInterop(string dllPath, ILogger<SnapDllInterop> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        try
        {
            _libraryHandle = NativeLibrary.Load(Path.GetFullPath(dllPath));

            var isButtonpressPtr = NativeLibrary.GetExport(_libraryHandle, "IsButtonpress");
            var releaseButtonPtr = NativeLibrary.GetExport(_libraryHandle, "ReleaseButton");

            _isButtonpress = Marshal.GetDelegateForFunctionPointer<IsButtonpressDelegate>(isButtonpressPtr);
            _releaseButton = Marshal.GetDelegateForFunctionPointer<ReleaseButtonDelegate>(releaseButtonPtr);

            IsLoaded = true;
            _logger.LogInformation("SnapDll loaded successfully from {Path}", Path.GetFullPath(dllPath));
        }
        catch (DllNotFoundException ex)
        {
            _logger.LogError(ex, "SnapDll not found at {Path}. See docs/snapdll-setup.md.", dllPath);
        }
        catch (EntryPointNotFoundException ex)
        {
            _logger.LogError(ex, "SnapDll found but required exports missing. Expected: IsButtonpress, ReleaseButton.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load SnapDll from {Path}", dllPath);
        }
    }

    /// <summary>
    /// Returns true if the Firefly hardware snap button is currently pressed.
    /// Always returns false if the library failed to load.
    /// </summary>
    public bool IsButtonPressed()
    {
        if (!IsLoaded || _isButtonpress == null) return false;

        try
        {
            return _isButtonpress();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IsButtonpress native call failed");
            return false;
        }
    }

    /// <summary>
    /// Signals the hardware that the button press has been acknowledged.
    /// Must be called promptly after <see cref="IsButtonPressed"/> returns true
    /// so the hardware can reset its pressed state.
    /// No-op if the library failed to load.
    /// </summary>
    public void ReleaseButton()
    {
        if (!IsLoaded || _releaseButton == null) return;

        try
        {
            _releaseButton();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReleaseButton native call failed");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _isButtonpress = null;
        _releaseButton = null;

        if (_libraryHandle != IntPtr.Zero)
        {
            NativeLibrary.Free(_libraryHandle);
            _libraryHandle = IntPtr.Zero;
            _logger.LogInformation("SnapDll unloaded");
        }
    }
}
