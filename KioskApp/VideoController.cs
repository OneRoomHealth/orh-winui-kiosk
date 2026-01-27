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
        private int _currentDemoIndex; // 1 or 2, tracks which demo video is playing
        private CancellationTokenSource _cancellationSource;
        private Task? _monitoringTask;
        private readonly VideoModeSettings _settings;
        private readonly int _targetMonitorIndex;

        public bool IsVideoModeEnabled => _settings.Enabled;
        public bool IsDemoPlaying => _isDemoPlaying;
        public int CurrentDemoIndex => _currentDemoIndex;

        public VideoController(VideoModeSettings settings, int targetMonitorIndex)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _targetMonitorIndex = targetMonitorIndex;
            _cancellationSource = new CancellationTokenSource();
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
        /// Handle Flic button press - legacy method, now calls ToggleDemoVideosAsync
        /// </summary>
        public async Task HandleFlicButtonPressAsync()
        {
            await ToggleDemoVideosAsync();
        }

        /// <summary>
        /// Resume playback with carescape video.
        /// Used when restarting video mode (e.g., after monitor switch) to preserve expected behavior.
        /// </summary>
        public async Task ResumePlaybackAsync()
        {
            Logger.Log("Resuming video playback with carescape");
            await PlayCarescapeVideoAsync();
        }

        /// <summary>
        /// Toggle between demo videos (demo1 -> demo2 -> demo1...)
        /// If no demo is playing, starts with demo1
        /// </summary>
        public async Task ToggleDemoVideosAsync()
        {
            // Check if any video is currently playing
            bool isVideoPlaying = _mpvProcess != null && !_mpvProcess.HasExited;

            if (!isVideoPlaying || !_isDemoPlaying)
            {
                // No video playing or carescape is playing - start demo1
                Logger.Log("Toggle demos: Starting demo video 1");
                await PlayDemoVideoAsync(1);
            }
            else if (_currentDemoIndex == 1)
            {
                // Demo1 is playing - switch to demo2
                Logger.Log("Toggle demos: Switching to demo video 2");
                await PlayDemoVideoAsync(2);
            }
            else
            {
                // Demo2 is playing - switch to demo1
                Logger.Log("Toggle demos: Switching to demo video 1");
                await PlayDemoVideoAsync(1);
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
                    loop: true
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
        /// Play demo video (plays once then switches to the other demo)
        /// </summary>
        /// <param name="demoIndex">Which demo to play (1 or 2)</param>
        private async Task PlayDemoVideoAsync(int demoIndex)
        {
            try
            {
                // Cancel any existing monitoring
                if (_isDemoPlaying)
                {
                    _cancellationSource?.Cancel();
                    _cancellationSource?.Dispose();
                    _cancellationSource = new CancellationTokenSource();
                }

                _isDemoPlaying = true;
                _currentDemoIndex = demoIndex;

                // Get the correct demo path
                string demoPath = demoIndex == 1 ? _settings.DemoVideoPath1 : _settings.DemoVideoPath2;

                // Start demo video
                bool success = await StartMpvAsync(demoPath, loop: false);

                if (success)
                {
                    Logger.Log($"Demo video {demoIndex} started successfully: {demoPath}");

                    // Start monitoring for demo completion
                    _monitoringTask = MonitorDemoCompletionAsync(_cancellationSource.Token);
                }
                else
                {
                    Logger.Log($"Failed to start demo video {demoIndex}");
                    _isDemoPlaying = false;
                    _currentDemoIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error playing demo video {demoIndex}: {ex.Message}");
                _isDemoPlaying = false;
                _currentDemoIndex = 0;
            }
        }

        /// <summary>
        /// Monitor demo video completion and play the other demo video
        /// </summary>
        private async Task MonitorDemoCompletionAsync(CancellationToken cancellationToken)
        {
            try
            {
                Logger.Log($"Starting demo video {_currentDemoIndex} monitoring...");

                while (_isDemoPlaying && !cancellationToken.IsCancellationRequested)
                {
                    if (_mpvProcess?.HasExited ?? true)
                    {
                        // Play the other demo video when current one finishes
                        int nextDemo = _currentDemoIndex == 1 ? 2 : 1;
                        Logger.Log($"Demo video {_currentDemoIndex} completed, switching to demo {nextDemo}");
                        await PlayDemoVideoAsync(nextDemo);
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
        private async Task<bool> StartMpvAsync(string videoPath, bool loop)
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
                await KillMpvProcessAsync();

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

            if (!File.Exists(_settings.DemoVideoPath1))
            {
                Logger.Log($"Demo video 1 not found: {_settings.DemoVideoPath1}");
                return false;
            }

            if (!File.Exists(_settings.DemoVideoPath2))
            {
                Logger.Log($"Demo video 2 not found: {_settings.DemoVideoPath2}");
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

            // Kill MPV process
            await KillMpvProcessAsync();

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
            _currentDemoIndex = 0;
            Logger.Log("Video playback stopped");
        }

        /// <summary>
        /// Forcefully kills the MPV process if running
        /// </summary>
        private async Task KillMpvProcessAsync()
        {
            if (_mpvProcess == null)
            {
                return;
            }

            try
            {
                if (!_mpvProcess.HasExited)
                {
                    Logger.Log($"Killing MPV process (PID: {_mpvProcess.Id})...");
                    _mpvProcess.Kill(entireProcessTree: true);

                    // Wait for process to exit with timeout
                    var exitTask = Task.Run(() => _mpvProcess.WaitForExit(2000));
                    await exitTask;

                    if (!_mpvProcess.HasExited)
                    {
                        Logger.Log("MPV process did not exit within timeout, forcing termination...");
                        try
                        {
                            _mpvProcess.Kill();
                        }
                        catch { }
                    }

                    Logger.Log("MPV process terminated");
                }
                else
                {
                    Logger.Log("MPV process already exited");
                }
            }
            catch (InvalidOperationException)
            {
                // Process already exited
                Logger.Log("MPV process was already terminated");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error killing MPV process: {ex.Message}");
            }
            finally
            {
                try
                {
                    _mpvProcess.Dispose();
                }
                catch { }
                _mpvProcess = null;
            }
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
            Logger.Log("Disposing VideoController...");

            _cancellationSource?.Cancel();
            _cancellationSource?.Dispose();

            // Synchronously kill MPV process if still running
            if (_mpvProcess != null)
            {
                try
                {
                    if (!_mpvProcess.HasExited)
                    {
                        Logger.Log($"Dispose: Killing MPV process (PID: {_mpvProcess.Id})...");
                        _mpvProcess.Kill(entireProcessTree: true);
                        _mpvProcess.WaitForExit(2000);
                        Logger.Log("Dispose: MPV process killed");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Dispose: Error killing MPV process: {ex.Message}");
                }
                finally
                {
                    try
                    {
                        _mpvProcess.Dispose();
                    }
                    catch { }
                    _mpvProcess = null;
                }
            }

            Logger.Log("VideoController disposed");
        }
    }
}
