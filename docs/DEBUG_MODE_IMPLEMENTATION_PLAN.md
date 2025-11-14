# Debug Mode and Exit Strategy Implementation Plan
## OneRoom Health WinUI Kiosk Application

**Date:** November 7, 2025
**Version:** 1.0
**Author:** Development Team

---

## Executive Summary

This document outlines the implementation plan for adding two critical features to the OneRoom Health WinUI Kiosk application:

1. **Debug Mode**: A hotkey-activated mode that windows the WebView2 display for developer access
2. **Exit Strategy**: A secure mechanism to exit kiosk mode and return to the Windows desktop

Currently, the application has NO keyboard handling and NO exit mechanism beyond Ctrl+Alt+Del. These features are essential for maintenance, troubleshooting, and emergency access.

---

## Current State Analysis

### Existing Gaps
- ❌ No keyboard event handling in the application
- ❌ No hotkey support
- ❌ WebView2 developer tools explicitly disabled
- ❌ No windowed mode - always fullscreen
- ❌ No application-controlled exit mechanism
- ❌ No configuration file for settings

### Security Considerations
- Shell Launcher v2 provides OS-level lockdown
- Window is always-on-top and non-closeable
- All browser shortcuts are disabled
- Context menus are disabled

---

## Feature 1: Debug Mode

### Overview
Debug mode will allow administrators and developers to temporarily window the application for accessing developer tools, camera selection, and other debugging features.

### Design Specifications

#### 1.1 Hotkey Activation
**Primary Hotkey:** `Ctrl + Shift + F12`
**Alternative:** `Ctrl + Alt + D` (for "Debug")

**Rationale:**
- F12 is standard for developer tools
- Adding modifiers prevents accidental activation
- Not commonly used by web applications

#### 1.2 Mode Toggle Behavior

**When Entering Debug Mode:**
```
1. Window transitions from fullscreen to windowed (80% of screen)
2. Window becomes resizable and movable
3. WebView2 developer tools are enabled
4. Status overlay shows "DEBUG MODE ACTIVE"
5. Window title changes to include "[DEBUG]"
6. Context menus are re-enabled
7. Browser accelerator keys are re-enabled
```

**When Exiting Debug Mode:**
```
1. Window returns to fullscreen on target monitor
2. Window becomes non-resizable and immovable
3. Developer tools are disabled and closed
4. Status overlay is hidden
5. Window title returns to normal
6. Context menus are disabled
7. Browser accelerator keys are disabled
```

#### 1.3 Implementation Details

**Step 1: Add Keyboard Event Handler**
```csharp
// In MainWindow.xaml.cs constructor
public MainWindow()
{
    InitializeComponent();
    this.Activated += MainWindow_Activated;

    // NEW: Register keyboard handler
    var inputPane = CoreWindow.GetForCurrentThread();
    inputPane.KeyDown += OnKeyDown;
}

private async void OnKeyDown(CoreWindow sender, KeyEventArgs args)
{
    // Check for Ctrl+Shift+F12
    var ctrl = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control);
    var shift = Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift);

    if (ctrl.HasFlag(CoreVirtualKeyStates.Down) &&
        shift.HasFlag(CoreVirtualKeyStates.Down) &&
        args.VirtualKey == VirtualKey.F12)
    {
        await ToggleDebugMode();
        args.Handled = true;
    }
}
```

**Step 2: Add Debug Mode State**
```csharp
// Add class members
private bool _isDebugMode = false;
private Rect _normalWindowBounds;
private AppWindow _appWindow;

private async Task ToggleDebugMode()
{
    _isDebugMode = !_isDebugMode;

    if (_isDebugMode)
    {
        await EnterDebugMode();
    }
    else
    {
        await ExitDebugMode();
    }
}
```

