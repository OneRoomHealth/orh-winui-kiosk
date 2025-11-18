# Logging Troubleshooting Guide

## Log File Not Being Created?

### Expected Location
The log file should be created at:
```
%LOCALAPPDATA%\OneRoomHealthKiosk\logs\kiosk.log
```

This typically resolves to:
```
C:\Users\[USERNAME]\AppData\Local\OneRoomHealthKiosk\logs\kiosk.log
```

### Quick Check
Run the provided PowerShell script to find logs:
```powershell
.\show-logs.ps1
```

This script will:
- Search all possible log locations
- Show the last 20 log entries if found
- Create the log directory if missing
- Open the log folder in Explorer

### Manual Check
1. Open File Explorer
2. Type `%LOCALAPPDATA%` in the address bar and press Enter
3. Look for `OneRoomHealthKiosk` folder
4. Check for `logs` subfolder
5. Look for `kiosk.log` file

### If No Logs Are Created

1. **Permission Issues**
   - The app may not have permission to create folders in %LOCALAPPDATA%
   - Try running the app as Administrator once

2. **Packaged App Restrictions**
   - If running as a packaged app, logs might be in a different location
   - Check: `%LOCALAPPDATA%\Packages\[AppPackageId]\LocalState`

3. **Fallback Location**
   - If the app can't create the normal log directory, it falls back to:
   - `%TEMP%\kiosk.log`
   - Check: Type `%TEMP%` in File Explorer

4. **Debug Output**
   - All log messages are also written to Debug output
   - Run the app with Visual Studio attached to see logs in Output window

### Improvements in Latest Version

The updated Logger (v1.0.36+):
- Creates `logs` subdirectory automatically
- Shows error message if log creation fails
- Writes to Debug output even if file logging fails
- Reports the log file path at startup

### What Gets Logged

With the enhanced logging:
- Window activation and initialization
- Display detection and selection
- Window positioning and verification
- WebView initialization steps
- Navigation events
- Status overlay show/hide events
- All errors with stack traces

### Enable More Logging

If you need even more detail:
1. The app writes to Debug output - attach a debugger
2. Use Windows Event Viewer for security events
3. Check Application event log for crash information

### Sample Log Output
```
[2025-11-18 10:30:45.123 UTC] === OneRoom Health Kiosk App Starting ===
[2025-11-18 10:30:45.125 UTC] Log file location: C:\Users\User\AppData\Local\OneRoomHealthKiosk\logs\kiosk.log
[2025-11-18 10:30:45.130 UTC] ==================== WINDOW ACTIVATION START ====================
[2025-11-18 10:30:45.132 UTC] Video mode: false
[2025-11-18 10:30:45.133 UTC] Target monitor index: 1
...
```

### Still No Logs?

If you still see no logs after trying the above:

1. **Check Antivirus**
   - Some antivirus software blocks log file creation
   - Add an exception for the app

2. **Check Group Policy**
   - Corporate policies might restrict file creation
   - Check with IT administrator

3. **Use Process Monitor**
   - Download Process Monitor from Microsoft
   - Filter by process name "KioskApp.exe"
   - Look for file access denied errors

4. **Report Issue**
   - Note the exact error messages
   - Check Windows Event Viewer
   - Include OS version and user permissions
