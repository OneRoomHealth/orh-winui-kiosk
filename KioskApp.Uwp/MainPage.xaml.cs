using System;
using System.Collections.Generic;
using Windows.ApplicationModel.Core;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace KioskApp.Uwp
{
    public sealed partial class MainPage : Page
    {
        private readonly string KioskUrl = App.KioskUrl;
        private readonly DispatcherTimer _tapTimer;
        private readonly List<DateTimeOffset> _tapTimestamps = new List<DateTimeOffset>();
        private const int TAP_COUNT_REQUIRED = 5;
        private const int TAP_WINDOW_SECONDS = 7;
        private int _failedPinAttempts = 0;
        private DateTimeOffset _lastFailedAttempt = DateTimeOffset.MinValue;

        public MainPage()
        {
            this.InitializeComponent();
            
            // Initialize tap timer for exit gesture
            _tapTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _tapTimer.Tick += TapTimer_Tick;

            this.Loaded += MainPage_Loaded;
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Enter full screen mode
            var view = ApplicationView.GetForCurrentView();
            view.TryEnterFullScreenMode();

            // Hide system UI
            CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;

            // Block keyboard shortcuts
            Window.Current.CoreWindow.Dispatcher.AcceleratorKeyActivated += Dispatcher_AcceleratorKeyActivated;

            // Initialize WebView2
            await InitializeWebView2Async();
        }

        private async System.Threading.Tasks.Task InitializeWebView2Async()
        {
            try
            {
                await RootWebView.EnsureCoreWebView2Async();

                if (RootWebView.CoreWebView2 != null)
                {
                    // Disable context menu
                    var settings = RootWebView.CoreWebView2.Settings;
                    settings.AreDefaultContextMenusEnabled = false;
                    settings.AreDevToolsEnabled = false;
                    settings.IsStatusBarEnabled = false;
                    settings.AreDefaultScriptDialogsEnabled = true; // Allow alerts, confirms, etc.

                    // Handle navigation errors
                    RootWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

                    // Set source
                    RootWebView.Source = new Uri(KioskUrl);

                    // Keep focus on WebView
                    RootWebView.Focus(FocusState.Programmatic);
                }
            }
            catch (Exception ex)
            {
                // Show offline page on WebView2 initialization failure
                ShowOfflinePage($"Failed to initialize browser: {ex.Message}");
            }
        }

        private void CoreWebView2_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (!args.IsSuccess)
            {
                // Show offline page on navigation failure
                var errorStatus = args.WebErrorStatus;
                ShowOfflinePage($"Navigation failed: {errorStatus}");
            }
            else
            {
                // Hide offline page if it was showing
                HideOfflinePage();
            }
        }

        private void ShowOfflinePage(string errorMessage)
        {
            RootWebView.Visibility = Visibility.Collapsed;
            OfflineFrame.Visibility = Visibility.Visible;
            OfflineFrame.Navigate(typeof(OfflinePage), errorMessage);
        }

        private void HideOfflinePage()
        {
            if (OfflineFrame.Visibility == Visibility.Visible)
            {
                OfflineFrame.Visibility = Visibility.Collapsed;
                RootWebView.Visibility = Visibility.Visible;
            }
        }

        private void TapHotspot_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var now = DateTimeOffset.Now;

            // Remove old taps outside the time window
            _tapTimestamps.RemoveAll(t => (now - t).TotalSeconds > TAP_WINDOW_SECONDS);

            // Add current tap
            _tapTimestamps.Add(now);

            // Check if we have enough taps
            if (_tapTimestamps.Count >= TAP_COUNT_REQUIRED)
            {
                _tapTimestamps.Clear();
                ShowPinDialogAsync();
            }

            // Start or restart the timer to clean up old taps
            _tapTimer.Stop();
            _tapTimer.Start();
        }

        private void TapTimer_Tick(object sender, object e)
        {
            var now = DateTimeOffset.Now;
            _tapTimestamps.RemoveAll(t => (now - t).TotalSeconds > TAP_WINDOW_SECONDS);

            if (_tapTimestamps.Count == 0)
            {
                _tapTimer.Stop();
            }
        }

        private async void ShowPinDialogAsync()
        {
            // Check if we're in exponential backoff period
            var timeSinceLastFailure = DateTimeOffset.Now - _lastFailedAttempt;
            if (_failedPinAttempts > 0)
            {
                var requiredWaitTime = TimeSpan.FromSeconds(Math.Pow(2, _failedPinAttempts - 1) * 5); // 5s, 10s, 20s, 40s...
                if (timeSinceLastFailure < requiredWaitTime)
                {
                    var remainingSeconds = (int)(requiredWaitTime - timeSinceLastFailure).TotalSeconds;
                    var errorDialog = new ContentDialog
                    {
                        Title = "Too Many Failed Attempts",
                        Content = $"Please wait {remainingSeconds} seconds before trying again.",
                        CloseButtonText = "OK",
                        XamlRoot = this.Content.XamlRoot
                    };
                    await errorDialog.ShowAsync();
                    return;
                }
            }

            var dialog = new PinDialog();
            dialog.XamlRoot = this.Content.XamlRoot;
            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                var enteredPin = dialog.EnteredPin;
                if (enteredPin == App.ExitPin)
                {
                    // Correct PIN - exit application
                    _failedPinAttempts = 0;
                    Application.Current.Exit();
                }
                else
                {
                    // Wrong PIN - increment failure count and set backoff
                    _failedPinAttempts++;
                    _lastFailedAttempt = DateTimeOffset.Now;

                    var errorDialog = new ContentDialog
                    {
                        Title = "Incorrect PIN",
                        Content = $"The PIN you entered is incorrect. (Attempt {_failedPinAttempts})",
                        CloseButtonText = "OK",
                        XamlRoot = this.Content.XamlRoot
                    };
                    await errorDialog.ShowAsync();
                }
            }
        }

        private void Dispatcher_AcceleratorKeyActivated(CoreDispatcher sender, AcceleratorKeyEventArgs args)
        {
            // Block common keyboard shortcuts
            var virtualKey = args.VirtualKey;

            // Block Alt+F4
            if (virtualKey == VirtualKey.F4 && 
                (Window.Current.CoreWindow.GetKeyState(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down)))
            {
                args.Handled = true;
                return;
            }

            // Block Windows key
            if (virtualKey == VirtualKey.LeftWindows || virtualKey == VirtualKey.RightWindows)
            {
                args.Handled = true;
                return;
            }

            // Block Alt+Tab
            if (virtualKey == VirtualKey.Tab && 
                (Window.Current.CoreWindow.GetKeyState(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down)))
            {
                args.Handled = true;
                return;
            }

            // Block Ctrl+Alt+Del (can't be fully blocked in UWP)
            // Block Ctrl+Shift+Esc (Task Manager)
            if (virtualKey == VirtualKey.Escape && 
                (Window.Current.CoreWindow.GetKeyState(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down) ||
                 Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down)))
            {
                args.Handled = true;
                return;
            }

            // Block F11 (fullscreen toggle in browsers)
            if (virtualKey == VirtualKey.F11)
            {
                args.Handled = true;
                return;
            }

            // Refocus on WebView if focus is lost
            if (RootWebView.Visibility == Visibility.Visible)
            {
                RootWebView.Focus(FocusState.Programmatic);
            }
        }
    }
}
