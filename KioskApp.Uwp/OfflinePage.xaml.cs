using System;
using Windows.Networking.Connectivity;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace KioskApp.Uwp
{
    public sealed partial class OfflinePage : Page
    {
        private readonly DispatcherTimer _retryTimer;
        private int _countdown = 30;
        private string _errorMessage;

        public OfflinePage()
        {
            this.InitializeComponent();

            _retryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _retryTimer.Tick += RetryTimer_Tick;

            // Monitor network status changes
            NetworkInformation.NetworkStatusChanged += NetworkInformation_NetworkStatusChanged;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            _errorMessage = e.Parameter as string ?? "Unable to connect to the network.";
            ErrorMessageText.Text = _errorMessage;

            StartRetryCountdown();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            _retryTimer.Stop();
            NetworkInformation.NetworkStatusChanged -= NetworkInformation_NetworkStatusChanged;
        }

        private void StartRetryCountdown()
        {
            _countdown = 30;
            UpdateCountdownText();
            _retryTimer.Start();
        }

        private void RetryTimer_Tick(object sender, object e)
        {
            _countdown--;
            
            if (_countdown <= 0)
            {
                _retryTimer.Stop();
                AttemptReconnect();
            }
            else
            {
                UpdateCountdownText();
            }
        }

        private void UpdateCountdownText()
        {
            CountdownText.Text = $"Automatically retrying in {_countdown} seconds...";
        }

        private async void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            _retryTimer.Stop();
            RetryButton.IsEnabled = false;
            StatusText.Text = "Attempting to reconnect...";
            
            await System.Threading.Tasks.Task.Delay(1000);
            
            AttemptReconnect();
        }

        private void NetworkInformation_NetworkStatusChanged(object sender)
        {
            // Auto-reconnect when network becomes available
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                await System.Threading.Tasks.Task.Delay(2000); // Give network a moment to stabilize
                
                if (IsNetworkAvailable())
                {
                    StatusText.Text = "Network detected! Reconnecting...";
                    await System.Threading.Tasks.Task.Delay(1000);
                    AttemptReconnect();
                }
            });
        }

        private void AttemptReconnect()
        {
            if (IsNetworkAvailable())
            {
                // Navigate back to MainPage (which will reload the WebView)
                if (Frame.CanGoBack)
                {
                    Frame.GoBack();
                }
                else
                {
                    Frame.Navigate(typeof(MainPage));
                }
            }
            else
            {
                // Still offline, restart countdown
                StatusText.Text = "Still offline. Will retry automatically.";
                RetryButton.IsEnabled = true;
                StartRetryCountdown();
            }
        }

        private bool IsNetworkAvailable()
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            return profile != null && 
                   profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
        }
    }
}
