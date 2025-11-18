# Video Startup and Window Positioning Fix Plan

## Issues Identified

### Issue 1: Video Starts Immediately on Startup
**Problem**: When video paths are configured, the carescape video starts playing immediately when the app initializes, even though it should wait for user interaction (Flic button press).

**Root Cause**: 
- In `MainWindow.xaml.cs`, `InitializeWebViewAsync()` calls `_videoController.InitializeAsync()` when video mode is enabled
- `VideoController.InitializeAsync()` immediately calls `PlayCarescapeVideoAsync()`, which starts the video

**Location**: 
- `KioskApp/MainWindow.xaml.cs:637` - calls `InitializeAsync()`
- `KioskApp/VideoController.cs:39-54` - `InitializeAsync()` immediately plays video

### Issue 2: Window and Video on Different Monitors
**Problem**: The carescape video displays correctly on screen index 1, but the main app window is stuck on screen index 0.

**Root Cause**:
- Main window uses `_config.Kiosk.TargetMonitorIndex` for positioning (line 566 in MainWindow.xaml.cs)
- Video controller uses `_config.Kiosk.VideoMode.TargetMonitor` for positioning (line 193 in VideoController.cs)
- These are **two separate configuration properties** that can have different values
- Default values: `TargetMonitorIndex = 1`, `VideoMode.TargetMonitor = 1` (both default to monitor 1, but user may have configured them differently)

**Location**:
- `KioskApp/MainWindow.xaml.cs:566` - uses `_config.Kiosk.TargetMonitorIndex`
- `KioskApp/VideoController.cs:193` - uses `_settings.TargetMonitor` (from `VideoModeSettings`)

## Solution Plan

### Fix 1: Defer Video Start Until Triggered

**Option A: Don't Auto-Start Video (Recommended)**
- Change `VideoController.InitializeAsync()` to NOT automatically start the video
- Only prepare/validate the video controller
- Video should start when:
  - Flic button is pressed (Ctrl+Alt+D/E/R)
  - OR explicit API call to start video
  - OR configuration option `autoStartVideo: true` is set

**Option B: Add Configuration Flag**
- Add `autoStartVideo` boolean to `VideoModeSettings`
- If `true`, start video on initialization (current behavior)
- If `false`, wait for trigger (new behavior)

**Recommendation**: Option A (simpler, cleaner) - videos should only start on user interaction or explicit command.

**Implementation**:
1. Modify `VideoController.InitializeAsync()` to only validate paths, not start playback
2. Create new method `VideoController.StartCarescapeVideoAsync()` that actually starts the video
3. Update `MainWindow.xaml.cs` to NOT call video start on initialization
4. Ensure Flic button handlers call the start method when needed

### Fix 2: Synchronize Monitor Configuration

**Option A: Use Single Source of Truth (Recommended)**
- Remove `VideoModeSettings.TargetMonitor` property
- Always use `KioskSettings.TargetMonitorIndex` for both window and video positioning
- This ensures they're always on the same monitor

**Option B: Sync on Initialization**
- When initializing video controller, use `KioskSettings.TargetMonitorIndex` if `VideoModeSettings.TargetMonitor` is not explicitly set
- Log a warning if they differ

**Option C: Make VideoMode.TargetMonitor Override**
- Keep both properties
- Video uses `VideoMode.TargetMonitor` if set, otherwise falls back to `Kiosk.TargetMonitorIndex`
- Window always uses `Kiosk.TargetMonitorIndex`

**Recommendation**: Option A - simpler, less confusing, ensures consistency.

**Implementation**:
1. Remove `TargetMonitor` from `VideoModeSettings` class
2. Update `VideoController` constructor to accept monitor index separately, or pass `KioskSettings` reference
3. Update `VideoController.StartMpvAsync()` to use the passed monitor index
4. Update `MainWindow` to pass `_config.Kiosk.TargetMonitorIndex` to video controller

## Implementation Steps

### Step 1: Fix Video Auto-Start
1. Modify `VideoController.InitializeAsync()`:
   ```csharp
   public async Task InitializeAsync()
   {
       if (!_settings.Enabled) return;
       if (!ValidateVideoPaths()) return;
       Logger.Log("Video controller initialized (ready, not started)");
       // Don't call PlayCarescapeVideoAsync() here
   }
   ```

2. Update `MainWindow.xaml.cs`:
   ```csharp
   if (_isVideoMode)
   {
       KioskWebView.Visibility = Visibility.Collapsed;
       if (_videoController != null)
       {
           await _videoController.InitializeAsync(); // Just validates, doesn't start
           Logger.Log("Video controller ready (waiting for trigger)");
       }
   }
   ```

3. Ensure Flic button handlers start video when needed (they already call `PlayCarescapeVideoAsync()`)

### Step 2: Fix Monitor Synchronization
1. Remove `TargetMonitor` from `VideoModeSettings` in `KioskConfiguration.cs`
2. Update `VideoController` constructor to accept monitor index:
   ```csharp
   public VideoController(VideoModeSettings settings, int targetMonitorIndex)
   {
       _settings = settings;
       _targetMonitorIndex = targetMonitorIndex;
       // ...
   }
   ```

3. Update `VideoController.StartMpvAsync()` to use `_targetMonitorIndex`:
   ```csharp
   $"--screen={_targetMonitorIndex - 1}", // MPV uses 0-based
   $"--fs-screen={_targetMonitorIndex - 1}",
   ```

4. Update `MainWindow` to pass monitor index:
   ```csharp
   if (_isVideoMode && _config.Kiosk.VideoMode != null)
   {
       _videoController = new VideoController(
           _config.Kiosk.VideoMode, 
           _config.Kiosk.TargetMonitorIndex
       );
   }
   ```

5. Update any existing config files or documentation that reference `videoMode.targetMonitor`

## Testing Checklist

- [ ] Video does NOT start automatically on app launch
- [ ] Video starts when Flic button (Ctrl+Alt+D) is pressed
- [ ] Main window appears on correct monitor (TargetMonitorIndex)
- [ ] Video appears on same monitor as main window
- [ ] Both window and video use same monitor index from config
- [ ] Config migration works (old configs with `videoMode.targetMonitor` still work)

## Migration Notes

If users have existing configs with `videoMode.targetMonitor`, we should:
1. Log a warning that this property is deprecated
2. Use `kiosk.targetMonitorIndex` instead
3. Or provide a migration script that copies the value

## Files to Modify

1. `KioskApp/VideoController.cs` - Remove auto-start, add monitor index parameter
2. `KioskApp/MainWindow.xaml.cs` - Pass monitor index, don't auto-start video
3. `KioskApp/KioskConfiguration.cs` - Remove `TargetMonitor` from `VideoModeSettings`
4. Documentation files - Update any references to `videoMode.targetMonitor`

