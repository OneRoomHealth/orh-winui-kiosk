using Microsoft.UI.Xaml;

namespace KioskApp;

public partial class App : Application
{
    private Window? m_window;
    private Task? _serverTask;

    public App()
    {
        this.InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        m_window = new MainWindow();
        m_window.Activate();

        // Start in-process localhost command server without blocking UI thread
        if (m_window is MainWindow mainWindow)
        {
            _serverTask = LocalCommandServer.StartAsync(mainWindow);
        }
    }
}