**Step 3: Implement Debug Mode Methods**
```csharp
private async Task EnterDebugMode()
{
    Logger.Log("Entering debug mode");

    // 1. Store current bounds
    _normalWindowBounds = _appWindow.GetPlacement().DisplayRegion;

    // 2. Enable WebView2 developer features
    if (WebView2Control?.CoreWebView2?.Settings != null)
    {
        var settings = WebView2Control.CoreWebView2.Settings;
        settings.AreDevToolsEnabled = true;
        settings.AreDefaultContextMenusEnabled = true;
        settings.AreBrowserAcceleratorKeysEnabled = true;
    }

    // 3. Window to 80% of screen
    var monitor = GetTargetMonitor();
    var debugWidth = (int)(monitor.WorkArea.Width * 0.8);
    var debugHeight = (int)(monitor.WorkArea.Height * 0.8);
    var debugX = monitor.WorkArea.X + (monitor.WorkArea.Width - debugWidth) / 2;
    var debugY = monitor.WorkArea.Y + (monitor.WorkArea.Height - debugHeight) / 2;

    // 4. Remove always-on-top and make resizable
    ModifyWindowStyle(false, true); // Not topmost, resizable

    // 5. Resize and reposition
    MoveAndResizeWindow(debugX, debugY, debugWidth, debugHeight);

    // 6. Update UI
    this.Title = "[DEBUG] OneRoom Health Kiosk";
    ShowStatus("DEBUG MODE", "Developer tools enabled. Press Ctrl+Shift+F12 to exit.");
}

private async Task ExitDebugMode()
{
    Logger.Log("Exiting debug mode");

    // 1. Disable WebView2 developer features
    if (WebView2Control?.CoreWebView2?.Settings != null)
    {
        var settings = WebView2Control.CoreWebView2.Settings;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;

        // Close developer tools if open
        WebView2Control.CoreWebView2.CloseDevTools();
    }

    // 2. Return to fullscreen
    ConfigureAsKioskWindow();

    // 3. Update UI
    this.Title = "OneRoom Health Kiosk";
    HideStatus();
}
```

**Step 4: Add Configuration Support**
```csharp
// New configuration class
public class KioskConfiguration
{
    public bool AllowDebugMode { get; set; } = false;
    public string DebugModePassword { get; set; } = "";
    public Keys DebugHotkey { get; set; } = Keys.Control | Keys.Shift | Keys.F12;
    public Keys ExitHotkey { get; set; } = Keys.Control | Keys.Shift | Keys.Escape;
}
```

---

## Feature 2: Exit Strategy

### Overview
Provide a secure mechanism for administrators to exit kiosk mode and access the normal Windows desktop.

### Design Specifications

#### 2.1 Exit Mechanisms

**Option A: Password-Protected Exit**
- Hotkey: `Ctrl + Shift + Escape`
- Shows password dialog
- Requires admin password
- Exits application after validation

**Option B: Key Sequence Exit**
- Complex sequence: `Ctrl+Alt+X` followed by `Ctrl+Alt+Q` within 2 seconds
- No UI shown until sequence complete
- More secure against accidental activation

**Option C: Time-Based Hold**
- Hold `Ctrl + Alt + End` for 5 seconds
- Shows countdown overlay
- Can be cancelled by releasing keys

#### 2.2 Recommended Implementation: Password-Protected Exit

**Step 1: Add Exit Hotkey Handler**
```csharp
private async void OnKeyDown(CoreWindow sender, KeyEventArgs args)
{
    var ctrl = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control);
    var shift = Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift);

    // Debug mode hotkey
    if (ctrl.HasFlag(CoreVirtualKeyStates.Down) &&
        shift.HasFlag(CoreVirtualKeyStates.Down) &&
        args.VirtualKey == VirtualKey.F12)
    {
        await ToggleDebugMode();
        args.Handled = true;
    }

    // Exit hotkey
    if (ctrl.HasFlag(CoreVirtualKeyStates.Down) &&
        shift.HasFlag(CoreVirtualKeyStates.Down) &&
        args.VirtualKey == VirtualKey.Escape)
    {
        await HandleExitRequest();
        args.Handled = true;
    }
}
```

**Step 2: Implement Password Dialog**
```csharp
private async Task HandleExitRequest()
{
    Logger.Log("Exit request initiated");

    // Create password dialog
    var dialog = new ContentDialog
    {
        Title = "Exit Kiosk Mode",
        Content = new PasswordBox
        {
            PlaceholderText = "Enter administrator password",
            Width = 300
        },
        PrimaryButtonText = "Exit",
        CloseButtonText = "Cancel",
        DefaultButton = ContentDialogButton.Close,
        XamlRoot = this.Content.XamlRoot
    };

    var result = await dialog.ShowAsync();

    if (result == ContentDialogResult.Primary)
    {
        var passwordBox = (PasswordBox)dialog.Content;
        if (ValidateExitPassword(passwordBox.Password))
        {
            await PerformKioskExit();
        }
        else
        {
            await ShowInvalidPasswordMessage();
        }
    }
}

private bool ValidateExitPassword(string password)
{
    // Load from secure configuration
    var config = LoadConfiguration();

    // In production, use secure hash comparison
    var hashedInput = HashPassword(password);
    return hashedInput == config.ExitPasswordHash;
}

private async Task PerformKioskExit()
{
    Logger.Log("Performing kiosk exit");

    try
    {
        // 1. Show exit message
        ShowStatus("EXITING", "Shutting down kiosk mode...");

        // 2. Clean up WebView2
        if (WebView2Control != null)
        {
            WebView2Control.Close();
        }

        // 3. Stop HTTP server
        LocalCommandServer.Stop();

        // 4. Log exit
        Logger.Log("Kiosk application exiting normally");

        // 5. For Shell Launcher v2, we need to trigger Explorer
        if (IsRunningInKioskMode())
        {
            // Start Explorer.exe for the current user
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = true
            });
        }

        // 6. Close application
        Application.Current.Exit();
    }
    catch (Exception ex)
    {
        Logger.Log($"Error during exit: {ex.Message}");
        // Force exit
        Environment.Exit(0);
    }
}
```

