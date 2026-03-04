using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace whisperMeOff;

public partial class App : System.Windows.Application
{
    private NotifyIcon? _notifyIcon;
    private WhisperOnlyWindow? _mainWindow;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        
        // Create tray icon
        _notifyIcon = new NotifyIcon
        {
            Text = "whisperMeOff",
            Visible = true,
            Icon = CreateRobotIcon()
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

    private Icon CreateRobotIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        
        // Background - blue
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(0, 120, 212));
        
        // Robot head - white
        var headBrush = new SolidBrush(Color.White);
        g.FillRectangle(headBrush, 6, 4, 20, 18);
        
        // Eyes
        g.FillEllipse(new SolidBrush(Color.FromArgb(0, 120, 212)), 10, 9, 4, 4);
        g.FillEllipse(new SolidBrush(Color.FromArgb(0, 120, 212)), 18, 9, 4, 4);
        
        // Mouth
        g.DrawLine(new Pen(Color.FromArgb(0, 120, 212), 2), 11, 17, 21, 17);
        
        // Antenna
        g.DrawLine(new Pen(Color.White, 2), 16, 4, 16, 1);
        g.FillEllipse(headBrush, 14, 0, 4, 3);
        
        return Icon.FromHandle(bitmap.GetHicon());
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

