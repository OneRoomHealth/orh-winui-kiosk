using Microsoft.UI.Xaml.Controls;

namespace KioskApp;

public sealed partial class PinDialog : ContentDialog
{
    private const string DEFAULT_PIN = "1234";
    private int _attemptCount = 0;
    private const int MAX_ATTEMPTS = 3;

    public PinDialog()
    {
        this.InitializeComponent();
        this.Loaded += PinDialog_Loaded;
    }

    private void PinDialog_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        // Focus the PIN input when dialog opens
        PinInput.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Get a deferral to prevent the dialog from closing immediately
        var deferral = args.GetDeferral();

        var enteredPin = PinInput.Password;

        // TODO: For production, consider reading PIN from secure configuration
        // or environment variable instead of hard-coding
        if (enteredPin == DEFAULT_PIN)
        {
            // PIN is correct, allow dialog to close with Primary result
            ErrorMessage.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            deferral.Complete();
        }
        else
        {
            // PIN is incorrect
            _attemptCount++;
            ErrorMessage.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            PinInput.Password = string.Empty;
            PinInput.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);

            if (_attemptCount >= MAX_ATTEMPTS)
            {
                // After max attempts, close the dialog
                ErrorMessage.Text = "Maximum attempts exceeded. Closing dialog.";
                args.Cancel = false; // Allow dialog to close
            }
            else
            {
                // Prevent dialog from closing
                args.Cancel = true;
            }

            deferral.Complete();
        }
    }
}

