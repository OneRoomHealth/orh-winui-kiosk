# Enhanced Logging for Debugging - November 18, 2025

## Overview
Added comprehensive logging throughout the window initialization and WebView setup process to help diagnose:
1. **Blank screen issues** - tracking status overlay visibility
2. **Wrong monitor issues** - tracking window positioning and display selection

## Logging Categories

### 1. Window Activation (MainWindow_Activated)
```
==================== WINDOW ACTIVATION START ====================
Video mode: true/false
Target monitor index: X
==================== WINDOW ACTIVATION COMPLETE ====================
```

### 2. Window Configuration (ConfigureAsKioskWindow)
```
========== ConfigureAsKioskWindow START ==========
Window ID: XXXXX
AppWindow retrieved: true/false

========== DISPLAY DETECTION ==========
Found X display(s)
  Display 0: 1920x1080 at (0, 0)
  Display 1: 1920x1080 at (1920, 0)

========== MONITOR SELECTION ==========
Configured target monitor index: X
✓ Using configured monitor index X
  Target display ID: XXXXX
  Target bounds: X, Y, WxH
  
========== WINDOW POSITIONING ==========
Target bounds: X=0, Y=0, W=1920, H=1080
Calling SetWindowPos with: X=0, Y=0, W=1920, H=1080
✓ SetWindowPos called successfully

Target monitor index: 1, starting verification task: true
Waiting 200ms before verifying window position...

========== WINDOW POSITION VERIFICATION ==========
Current display: XXXXX
Target display: XXXXX
✓ Window successfully positioned on target display
  Current bounds: X=1920, Y=0, W=1920x1080

========== ConfigureAsKioskWindow COMPLETE ==========
```

### 3. WebView Initialization (InitializeWebViewAsync)
```
========== InitializeWebViewAsync START ==========
Video mode: false
WEB MODE: Initializing WebView2
WebView2 Runtime version: XXX.X.XXXX.XX
[STATUS] SHOWING: Initializing - Loading WebView2...
[STATUS] StatusOverlay.Visibility set to VISIBLE

Ensuring CoreWebView2 is ready...
CoreWebView2 is ready, setting up WebView...
WebView2 setup complete, ensuring status overlay is hidden
[STATUS] HIDING status overlay
[STATUS] StatusOverlay.Visibility set to COLLAPSED

Setting WebView source to: https://...
✓ Navigation initiated to: https://...

Starting 3-second timeout fallback for status overlay
========== InitializeWebViewAsync COMPLETE ==========
```

### 4. Navigation Events (OnNavigationCompleted)
```
========== NAVIGATION COMPLETED ==========
Success: true, Error: None
✓ Navigation successful to: https://...
[STATUS] SHOWING: Navigation Complete - https://...
Current URL updated to: https://...
Window title updated to: OneRoom Health Kiosk
Hiding status overlay after successful navigation
[STATUS] HIDING status overlay
[STATUS] StatusOverlay.Visibility set to COLLAPSED
```

### 5. Status Overlay Tracking
Every `ShowStatus()` and `HideStatus()` call now logs:
```
[STATUS] SHOWING: Title - Detail
[STATUS] StatusOverlay.Visibility set to VISIBLE

[STATUS] HIDING status overlay
[STATUS] StatusOverlay.Visibility set to COLLAPSED
```

### 6. Timeout Fallback
After 3 seconds:
```
Timeout reached. Current status title: 'Initializing'
✓ Forcing status overlay to hide after timeout
or
Status already changed to 'Navigation Complete', not forcing hide
```

## Log File Location
`%LocalAppData%\OneRoomHealthKiosk\logs\kiosk.log`

Typical path: `C:\Users\[USERNAME]\AppData\Local\OneRoomHealthKiosk\logs\kiosk.log`

## How to Use These Logs

### Diagnosing Blank Screen
1. Look for the status overlay tracking:
   - If you see `[STATUS] SHOWING` but no `[STATUS] HIDING`, the overlay is stuck
   - Check if navigation completed successfully
   - Check if the 3-second timeout fired

2. Key indicators:
   ```
   ✓ Navigation successful to: [URL]  ← Page loaded
   [STATUS] HIDING status overlay   ← Overlay should hide here
   [STATUS] StatusOverlay.Visibility set to COLLAPSED  ← Confirmed hidden
   ```

### Diagnosing Wrong Monitor
1. Look for display detection:
   ```
   Found 2 display(s)
     Display 0: 1920x1080 at (0, 0)      ← Primary
     Display 1: 1920x1080 at (1920, 0)    ← Secondary
   ```

2. Check monitor selection:
   ```
   Configured target monitor index: 1
   ✓ Using configured monitor index 1
   Target display ID: XXXXX
   ```

3. Verify positioning:
   ```
   Calling SetWindowPos with: X=1920, Y=0, W=1920, H=1080
   ✓ SetWindowPos called successfully
   ```

4. Check verification results:
   ```
   ========== WINDOW POSITION VERIFICATION ==========
   Current display: XXXXX
   Target display: XXXXX
   ✓ Window successfully positioned on target display
   ```

### Common Issues

**Issue: Blank white screen**
- Look for: Status overlay not being hidden
- Search log for: `[STATUS] HIDING status overlay`
- Expected: Should see this after navigation completes or after 3-second timeout

**Issue: Window on wrong monitor**
- Look for: Display ID mismatch in verification section
- Search log for: `✗ Window is NOT on target display!`
- Expected: `✓ Window successfully positioned on target display`

**Issue: Invalid monitor index**
- Look for: `✗ WARNING: Monitor index X is INVALID`
- Solution: Reduce `targetMonitorIndex` in config.json

## Error Indicators

All errors use the ✗ symbol for easy searching:
```bash
# Find all errors in log
findstr /C:"✗" kiosk.log
```

Success indicators use ✓:
```bash
# Find all successes in log
findstr /C:"✓" kiosk.log
```

## Next Steps

If issues persist after reviewing logs:
1. Copy the relevant log section (from WINDOW ACTIVATION START to COMPLETE)
2. Check for any ✗ error markers
3. Verify the window positioning and status overlay sequences
4. Compare actual vs expected behavior using the log patterns above

