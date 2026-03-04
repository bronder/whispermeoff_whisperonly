using System;
using System.Windows;

namespace whisperMeOff;

public partial class App : System.Windows.Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        
        var mainWindow = new WhisperOnlyWindow();
        mainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        mainWindow.Closed += (s, ev) => Shutdown();
        mainWindow.Show();
    }
}

