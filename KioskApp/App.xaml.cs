using Microsoft.UI.Xaml;
using System.Diagnostics;

namespace KioskApp;

public partial class App : Application
{
    private Window? m_window;
    private Task? _serverTask;

    public App()
    {
        this.InitializeComponent();
        
        // Catch unhandled exceptions to prevent silent crashes
        this.UnhandledException += App_UnhandledException;
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Log the exception
        Debug.WriteLine($"UNHANDLED EXCEPTION: {e.Exception.Message}");
        Debug.WriteLine($"Stack Trace: {e.Exception.StackTrace}");
        
        // Try to write to event log as well
        try
        {
            System.Diagnostics.EventLog.WriteEntry("Application", 
                $"OneRoom Health Kiosk App Error: {e.Exception.Message}\n\n{e.Exception.StackTrace}", 
                System.Diagnostics.EventLogEntryType.Error);
        }
        catch { /* Ignore if event log write fails */ }
        
        // Mark as handled to prevent app crash (for debugging)
        // Remove this in production if you want the app to crash on errors
        e.Handled = true;
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            Debug.WriteLine("=== OneRoom Health Kiosk App Starting ===");
            
            m_window = new MainWindow();
            m_window.Activate();
            
            Debug.WriteLine("MainWindow created and activated");

            // Start in-process localhost command server without blocking UI thread
            if (m_window is MainWindow mainWindow)
            {
                _serverTask = LocalCommandServer.StartAsync(mainWindow);
                Debug.WriteLine("LocalCommandServer started");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LAUNCH ERROR: {ex.Message}");
            Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
            
            // Try to show error to user
            try
            {
                System.Windows.Forms.MessageBox.Show(
                    $"Failed to start Kiosk App:\n\n{ex.Message}\n\nCheck Event Viewer for details.",
                    "Kiosk App Error",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
            catch { /* Ignore if MessageBox fails */ }
            
            throw;
        }
    }
}

