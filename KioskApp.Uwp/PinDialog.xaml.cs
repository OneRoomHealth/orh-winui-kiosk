using Windows.System;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace KioskApp.Uwp
{
    public sealed partial class PinDialog : ContentDialog
    {
        public string EnteredPin { get; private set; }

        public PinDialog()
        {
            this.InitializeComponent();
            this.Loaded += (s, e) => PinPasswordBox.Focus(Windows.UI.Xaml.FocusState.Programmatic);
            this.PrimaryButtonClick += PinDialog_PrimaryButtonClick;
        }

        private void PinDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            EnteredPin = PinPasswordBox.Password;
            
            // Validation will be done by the caller
            // Just return the entered PIN
        }

        private void PinPasswordBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                // Don't call Hide() - let the DefaultButton="Primary" handle it
                // This ensures ContentDialogResult.Primary is returned for validation
                e.Handled = false; // Allow the event to bubble up to trigger Primary button
            }
        }
    }
}
