using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace KioskApp
{
    /// <summary>
    /// Controls video playback using either WebView2 (HTML5) or external MPV player
    /// Implements Flic button functionality for switching between carescape and demo videos
    /// </summary>
    public class VideoController : IDisposable
    {
        private Process? _mpvProcess;
        private bool _isDemoPlaying;
        private CancellationTokenSource _cancellationSource;
        private Task? _monitoringTask;
        private readonly VideoModeSettings _settings;
        private readonly int _targetMonitorIndex;

        // Volume control via Windows Core Audio API
        private readonly VolumeController _volumeController;

        public bool IsVideoModeEnabled => _settings.Enabled;
        public bool IsDemoPlaying => _isDemoPlaying;

        public VideoController(VideoModeSettings settings, int targetMonitorIndex)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _targetMonitorIndex = targetMonitorIndex;
            _cancellationSource = new CancellationTokenSource();
            _volumeController = new VolumeController();
        }

        /// <summary>
        /// Initialize video controller (validates paths but does not start playback)
        /// Video will start when triggered by Flic button or explicit API call
        /// </summary>
        public Task InitializeAsync()
        {
            if (!_settings.Enabled)
            {
                Logger.Log("Video mode is disabled in configuration");
                return Task.CompletedTask;
            }

            if (!ValidateVideoPaths())
            {
                Logger.Log("Video paths validation failed");
                return Task.CompletedTask;
            }

            Logger.Log("Video controller initialized (ready, waiting for trigger)");
            // Don't start video automatically - wait for Flic button or explicit trigger
            return Task.CompletedTask;
        }

        /// <summary>
        /// Handle Flic button press (toggle between videos)
        /// If no video is playing, starts carescape video
        /// </summary>
        public async Task HandleFlicButtonPressAsync()
        {
            // Check if any video is currently playing
            bool isVideoPlaying = _mpvProcess != null && !_mpvProcess.HasExited;
            
            if (!isVideoPlaying)
            {
                // No video playing - start carescape video
                Logger.Log("Flic pressed: Starting carescape video (no video currently playing)");
                await PlayCarescapeVideoAsync();
            }
            else if (_isDemoPlaying)
            {
                // Demo is playing - return to carescape
                Logger.Log("Flic pressed: Returning to carescape video");
                await PlayCarescapeVideoAsync();
            }
            else
            {
                // Carescape is playing - switch to demo
                Logger.Log("Flic pressed: Playing demo video");
                await PlayDemoVideoAsync();
            }
        }

        /// <summary>
        /// Play carescape video (loops indefinitely)
        /// </summary>
        public async Task PlayCarescapeVideoAsync()
        {
            try
            {
                // Cancel demo monitoring if running
                if (_isDemoPlaying)
                {
                    _cancellationSource?.Cancel();
                    _cancellationSource?.Dispose();
                    _cancellationSource = new CancellationTokenSource();
                }
                
                _isDemoPlaying = false;
                
                // Start carescape video
                bool success = await StartMpvAsync(
                    _settings.CarescapeVideoPath,
                    loop: true,
                    volume: _settings.CarescapeVolume
                );

                if (success)
                {
                    Logger.Log("Carescape video started successfully");
                }
                else
                {
                    Logger.Log("Failed to start carescape video");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error playing carescape video: {ex.Message}");
            }
        }

        /// <summary>
        /// Play demo video (plays once then returns to carescape)
        /// </summary>
        private async Task PlayDemoVideoAsync()
        {
            try
            {
                _isDemoPlaying = true;

                // Start demo video
                bool success = await StartMpvAsync(
                    _settings.DemoVideoPath,
                    loop: false,
                    volume: _settings.DemoVolume
                );

                if (success)
                {
                    Logger.Log("Demo video started successfully");
                    
                    // Start monitoring for demo completion
                    _monitoringTask = MonitorDemoCompletionAsync(_cancellationSource.Token);
                }
                else
                {
                    Logger.Log("Failed to start demo video");
                    _isDemoPlaying = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error playing demo video: {ex.Message}");
                _isDemoPlaying = false;
            }
        }

        /// <summary>
        /// Monitor demo video completion and return to carescape
        /// </summary>
        private async Task MonitorDemoCompletionAsync(CancellationToken cancellationToken)
        {
            try
            {
                Logger.Log("Starting demo video monitoring...");

                while (_isDemoPlaying && !cancellationToken.IsCancellationRequested)
                {
                    if (_mpvProcess?.HasExited ?? true)
                    {
                        Logger.Log("Demo video completed, returning to carescape");
                        await PlayCarescapeVideoAsync();
                        break;
                    }

                    await Task.Delay(500, cancellationToken);
                }

                Logger.Log("Demo monitoring ended");
            }
            catch (OperationCanceledException)
            {
                Logger.Log("Demo monitoring canceled");
            }
        }

        /// <summary>
        /// Start MPV with specified video
        /// </summary>
        private async Task<bool> StartMpvAsync(string videoPath, bool loop, double volume)
        {
            try
            {
                // Find MPV executable
                string? mpvPath = FindMpvExecutable();
                if (string.IsNullOrEmpty(mpvPath))
                {
                    Logger.Log("MPV executable not found");
                    return false;
                }

                // Build MPV arguments
                var args = new List<string>
                {
                    "--fullscreen",
                    "--no-osc",
                    "--no-border",
                    "--ontop",
                    $"--screen={_targetMonitorIndex - 1}", // MPV uses 0-based index
                    $"--fs-screen={_targetMonitorIndex - 1}",
                    "--quiet"
                };

                if (loop)
                {
                    args.Add("--loop-file=inf");
                }

                args.Add($"\"{videoPath}\"");

                // Kill existing MPV process if running
                if (_mpvProcess != null && !_mpvProcess.HasExited)
                {
                    _mpvProcess.Kill();
                    await Task.Delay(100);
                }

                // Start new MPV process
                var startInfo = new ProcessStartInfo
                {
                    FileName = mpvPath,
                    Arguments = string.Join(" ", args),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _mpvProcess = Process.Start(startInfo);
                
                if (_mpvProcess == null)
                {
                    Logger.Log("Failed to start MPV process");
                    return false;
                }

                // Set volume after slight delay
                await Task.Delay(300);
                _volumeController.SetSystemVolume(volume);

                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error starting MPV: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Find MPV executable in common locations
        /// </summary>
        private string? FindMpvExecutable()
        {
            var mpvPaths = new List<string>();

            // Add configured path if available
            if (!string.IsNullOrEmpty(_settings.MpvPath))
            {
                mpvPaths.Add(_settings.MpvPath);
            }

            // Add standard paths
            mpvPaths.AddRange(new[]
            {
                "mpv", // In PATH
                @"C:\mpv\mpv.exe",
                @"C:\Program Files\mpv\mpv.exe",
                @"C:\Program Files (x86)\mpv\mpv.exe",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mpv", "mpv.exe"),
                // Add same paths from Python script
                @"C:\Users\CareWall\Downloads\mpv-x86_64-v3-20251012-git-ad59ff1\mpv.exe"
            });

            foreach (var path in mpvPaths)
            {
                if (path == "mpv")
                {
                    // Check if mpv is in PATH
                    try
                    {
                        var result = Process.Start(new ProcessStartInfo
                        {
                            FileName = "where",
                            Arguments = "mpv",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        });

                        if (result != null)
                        {
                            result.WaitForExit();
                            if (result.ExitCode == 0)
                            {
                                return "mpv";
                            }
                        }
                    }
                    catch { }
                }
                else if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        /// <summary>
        /// Validate that video files exist
        /// </summary>
        private bool ValidateVideoPaths()
        {
            if (!File.Exists(_settings.CarescapeVideoPath))
            {
                Logger.Log($"Carescape video not found: {_settings.CarescapeVideoPath}");
                return false;
            }

            if (!File.Exists(_settings.DemoVideoPath))
            {
                Logger.Log($"Demo video not found: {_settings.DemoVideoPath}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Stop all video playback
        /// </summary>
        public async Task StopAsync()
        {
            Logger.Log("Stopping video playback...");
            
            _cancellationSource?.Cancel();
            
            if (_mpvProcess != null && !_mpvProcess.HasExited)
            {
                _mpvProcess.Kill();
                await Task.Delay(100);
            }

            if (_monitoringTask != null)
            {
                try
                {
                    await _monitoringTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                    Logger.Log("Monitoring task canceled as expected");
                }
            }
            
            _isDemoPlaying = false;
            Logger.Log("Video playback stopped");
        }

        /// <summary>
        /// Restart carescape video
        /// </summary>
        public async Task RestartCarescapeAsync()
        {
            Logger.Log("Restarting carescape video...");
            await PlayCarescapeVideoAsync();
        }

        public void Dispose()
        {
            _cancellationSource?.Cancel();
            _cancellationSource?.Dispose();
            _mpvProcess?.Dispose();
            _volumeController?.Dispose();
        }
    }

    /// <summary>
    /// Controls system volume using Windows Core Audio API
    /// </summary>
    internal class VolumeController : IDisposable
    {
        // This is a simplified version - in production, use NAudio or CSCore
        public void SetSystemVolume(double volumePercent)
        {
            try
            {
                // Use PowerShell as a workaround for simplicity
                var volume = Math.Max(0, Math.Min(100, volumePercent));
                var script = $@"
                    Add-Type -TypeDefinition @'
                    using System.Runtime.InteropServices;
                    [Guid(""5CDF2C82-841E-4546-9722-0CF74078229A""), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                    interface IAudioEndpointVolume {{
                        int f(); int g(); int h(); int i();
                        int SetMasterVolumeLevelScalar(float fLevel, System.Guid pguidEventContext);
                        int j();
                        int GetMasterVolumeLevelScalar(out float pfLevel);
                    }}
                    [Guid(""D666063F-1587-4E43-81F1-B948E807363F""), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                    interface IMMDevice {{
                        int Activate(ref System.Guid id, int clsCtx, int activationParams, out IAudioEndpointVolume aev);
                    }}
                    [Guid(""A95664D2-9614-4F35-A746-DE8DB63617E6""), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                    interface IMMDeviceEnumerator {{
                        int f();
                        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
                    }}
                    [ComImport, Guid(""BCDE0395-E52F-467C-8E3D-C4579291692E"")] class MMDeviceEnumeratorComObject {{ }}
                    public class Audio {{
                        static IAudioEndpointVolume Vol() {{
                            var enumerator = new MMDeviceEnumeratorComObject() as IMMDeviceEnumerator;
                            IMMDevice dev = null;
                            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out dev));
                            IAudioEndpointVolume epv = null;
                            var epvid = typeof(IAudioEndpointVolume).GUID;
                            Marshal.ThrowExceptionForHR(dev.Activate(ref epvid, 23, 0, out epv));
                            return epv;
                        }}
                        public static void SetVolume(float v) {{
                            Marshal.ThrowExceptionForHR(Vol().SetMasterVolumeLevelScalar(v, System.Guid.Empty));
                        }}
                    }}
                    '@
                    [Audio]::SetVolume({volume / 100.0}f)
                ";

                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-Command \"{script}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                process?.WaitForExit(1000);
            }
            catch
            {
                // Silently fail - volume control is not critical
            }
        }

        public void Dispose()
        {
            // Cleanup if needed
        }
    }
}