**Step 3: Shell Launcher Considerations**
```csharp
private bool IsRunningInKioskMode()
{
    // Check if we're running as the shell
    try
    {
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Winlogon"))
        {
            var shell = key?.GetValue("Shell") as string;
            return shell?.Contains("OneRoomHealthKiosk") ?? false;
        }
    }
    catch
    {
        return false;
    }
}

private void RestoreNormalShell()
{
    // This requires admin rights and should be done via provisioning script
    // The app itself cannot modify Shell Launcher configuration
    Logger.Log("Note: Shell Launcher configuration must be modified by administrator");
}
```

---

## Feature 3: Configuration Management

### Overview
Implement external configuration file for managing debug and exit features.

### Configuration File Location
```
%ProgramData%\OneRoomHealth\Kiosk\config.json
```

### Configuration Schema
```json
{
  "kiosk": {
    "defaultUrl": "https://orh-frontend.azurecontainerapps.io/wall/default",
    "targetMonitorIndex": 1,
    "fullscreen": true,
    "alwaysOnTop": true
  },
  "debug": {
    "enabled": false,
    "hotkey": "Ctrl+Shift+F12",
    "requirePassword": true,
    "passwordHash": "SHA256_HASH_HERE",
    "autoOpenDevTools": false,
    "windowSizePercent": 80
  },
  "exit": {
    "enabled": false,
    "hotkey": "Ctrl+Shift+Escape",
    "requirePassword": true,
    "passwordHash": "SHA256_HASH_HERE",
    "allowedUsers": ["DOMAIN\\AdminGroup"],
    "timeout": 5000
  },
  "logging": {
    "enabled": true,
    "level": "Info",
    "path": "%LocalAppData%\\OneRoomHealthKiosk\\logs",
    "maxSizeKb": 10240,
    "maxFiles": 5
  },
  "httpApi": {
    "enabled": true,
    "port": 8787,
    "allowRemote": false
  }
}
```

### Configuration Loading
```csharp
public class ConfigurationManager
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "OneRoomHealth", "Kiosk", "config.json");

    public static KioskConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<KioskConfig>(json);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to load config: {ex.Message}");
        }

        // Return default configuration
        return new KioskConfig();
    }

    public static void Save(KioskConfig config)
    {
        try
        {
            var directory = Path.GetDirectoryName(ConfigPath);
            Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to save config: {ex.Message}");
        }
    }
}
```

---

## Implementation Timeline

### Phase 1: Core Infrastructure (Week 1)
- [ ] Add keyboard event handling to MainWindow
- [ ] Implement configuration file support
- [ ] Add secure password hashing utilities
- [ ] Update Logger class for enhanced debugging

### Phase 2: Debug Mode (Week 2)
- [ ] Implement debug mode toggle logic
- [ ] Add window state management (fullscreen ↔ windowed)
- [ ] Enable/disable WebView2 developer tools dynamically
- [ ] Add debug mode UI indicators
- [ ] Test with various display configurations

### Phase 3: Exit Strategy (Week 3)
- [ ] Implement password-protected exit
- [ ] Add exit confirmation dialog
- [ ] Handle Shell Launcher considerations
- [ ] Test exit mechanism in kiosk environment
- [ ] Document administrator procedures

### Phase 4: Testing & Documentation (Week 4)
- [ ] Comprehensive testing in kiosk mode
- [ ] Security audit of new features
- [ ] Update deployment documentation
- [ ] Create administrator guide
- [ ] Training materials for IT staff

---

## Security Considerations

### 1. Password Security
- Store only hashed passwords (SHA256 or better)
- Use secure comparison to prevent timing attacks
- Implement rate limiting for password attempts
- Log all exit/debug attempts

### 2. Hotkey Security
- Use complex key combinations
- Avoid common shortcuts
- Make hotkeys configurable
- Consider disabling in production

### 3. Audit Trail
```csharp
public class AuditLogger
{
    public static void LogSecurityEvent(string eventType, string details)
    {
        var entry = new
        {
            Timestamp = DateTime.UtcNow,
            EventType = eventType,
            User = Environment.UserName,
            Machine = Environment.MachineName,
            Details = details
        };

        // Log to Windows Event Log
        EventLog.WriteEntry("OneRoomHealthKiosk",
            JsonSerializer.Serialize(entry),
            EventLogEntryType.Information,
            1000);

        // Also log to file
        Logger.Log($"AUDIT: {JsonSerializer.Serialize(entry)}");
    }
}
```

