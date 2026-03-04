using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace whisperMeOff;

public partial class App : System.Windows.Application
{
    private NotifyIcon? _notifyIcon;
    private WhisperOnlyWindow? _mainWindow;
    private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "whisperMeOff";

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        
        // Create tray icon
        _notifyIcon = new NotifyIcon
        {
            Text = "whisperMeOff",
            Visible = true,
            Icon = LoadIconFromResource()
        };
        
        // Create context menu
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Show", null, (s, args) => ShowWindow());
        contextMenu.Items.Add("Settings", null, (s, args) => ShowSettings());
        contextMenu.Items.Add("-");
        contextMenu.Items.Add("Exit", null, (s, args) => ExitApp());
        _notifyIcon.ContextMenuStrip = contextMenu;
        
        _notifyIcon.DoubleClick += (s, args) => ShowWindow();
        
        // Create and show main window (hidden initially)
        _mainWindow = new WhisperOnlyWindow();
        _mainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _mainWindow.ShowInTaskbar = false;
        _mainWindow.WindowState = WindowState.Minimized;
        _mainWindow.Show();
        _mainWindow.Hide();
    }

    public static void SetStartWithWindows(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
            if (key != null)
            {
                if (enable)
                {
                    var exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue(AppName, $"\"{exePath}\"");
                    }
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error setting startup: {ex.Message}");
        }
    }

    public static bool GetStartWithWindows()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    private Icon LoadIconFromResource()
    {
        // Try to load flatrobot.ico from the application directory
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var iconPath = Path.Combine(exeDir, "flatrobot.ico");
        
        if (File.Exists(iconPath))
        {
            try
            {
                // Use native LoadImage to load the ICO file
                var hIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE);
                if (hIcon != IntPtr.Zero)
                {
                    return Icon.FromHandle(hIcon);
                }
            }
            catch
            {
                // Fall back to default icon if loading fails
            }
        }
        
        // Return default system icon if file not found
        return SystemIcons.Application;
    }

    private void ShowWindow()
    {
        if (_mainWindow != null)
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }
    }

    private void ShowSettings()
    {
        if (_mainWindow != null)
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
            // Call the Settings_Click method via reflection or by making it accessible
            _mainWindow.OpenSettings();
        }
    }

    private void ExitApp()
    {
        _notifyIcon?.Dispose();
        Shutdown();
    }
}

