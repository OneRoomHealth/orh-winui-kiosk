# WebView2 Enhancement Plan for Kiosk Application

## Executive Summary

This plan addresses critical discrepancies between the current WinUI3 WebView2 implementation and the Chromium kiosk configuration, focusing on automatic media permissions, autoplay policies, and performance optimizations essential for a seamless kiosk experience.

---

## Priority 1: Critical Media Functionality (Must Have)

### 1.1 Automatic Media Permission Approval

**Issue:** Users are prompted for microphone/camera access, breaking the kiosk experience.

**Implementation Steps:**

1. Add `PermissionRequested` event handler in `MainWindow.xaml.cs`:
```csharp
// In InitializeWebViewAsync() after settings configuration
KioskWebView.CoreWebView2.PermissionRequested += CoreWebView2_PermissionRequested;
```

2. Create the event handler:
```csharp
private void CoreWebView2_PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
{
    // Auto-approve critical media permissions
    switch (e.PermissionKind)
    {
        case CoreWebView2PermissionKind.Microphone:
        case CoreWebView2PermissionKind.Camera:
        case CoreWebView2PermissionKind.CameraAndMicrophone:
            e.State = CoreWebView2PermissionState.Allow;
            Logger.Log($"Auto-approved permission: {e.PermissionKind} for {e.Uri}");
            break;
        
        case CoreWebView2PermissionKind.Notifications:
        case CoreWebView2PermissionKind.Geolocation:
        case CoreWebView2PermissionKind.OtherSensors:
            // Optionally auto-approve these as well
            e.State = CoreWebView2PermissionState.Allow;
            break;
        
        default:
            // Deny everything else by default
            e.State = CoreWebView2PermissionState.Deny;
            Logger.Log($"Denied permission: {e.PermissionKind} for {e.Uri}");
            break;
    }
    
    // Persist the decision (optional - requires handling)
    e.SavesInProfile = true;
}
```

### 1.2 Autoplay Policy Configuration

**Issue:** Media may not play automatically without user interaction.

**Implementation Steps:**

1. Add autoplay configuration in `InitializeWebViewAsync()`:
```csharp
// After basic settings configuration
settings.IsPasswordAutosaveEnabled = false;
settings.IsGeneralAutofillEnabled = false;

// Add media autoplay settings
await KioskWebView.CoreWebView2.ExecuteScriptAsync(@"
    // Override autoplay policy at the JavaScript level
    Object.defineProperty(navigator, 'userActivation', {
        get: () => ({
            hasBeenActive: true,
            isActive: true
        })
    });
");
```

2. Configure additional media settings via environment options (before WebView2 initialization):
```csharp
private async Task<CoreWebView2Environment> CreateWebView2EnvironmentAsync()
{
    var options = CoreWebView2EnvironmentOptions.CreateDefault();
    
    // Enable media autoplay without user gesture
    options.AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required " +
                                        "--disable-features=PreloadMediaEngagementData,MediaEngagementBypassAutoplayPolicies";
    
    var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                                     "OneRoomHealthKiosk", "WebView2Data");
    
    return await CoreWebView2Environment.CreateAsync(
        browserExecutableFolder: null,
        userDataFolder: userDataFolder,
        options: options);
}
```

---

## Priority 2: Enhanced Security & Performance (Should Have)

### 2.1 Security Relaxation for Kiosk Environment

**Implementation Steps:**

1. Expand environment options with security flags:
```csharp
options.AdditionalBrowserArguments += 
    " --disable-web-security" +
    " --allow-running-insecure-content" +
    " --disable-site-isolation-trials" +
    " --disable-features=IsolateOrigins,site-per-process";
```

2. Handle navigation errors gracefully:
```csharp
KioskWebView.CoreWebView2.NavigationCompleted += async (_, args) =>
{
    if (!args.IsSuccess && args.WebErrorStatus == CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect)
    {
        // Log and potentially retry or redirect
        Logger.Log($"Certificate error ignored for kiosk mode");
    }
};
```

### 2.2 Performance Optimizations

**Implementation Steps:**

