using System;
using System.IO;
using System.Windows;
using System.Runtime.InteropServices;

namespace whisperMeOff;

public partial class WhisperOnlyWindow : Window
{
    private SpeechRecognitionService? _speechService;
    private bool _isRecording;
    private AppSettings _settings = new();
    
    // Global hotkey constants
    private const int HOTKEY_ID = 9000;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint VK_R = 0x52; // R key
    
    // For sending Ctrl+V paste
    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    private const byte VK_CONTROL = 0x11;
    private const byte VK_V = 0x56;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public WhisperOnlyWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();
        PreviewKeyDown += WhisperOnlyWindow_PreviewKeyDown;
        Loaded += WhisperOnlyWindow_Loaded;
        Closing += WhisperOnlyWindow_Closing;
    }

    private void WhisperOnlyWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Register Ctrl+Shift+R as global hotkey
        var helper = new System.Windows.Interop.WindowInteropHelper(this);
        RegisterHotKey(helper.Handle, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_R);
    }

    private void WhisperOnlyWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Unregister hotkey
        var helper = new System.Windows.Interop.WindowInteropHelper(this);
        UnregisterHotKey(helper.Handle, HOTKEY_ID);
    }
    
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var helper = new System.Windows.Interop.WindowInteropHelper(this);
        var source = System.Windows.Interop.HwndSource.FromHwnd(helper.Handle);
        source?.AddHook(WndProc);
    }
    
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // WM_HOTKEY = 0x0312
        if (msg == 0x0312 && wParam.ToInt32() == HOTKEY_ID)
        {
            handled = true;
            MicButton_Click(this, new RoutedEventArgs());
        }
        return IntPtr.Zero;
    }

    private void WhisperOnlyWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.F5)
        {
            e.Handled = true;
            MicButton_Click(this, new RoutedEventArgs());
        }
    }

    private void SpeechService_AudioLevelChanged(object? sender, int level)
    {
        // AudioLevel is 0-100, -1 means no audio
        Dispatcher.Invoke(() =>
        {
            VuMeter.Value = level >= 0 ? level : 0;
        });
    }

    private async void MicButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRecording)
        {
            _isRecording = false;
            MicButton.Content = "MIC";
            StatusText.Text = "Processing...";
            VuMeter.Value = 0;
            _speechService!.AudioLevelChanged -= SpeechService_AudioLevelChanged;
            _speechService?.StopRecognition();
            return;
        }

        _isRecording = true;
        MicButton.Content = "STOP";
        StatusText.Text = "Listening...";
        VuMeter.Value = 0;

        try
        {
            _speechService = new SpeechRecognitionService();
            _speechService.AudioLevelChanged += SpeechService_AudioLevelChanged;
            
            int deviceIndex = -1;
            var savedDevice = _settings.SelectedMicrophoneDevice;
            if (!string.IsNullOrEmpty(savedDevice) && savedDevice.Contains(":"))
            {
                var parts = savedDevice.Split(':');
                if (parts.Length > 0 && int.TryParse(parts[0].Trim(), out int parsedIndex))
                    deviceIndex = parsedIndex;
            }

            _speechService.TranslateWithWhisper = _settings.TranslationEnabled;
            _speechService.WhisperTranslationTarget = _settings.TranslationTargetLanguage;
            var modelFolder = _settings.WhisperModelFolder;
            var modelFile = _settings.WhisperModelFileName;
            if (string.IsNullOrEmpty(modelFolder))
                modelFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "whisperMeOff", "models");
            _speechService.LocalWhisperModelPath = Path.Combine(modelFolder, modelFile);

            StatusText.Text = "Transcribing...";
            var result = await _speechService.RecognizeSpeechWithWhisperAsync(deviceIndex);

            if (!string.IsNullOrEmpty(result) && !result.StartsWith("Speech recognition error") && !result.StartsWith("Whisper"))
            {
                try 
                { 
                    System.Windows.Clipboard.SetText(result.Trim()); 
                    StatusText.Text = "Copied to clipboard!";
                    
                    // Small delay then paste to the previously focused app
                    await Task.Delay(100);
                    
                    // Send Ctrl+V using keybd_event
                    keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero); // Ctrl down
                    keybd_event(VK_V, 0, 0, UIntPtr.Zero);       // V down
                    keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // V up
                    keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // Ctrl up
                    
                    StatusText.Text = "Pasted!";
                }
                catch (Exception ex) { StatusText.Text = $"Clipboard error: {ex.Message}"; }
            }
            else if (result.StartsWith("Speech recognition error") || result.StartsWith("Whisper"))
                StatusText.Text = result;
            else
                StatusText.Text = "No speech detected";
        }
        catch (Exception ex) { StatusText.Text = $"Error: {ex.Message}"; }
        finally
        {
            _isRecording = false;
            MicButton.Content = "MIC";
            VuMeter.Value = 0;
            if (_speechService != null)
            {
                _speechService.AudioLevelChanged -= SpeechService_AudioLevelChanged;
            }
            if (StatusText.Text == "Listening..." || StatusText.Text == "Processing..." || StatusText.Text == "Transcribing...")
                StatusText.Text = "Ready";
        }
    }
}
