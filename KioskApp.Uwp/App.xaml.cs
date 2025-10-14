using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace KioskApp.Uwp
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    sealed partial class App : Application
    {
        public static string KioskUrl { get; private set; } = "https://orh-frontend-container-prod.purplewave-6482a85c.westus2.azurecontainerapps.io/login";
        public static string ExitPin { get; private set; } = "7355608";

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            // Load configuration from kiosk.json or LocalSettings
            await LoadConfigurationAsync();

            Frame rootFrame = Window.Current.Content as Frame;

            // Do not repeat app initialization when the Window already has content,
            // just ensure that the window is active
            if (rootFrame == null)
            {
                // Create a Frame to act as the navigation context and navigate to the first page
                rootFrame = new Frame();

                rootFrame.NavigationFailed += OnNavigationFailed;

                if (e.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                    //TODO: Load state from previously suspended application
                }

                // Place the frame in the current Window
                Window.Current.Content = rootFrame;
            }

            if (e.PrelaunchActivated == false)
            {
                if (rootFrame.Content == null)
                {
                    // When the navigation stack isn't restored navigate to the first page,
                    // configuring the new page by passing required information as a navigation
                    // parameter
                    rootFrame.Navigate(typeof(MainPage), e.Arguments);
                }
                // Ensure the current window is active
                Window.Current.Activate();
            }
        }

        /// <summary>
        /// Load configuration from kiosk.json or LocalSettings
        /// </summary>
        private async System.Threading.Tasks.Task LoadConfigurationAsync()
        {
            try
            {
                // Try to load from kiosk.json in Assets folder
                var jsonFile = await Windows.ApplicationModel.Package.Current.InstalledLocation.GetFileAsync(@"Assets\kiosk.json");
                var jsonText = await FileIO.ReadTextAsync(jsonFile);
                
                // Parse JSON using proper JSON parser
                if (!string.IsNullOrWhiteSpace(jsonText))
                {
                    JsonObject jsonObject;
                    if (JsonObject.TryParse(jsonText, out jsonObject))
                    {
                        // Extract KioskUrl
                        if (jsonObject.ContainsKey("KioskUrl"))
                        {
                            var urlValue = jsonObject.GetNamedString("KioskUrl", null);
                            if (!string.IsNullOrWhiteSpace(urlValue))
                            {
                                KioskUrl = urlValue;
                            }
                        }

                        // Extract ExitPin
                        if (jsonObject.ContainsKey("ExitPin"))
                        {
                            var pinValue = jsonObject.GetNamedString("ExitPin", null);
                            if (!string.IsNullOrWhiteSpace(pinValue))
                            {
                                ExitPin = pinValue;
                            }
                        }
                    }
                }
            }
            catch
            {
                // If kiosk.json doesn't exist, try LocalSettings
                try
                {
                    var localSettings = ApplicationData.Current.LocalSettings;
                    
                    if (localSettings.Values.ContainsKey("KioskUrl"))
                    {
                        KioskUrl = localSettings.Values["KioskUrl"]?.ToString() ?? KioskUrl;
                    }
                    
                    if (localSettings.Values.ContainsKey("ExitPin"))
                    {
                        ExitPin = localSettings.Values["ExitPin"]?.ToString() ?? ExitPin;
                    }
                }
                catch
                {
                    // Use default values if both methods fail
                }
            }
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        /// <summary>
        /// Invoked when application execution is being suspended.  Application state is saved
        /// without knowing whether the application will be terminated or resumed with the contents
        /// of memory still intact.
        /// </summary>
        /// <param name="sender">The source of the suspend request.</param>
        /// <param name="e">Details about the suspend request.</param>
        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            //TODO: Save application state and stop any background activity
            deferral.Complete();
        }
    }
}