1. Add performance-related browser arguments:
```csharp
options.AdditionalBrowserArguments += 
    " --disable-background-timer-throttling" +
    " --disable-renderer-backgrounding" +
    " --disable-features=TranslateUI" +
    " --disable-ipc-flooding-protection" +
    " --enable-features=OverlayScrollbar" +
    " --force-device-scale-factor=1.0";
```

2. Implement memory management for long-running sessions:
```csharp
private async Task PeriodicMaintenanceAsync()
{
    while (true)
    {
        await Task.Delay(TimeSpan.FromHours(6));
        
        try
        {
            // Clear cache periodically to prevent memory bloat
            await KioskWebView.CoreWebView2.ClearCacheAsync();
            Logger.Log("Performed periodic cache cleanup");
        }
        catch (Exception ex)
        {
            Logger.Log($"Maintenance error: {ex.Message}");
        }
    }
}
```

---

## Priority 3: Additional Browser Features (Nice to Have)

### 3.1 Download Handling

```csharp
KioskWebView.CoreWebView2.DownloadStarting += (_, e) =>
{
    // Cancel all downloads in kiosk mode
    e.Cancel = true;
    Logger.Log($"Download blocked: {e.ResultFilePath}");
};
```

### 3.2 New Window Handling

```csharp
KioskWebView.CoreWebView2.NewWindowRequested += (_, e) =>
{
    // Navigate in same window instead of opening new windows
    e.Handled = true;
    KioskWebView.CoreWebView2.Navigate(e.Uri);
    Logger.Log($"Redirected new window request to main window: {e.Uri}");
};
```

### 3.3 Context Menu Customization

```csharp
KioskWebView.CoreWebView2.ContextMenuRequested += (_, e) =>
{
    // Remove all context menu items for kiosk mode
    e.MenuItems.Clear();
    Logger.Log("Context menu suppressed");
};
```

---

## Implementation Timeline

### Phase 1: Critical Features (Week 1)
- [ ] Implement automatic media permission approval
- [ ] Configure autoplay policy
- [ ] Test with target web application

### Phase 2: Security & Performance (Week 2)
- [ ] Add security relaxation flags
- [ ] Implement performance optimizations
- [ ] Add periodic maintenance tasks

### Phase 3: Polish & Testing (Week 3)
- [ ] Add download blocking
- [ ] Handle new window requests
- [ ] Comprehensive testing on target hardware
- [ ] Document any WebView2 limitations vs Chromium

---

## Testing Checklist

### Media Functionality
- [ ] Microphone access works without prompts
- [ ] Camera access works without prompts
- [ ] Audio plays automatically on page load
- [ ] Video plays automatically on page load

### Performance
- [ ] App runs stable for 24+ hours
- [ ] Memory usage remains stable
- [ ] No UI freezes or hangs

### Security & UX
- [ ] Cross-origin requests work as expected
- [ ] No unexpected dialogs or prompts appear
- [ ] Navigation controls work correctly

---

## Known Limitations

Some Chromium features cannot be replicated in WebView2:
- Chrome DevTools Protocol (replaced with HTTP API)
- Some advanced GPU optimization flags
- Direct control over process sandboxing
- Native OS-level password manager bypass

---

## Configuration File Proposal

Consider externalizing configuration for easier deployment:

```json
{
  "webview": {
    "defaultUrl": "https://orh-frontend-dev-container.politebeach-927fe169.westus2.azurecontainerapps.io/wall/default",
    "autoApprovePermissions": ["microphone", "camera", "notifications"],
    "enableAutoplay": true,
    "disableSecurityForKiosk": true,
    "cacheClearIntervalHours": 6
  },
  "display": {
    "targetMonitorIndex": 1,
    "forceFullscreen": true,
    "hideScrollbars": true
  }
}
```

---

## Next Steps

1. Review and approve this plan
2. Create a feature branch for implementation
3. Implement Phase 1 critical features
4. Test with actual kiosk hardware
5. Iterate based on testing results

---

**Document Version:** 1.0  
**Created:** November 2025  
**Author:** Development Team  
**Status:** Pending Review