### 4. Configuration Security
- Store config in ProgramData (requires admin to modify)
- Validate all configuration on load
- Use Windows file permissions to protect config
- Encrypt sensitive values if needed

---

## Testing Strategy

### 1. Unit Tests
```csharp
[TestMethod]
public void TestDebugModeToggle()
{
    // Test debug mode can be enabled
    // Test window state changes correctly
    // Test WebView2 settings are modified
    // Test debug mode can be disabled
}

[TestMethod]
public void TestExitMechanism()
{
    // Test password validation
    // Test exit process
    // Test Shell Launcher handling
}
```

### 2. Integration Tests
- Test in actual kiosk environment
- Test with Shell Launcher v2 active
- Test monitor detection and positioning
- Test WebView2 developer tools access
- Test configuration loading/saving

### 3. Security Tests
- Attempt bypass of password protection
- Test hotkey combinations
- Verify audit logging
- Check for privilege escalation

### 4. User Acceptance Tests
- IT administrator workflow
- Developer debugging workflow
- Emergency exit procedures
- Configuration management

---

## Deployment Considerations

### 1. Version Management
- Increment version to 1.1.0 for these features
- Update Package.appxmanifest
- Document breaking changes

### 2. Configuration Deployment
```powershell
# Deploy default configuration
$configPath = "$env:ProgramData\OneRoomHealth\Kiosk"
New-Item -Path $configPath -ItemType Directory -Force

# Copy configuration template
Copy-Item "config.template.json" "$configPath\config.json"

# Set permissions (admins only)
$acl = Get-Acl $configPath
$acl.SetAccessRuleProtection($true, $false)
$adminRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    "BUILTIN\Administrators", "FullControl", "Allow")
$acl.SetAccessRule($adminRule)
Set-Acl -Path $configPath -AclObject $acl
```

### 3. Update Provisioning Script
```powershell
# provision_kiosk_user.ps1 additions
param(
    [switch]$EnableDebugMode = $false,
    [switch]$EnableExitMechanism = $false,
    [string]$DebugPassword = "",
    [string]$ExitPassword = ""
)

# Configure debug/exit features
if ($EnableDebugMode -or $EnableExitMechanism) {
    $config = @{
        debug = @{
            enabled = $EnableDebugMode
            passwordHash = if ($DebugPassword) {
                Get-PasswordHash $DebugPassword
            } else { "" }
        }
        exit = @{
            enabled = $EnableExitMechanism
            passwordHash = if ($ExitPassword) {
                Get-PasswordHash $ExitPassword
            } else { "" }
        }
    }

    $configPath = "$env:ProgramData\OneRoomHealth\Kiosk\config.json"
    $config | ConvertTo-Json -Depth 10 | Set-Content $configPath
}
```

---

## Documentation Updates

### 1. Update README.md
- Document new keyboard shortcuts
- Explain debug mode features
- Describe exit mechanisms
- Add security warnings

### 2. Create ADMIN_GUIDE.md
- How to configure debug mode
- How to set exit passwords
- Troubleshooting procedures
- Security best practices

### 3. Update DEPLOYMENT_GUIDE.md
- New configuration requirements
- Password management
- Hotkey customization
- Audit log locations

---

## Risk Assessment

### Risks
1. **Unauthorized Access**: Debug mode could expose system
   - **Mitigation**: Password protection, audit logging

2. **Accidental Activation**: User triggers debug/exit
   - **Mitigation**: Complex key combinations, confirmation dialogs

3. **Configuration Tampering**: Modified config file
   - **Mitigation**: File permissions, hash validation

4. **Shell Launcher Bypass**: Exit doesn't restore shell
   - **Mitigation**: Proper Explorer.exe launch, admin procedures

### Benefits
1. **Improved Maintainability**: Easier debugging and troubleshooting
2. **Emergency Access**: IT can exit kiosk when needed
3. **Developer Productivity**: Access to dev tools
4. **Flexibility**: Configurable behavior

---

## Conclusion

This implementation plan provides a comprehensive approach to adding debug mode and exit strategy features to the OneRoom Health WinUI Kiosk application. The design balances security with usability, ensuring that administrators and developers have the tools they need while maintaining the kiosk's primary security purpose.

Key deliverables:
- ✅ Secure debug mode with windowing capability
- ✅ Password-protected exit mechanism
- ✅ External configuration support
- ✅ Comprehensive audit logging
- ✅ Full documentation and testing

The implementation follows Windows development best practices and maintains compatibility with Shell Launcher v2 while providing the flexibility needed for maintenance and emergency access.