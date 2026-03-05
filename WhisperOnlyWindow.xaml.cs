using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Runtime.InteropServices;
using Button = System.Windows.Controls.Button;
using Cursors = System.Windows.Input.Cursors;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using TabControl = System.Windows.Controls.TabControl;
using TabItem = System.Windows.Controls.TabItem;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;
using StackPanel = System.Windows.Controls.StackPanel;
using Grid = System.Windows.Controls.Grid;
using Border = System.Windows.Controls.Border;
using Orientation = System.Windows.Controls.Orientation;
using VerticalAlignment = System.Windows.VerticalAlignment;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MessageBox = System.Windows.MessageBox;
using FontWeights = System.Windows.FontWeights;

namespace whisperMeOff;

public partial class WhisperOnlyWindow : Window
{
    private SpeechRecognitionService? _speechService;
    private bool _isRecording;
    private AppSettings _settings = new();
    private VuMeterWindow? _vuMeterWindow;
    
    // Global hotkey constants
    private const int HOTKEY_ID = 9000;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_WIN = 0x0008;
    
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
    
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

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
        // Reload settings in case they were changed
        _settings = AppSettings.Load();
        
        // Register global hotkey from settings (only if push-to-talk is disabled)
        if (!_settings.PushToTalk)
        {
            RegisterGlobalHotkey();
        }
        else
        {
            // Start push-to-talk timer to monitor hotkey
            StartPushToTalkTimer();
        }
    }

    private void RegisterGlobalHotkey()
    {
        var helper = new System.Windows.Interop.WindowInteropHelper(this);
        
        // Ensure window is loaded
        if (helper.Handle == IntPtr.Zero)
        {
            helper.EnsureHandle();
        }
        
        // Unregister existing hotkey first
        UnregisterHotKey(helper.Handle, HOTKEY_ID);
        
        // Skip registration if push-to-talk is enabled (keyboard hook handles it)
        if (_settings.PushToTalk)
        {
            return;
        }
        
        // Build modifiers from settings
        uint modifiers = 0;
        if (_settings.HotkeyCtrl) modifiers |= MOD_CONTROL;
        if (_settings.HotkeyShift) modifiers |= MOD_SHIFT;
        if (_settings.HotkeyAlt) modifiers |= MOD_ALT;
        if (_settings.HotkeyWin) modifiers |= MOD_WIN;
        
        // Get virtual key code from settings
        uint vk = GetVirtualKeyCode(_settings.HotkeyKey);
        
        // Register the hotkey
        RegisterHotKey(helper.Handle, HOTKEY_ID, modifiers, vk);
    }

    private uint GetVirtualKeyCode(string key)
    {
        // Common keys
        if (key.Length == 1 && char.IsLetter(key[0]))
            return (uint)char.ToUpper(key[0]);
        
        return key.ToUpper() switch
        {
            "NONE" => 0,
            "R" => 0x52,
            "F5" => 0x74,
            "F6" => 0x75,
            "F7" => 0x76,
            "F8" => 0x77,
            "F9" => 0x78,
            "F10" => 0x79,
            "F11" => 0x7A,
            "F12" => 0x7B,
            "SPACE" => 0x20,
            "ENTER" => 0x0D,
            "TAB" => 0x09,
            "ESCAPE" => 0x1B,
            _ => 0x52 // Default to R
        };
    }

    private void WhisperOnlyWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Unregister hotkey
        var helper = new System.Windows.Interop.WindowInteropHelper(this);
        UnregisterHotKey(helper.Handle, HOTKEY_ID);
        
        // Stop push-to-talk timer
        _pushToTalkTimer?.Stop();
    }

    // Push-to-talk using timer to check key state
    private System.Windows.Threading.DispatcherTimer? _pushToTalkTimer;
    
    private void StartPushToTalkTimer()
    {
        System.Diagnostics.Debug.WriteLine("[PTT] Starting push-to-talk timer");
        
        _pushToTalkTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _pushToTalkTimer.Tick += PushToTalkTimer_Tick;
        _pushToTalkTimer.Start();
    }
    
    private void StopPushToTalkTimer()
    {
        _pushToTalkTimer?.Stop();
        _pushToTalkTimer = null;
    }
    
    private void PushToTalkTimer_Tick(object? sender, EventArgs e)
    {
        if (!_settings.PushToTalk) return;
        
        uint hotkeyVk = GetVirtualKeyCode(_settings.HotkeyKey);
        
        // Check if modifiers are pressed
        bool ctrlPressed = (GetAsyncKeyState(0x11) & 0x8000) != 0;
        bool shiftPressed = (GetAsyncKeyState(0x10) & 0x8000) != 0;
        bool altPressed = (GetAsyncKeyState(0x12) & 0x8000) != 0;
        bool winPressed = (GetAsyncKeyState(0x5B) & 0x8000) != 0;
        bool keyPressed = (GetAsyncKeyState((int)hotkeyVk) & 0x8000) != 0;
        
        System.Diagnostics.Debug.WriteLine($"[PTT] Key={_settings.HotkeyKey} Vk={hotkeyVk} | Ctrl={ctrlPressed}({_settings.HotkeyCtrl}) Shift={shiftPressed}({_settings.HotkeyShift}) Alt={altPressed}({_settings.HotkeyAlt}) Win={winPressed}({_settings.HotkeyWin}) KeyPressed={keyPressed}");
        
        bool modifiersMatch = true;
        if (_settings.HotkeyCtrl) modifiersMatch &= ctrlPressed;
        if (_settings.HotkeyShift) modifiersMatch &= shiftPressed;
        if (_settings.HotkeyAlt) modifiersMatch &= altPressed;
        if (_settings.HotkeyWin) modifiersMatch &= winPressed;
        
        // If key is "None" (Vk=0), just check modifiers; otherwise check both key and modifiers
        bool isNoneKey = hotkeyVk == 0 || _settings.HotkeyKey.ToUpper() == "NONE";
        bool hotkeyPressed = isNoneKey ? modifiersMatch : (keyPressed && modifiersMatch);
        
        if (_isRecording)
        {
            // If recording and hotkey is released, stop
            if (!hotkeyPressed)
            {
                System.Diagnostics.Debug.WriteLine("[PTT] Key released, stopping recording");
                StopRecordingAndTranscribe();
            }
        }
        else
        {
            // If not recording and hotkey is pressed, start
            if (hotkeyPressed)
            {
                System.Diagnostics.Debug.WriteLine("[PTT] Key pressed, starting recording");
                StartRecording();
            }
        }
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
        // Skip if push-to-talk is enabled (keyboard hook handles it)
        if (msg == 0x0312 && wParam.ToInt32() == HOTKEY_ID && !_settings.PushToTalk)
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
            _vuMeterWindow?.SetLevel(level >= 0 ? level : 0);
        });
    }

    private async void MicButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRecording)
        {
            StopRecordingAndTranscribe();
            return;
        }

        StartRecording();
    }
    
    private void StartRecording()
    {
        if (_isRecording) return;
        
        System.Diagnostics.Debug.WriteLine("[PTT] Starting recording");
        
        // Show floating VU meter
        _vuMeterWindow = new VuMeterWindow();
        _vuMeterWindow.PositionAtBottomMiddle();
        _vuMeterWindow.Show();

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

            // Start recording (async - will keep recording until stopped)
            _ = _speechService.StartRecordingAsync(deviceIndex);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            _isRecording = false;
            MicButton.Content = "MIC";
            _vuMeterWindow?.Close();
            _vuMeterWindow = null;
        }
    }
    
    public async void StopRecordingAndTranscribe()
    {
        if (!_isRecording) return;
        
        System.Diagnostics.Debug.WriteLine("[PTT] Stopping recording");
        
        // Stop push-to-talk timer
        StopPushToTalkTimer();
        
        _isRecording = false;
        MicButton.Content = "MIC";
        StatusText.Text = "Processing...";
        VuMeter.Value = 0;
        
        _speechService!.AudioLevelChanged -= SpeechService_AudioLevelChanged;
        
        // Stop recording and get result
        var result = await _speechService!.StopRecordingAndTranscribeAsync();
        _speechService?.Dispose();
        _speechService = null;
        
        _vuMeterWindow?.Close();
        _vuMeterWindow = null;

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
        
        StatusText.Text = "Ready";
        
        // Restart push-to-talk timer for next recording
        if (_settings.PushToTalk)
        {
            StartPushToTalkTimer();
        }
    }

    public void OpenSettings()
    {
        Settings_Click(this, new RoutedEventArgs());
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        // Show a simple settings dialog with tabs for Whisper and Audio
        var settingsWindow = new Window
        {
            Title = "Settings",
            Width = 500,
            SizeToContent = SizeToContent.Height,
            MaxHeight = SystemParameters.PrimaryScreenHeight * 0.85,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(235, 235, 235)),
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            ShowInTaskbar = false
        };

        // Add rounded corners and shadow
        settingsWindow.Resources.Add(typeof(Window), new Style(typeof(Window))
        {
            Setters = { new Setter(Window.EffectProperty, new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 20, ShadowDepth = 0, Opacity = 0.5 }) }
        });

        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Create TabControl
        var tabControl = new TabControl { Margin = new Thickness(15, 15, 15, 0) };
        
        // ============ TAB 1: Whisper ============
        var whisperTab = new TabItem { Header = "🎙️ Whisper" };
        var whisperPanel = new StackPanel { Margin = new Thickness(15) };
        
        // Model selection
        var localModelLabel = new TextBlock { Text = "Model Size", FontSize = 13, Margin = new Thickness(0, 0, 0, 6) };
        whisperPanel.Children.Add(localModelLabel);
        var localModelPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 15) };
        var localModelCombo = new ComboBox { Width = 260, Padding = new Thickness(12, 10, 12, 10), FontSize = 14, Style = CreateMaterialComboBoxStyle() };
        localModelCombo.Items.Add("ggml-tiny.bin (~75 MB)");
        localModelCombo.Items.Add("ggml-base.bin (~150 MB)");
        localModelCombo.Items.Add("ggml-small.bin (~500 MB)");
        localModelCombo.Items.Add("ggml-medium.bin (~1.5 GB)");
        localModelCombo.SelectedIndex = 2; // default to small
        // Try to match saved setting
        for (int i = 0; i < localModelCombo.Items.Count; i++)
        {
            if (localModelCombo.Items[i]?.ToString()?.Contains(_settings.WhisperModelFileName) == true)
            {
                localModelCombo.SelectedIndex = i;
                break;
            }
        }
        localModelPanel.Children.Add(localModelCombo);
        var downloadModelButton = new Button { Content = "⬇️ Download", Margin = new Thickness(8, 0, 0, 0), Height = 30, VerticalAlignment = VerticalAlignment.Center };
        localModelPanel.Children.Add(downloadModelButton);
        whisperPanel.Children.Add(localModelPanel);

        // Model folder
        var modelFolderLabel = new TextBlock { Text = "Model Folder", FontSize = 13, Margin = new Thickness(0, 0, 0, 6) };
        whisperPanel.Children.Add(modelFolderLabel);
        var modelFolderBorder = CreateBorder();
        var modelFolderBox = new TextBox { Padding = new Thickness(12, 10, 12, 10), FontSize = 14, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        modelFolderBox.Text = _settings.WhisperModelFolder;
        modelFolderBorder.Child = modelFolderBox;
        whisperPanel.Children.Add(modelFolderBorder);

        // Separator
        var separator = new Separator { Margin = new Thickness(0, 20, 0, 20) };
        whisperPanel.Children.Add(separator);

        // Translation settings
        var translationLabel = new TextBlock { Text = "Translation", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
        whisperPanel.Children.Add(translationLabel);
        
        var translationPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var translationCheck = new CheckBox { Content = "Enable translation", VerticalAlignment = VerticalAlignment.Center };
        var translationLang = new ComboBox { Width = 100, Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(12, 10, 12, 10), FontSize = 14, Style = CreateMaterialComboBoxStyle() };
        translationLang.Items.Add("en");
        translationLang.Items.Add("es");
        translationLang.Items.Add("fr");
        translationLang.Items.Add("de");
        translationLang.Items.Add("zh");
        translationLang.Items.Add("ja");
        translationPanel.Children.Add(translationCheck);
        translationPanel.Children.Add(translationLang);
        whisperPanel.Children.Add(translationPanel);

        // Initialize translation controls
        translationCheck.IsChecked = _settings.TranslationEnabled;
        translationLang.SelectedItem = string.IsNullOrEmpty(_settings.TranslationTargetLanguage) ? "en" : _settings.TranslationTargetLanguage;
        
        whisperTab.Content = whisperPanel;
        tabControl.Items.Add(whisperTab);

        // ============ TAB 2: Hotkey ============
        var hotkeyTab = new TabItem { Header = "⌨️ Hotkey" };
        var hotkeyPanel = new StackPanel { Margin = new Thickness(15) };
        
        var hotkeyLabel = new TextBlock { Text = "Global Hotkey", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
        hotkeyPanel.Children.Add(hotkeyLabel);
        
        var modifierPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 15) };
        var ctrlCheck = new CheckBox { Content = "Ctrl", Margin = new Thickness(0, 0, 15, 0), IsChecked = _settings.HotkeyCtrl };
        var shiftCheck = new CheckBox { Content = "Shift", Margin = new Thickness(0, 0, 15, 0), IsChecked = _settings.HotkeyShift };
        var altCheck = new CheckBox { Content = "Alt", Margin = new Thickness(0, 0, 15, 0), IsChecked = _settings.HotkeyAlt };
        var winCheck = new CheckBox { Content = "Win", IsChecked = _settings.HotkeyWin };
        modifierPanel.Children.Add(ctrlCheck);
        modifierPanel.Children.Add(shiftCheck);
        modifierPanel.Children.Add(altCheck);
        modifierPanel.Children.Add(winCheck);
        hotkeyPanel.Children.Add(modifierPanel);
        
        var keyLabel = new TextBlock { Text = "Key", FontSize = 13, Margin = new Thickness(0, 0, 0, 6) };
        hotkeyPanel.Children.Add(keyLabel);
        var keyComboBox = new ComboBox { Width = 120, Padding = new Thickness(12, 10, 12, 10), FontSize = 14, Style = CreateMaterialComboBoxStyle(), HorizontalAlignment = HorizontalAlignment.Left };
        
        // Common keys (Win not included - use as modifier instead)
        string[] commonKeys = { "None", "R", "F5", "F6", "F7", "F8", "Space", "Enter", "Escape" };
        foreach (var key in commonKeys)
        {
            keyComboBox.Items.Add(key);
        }
        
        // Try to match saved setting
        for (int i = 0; i < keyComboBox.Items.Count; i++)
        {
            if (keyComboBox.Items[i]?.ToString()?.ToUpper() == _settings.HotkeyKey.ToUpper())
            {
                keyComboBox.SelectedIndex = i;
                break;
            }
        }
        if (keyComboBox.SelectedIndex < 0) keyComboBox.SelectedIndex = 0;
        
        hotkeyPanel.Children.Add(keyComboBox);
        
        var hotkeyHint = new TextBlock { Text = "Select 'None' for modifier-only (e.g., Ctrl+Win), or pick a modifier + key.", FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 15, 0, 0) };
        hotkeyPanel.Children.Add(hotkeyHint);
        
        // Push-to-talk checkbox
        var pushToTalkCheck = new CheckBox { Content = "Push-to-talk (hold key to record, release to transcribe)", FontSize = 13, IsChecked = _settings.PushToTalk, Margin = new Thickness(0, 15, 0, 0) };
        hotkeyPanel.Children.Add(pushToTalkCheck);
        
        // Start with Windows checkbox
        var startupSeparator = new Separator { Margin = new Thickness(0, 20, 0, 15) };
        hotkeyPanel.Children.Add(startupSeparator);
        
        var startupCheck = new CheckBox { Content = "Start with Windows", FontSize = 13, IsChecked = App.GetStartWithWindows() };
        hotkeyPanel.Children.Add(startupCheck);
        
        hotkeyTab.Content = hotkeyPanel;
        tabControl.Items.Add(hotkeyTab);

        // ============ TAB 3: Audio ============
        var audioTab = new TabItem { Header = "🔊 Audio" };
        var audioPanel = new StackPanel { Margin = new Thickness(15) };
        
        // Microphone device
        var micLabel = new TextBlock { Text = "Microphone Device", FontSize = 13, Margin = new Thickness(0, 0, 0, 6) };
        audioPanel.Children.Add(micLabel);
        var micComboBoxBorder = CreateBorder();
        var micComboBox = new ComboBox { IsEditable = false, Padding = new Thickness(12, 10, 12, 10), FontSize = 14, Style = CreateMaterialComboBoxStyle() };
        micComboBoxBorder.Child = micComboBox;
        
        micComboBox.Items.Add("(Default System Microphone)");
        try
        {
            for (int deviceId = 0; deviceId < NAudio.Wave.WaveIn.DeviceCount; deviceId++)
            {
                var caps = NAudio.Wave.WaveIn.GetCapabilities(deviceId);
                micComboBox.Items.Add($"{deviceId}: {caps.ProductName}");
            }
        }
        catch { }
        
        if (string.IsNullOrEmpty(_settings.SelectedMicrophoneDevice))
            micComboBox.SelectedIndex = 0;
        else
        {
            for (int i = 0; i < micComboBox.Items.Count; i++)
            {
                if (micComboBox.Items[i]?.ToString()?.Contains(_settings.SelectedMicrophoneDevice) == true)
                { micComboBox.SelectedIndex = i; break; }
            }
            if (micComboBox.SelectedIndex < 0) micComboBox.SelectedIndex = 0;
        }
        audioPanel.Children.Add(micComboBoxBorder);
        
        audioTab.Content = audioPanel;
        tabControl.Items.Add(audioTab);

        Grid.SetRow(tabControl, 0);
        mainGrid.Children.Add(tabControl);

        // Helper to create bordered textbox
        Border CreateBorder() => new Border 
        { 
            CornerRadius = new CornerRadius(6),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 0)
        };

        // ============ BUTTONS ============
        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(15, 15, 15, 15) };
        
        var primaryStyle = CreatePrimaryButtonStyle();
        var cancelStyle = CreateCancelButtonStyle();
        
        var saveButton = new Button { Content = "Save", Style = primaryStyle, Margin = new Thickness(0, 0, 0, 0), Height = 44, MinWidth = 80 };
        var cancelButton = new Button { Content = "Cancel", Style = cancelStyle, Margin = new Thickness(12, 0, 0, 0), Height = 44, MinWidth = 80 };

        downloadModelButton.Click += async (s, args) =>
        {
            try
            {
                var fileNames = new[] { "ggml-tiny.bin", "ggml-base.bin", "ggml-small.bin", "ggml-medium.bin" };
                var fileName = fileNames[localModelCombo.SelectedIndex];
                var folder = string.IsNullOrEmpty(modelFolderBox.Text) ? _settings.WhisperModelFolder : modelFolderBox.Text;
                Directory.CreateDirectory(folder);
                var targetPath = Path.Combine(folder, fileName);
                downloadModelButton.IsEnabled = false;
                downloadModelButton.Content = "Downloading...";
                await SpeechRecognitionService.DownloadModelToPathAsync(targetPath);
                downloadModelButton.Content = "Downloaded ✅";
                MessageBox.Show($"Model downloaded to: {targetPath}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Download failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                downloadModelButton.IsEnabled = true;
                downloadModelButton.Content = "⬇️ Download Model";
            }
        };

        saveButton.Click += (s, args) =>
        {
            try
            {
                var fileNames = new[] { "ggml-tiny.bin", "ggml-base.bin", "ggml-small.bin", "ggml-medium.bin" };
                _settings.WhisperModelFileName = fileNames[localModelCombo.SelectedIndex];
                _settings.WhisperModelFolder = modelFolderBox.Text;
                _settings.TranslationEnabled = translationCheck.IsChecked == true;
                _settings.TranslationTargetLanguage = translationLang.SelectedItem?.ToString() ?? "en";
                
                if (micComboBox.SelectedIndex > 0)
                    _settings.SelectedMicrophoneDevice = micComboBox.SelectedItem?.ToString() ?? "";
                else
                    _settings.SelectedMicrophoneDevice = "";
                
                // Save hotkey settings
                _settings.HotkeyCtrl = ctrlCheck.IsChecked == true;
                _settings.HotkeyShift = shiftCheck.IsChecked == true;
                _settings.HotkeyAlt = altCheck.IsChecked == true;
                _settings.HotkeyWin = winCheck.IsChecked == true;
                _settings.HotkeyKey = keyComboBox.SelectedItem?.ToString() ?? "R";
                _settings.PushToTalk = pushToTalkCheck.IsChecked == true;
                
                // Save startup setting
                _settings.StartWithWindows = startupCheck.IsChecked == true;
                App.SetStartWithWindows(_settings.StartWithWindows);
                
                _settings.Save();
                
                // Re-register hotkey immediately
                RegisterGlobalHotkey();
            }
            finally
            {
                settingsWindow.Close();
            }
        };

        cancelButton.Click += (s, args) => settingsWindow.Close();

        buttonPanel.Children.Add(saveButton);
        buttonPanel.Children.Add(cancelButton);
        Grid.SetRow(buttonPanel, 1);
        mainGrid.Children.Add(buttonPanel);

        // Wrap content in a border for rounded corners
        var contentBorder = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = Brushes.White,
            Margin = new Thickness(10)
        };
        contentBorder.Child = mainGrid;
        
        // Add a simple title bar with close button
        var titleBar = new Grid { Height = 44, Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)) };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        
        var titleText = new TextBlock { Text = "Settings", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(15, 0, 0, 0) };
        var closeButton = new Button { Content = "✕", Width = 44, Height = 44, Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 16, Cursor = Cursors.Hand };
        closeButton.Click += (s, args) => settingsWindow.Close();
        
        Grid.SetColumn(titleText, 0);
        Grid.SetColumn(closeButton, 1);
        titleBar.Children.Add(titleText);
        titleBar.Children.Add(closeButton);
        
        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(contentBorder, 1);
        rootGrid.Children.Add(titleBar);
        rootGrid.Children.Add(contentBorder);
        
        settingsWindow.Content = rootGrid;
        settingsWindow.ShowDialog();
        Hide();
    }

    private Style CreatePrimaryButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0, 120, 212))));
        style.Setters.Add(new Setter(Button.ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(20, 12, 20, 12)));
        style.Setters.Add(new Setter(Button.FontSizeProperty, 14.0));
        style.Setters.Add(new Setter(Button.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0, 120, 212)));
        var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(contentPresenter);
        template.VisualTree = border;
        style.Setters.Add(new Setter(Button.TemplateProperty, template));
        return style;
    }

    private Style CreateCancelButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(Color.FromRgb(240, 240, 240))));
        style.Setters.Add(new Setter(Button.ForegroundProperty, new SolidColorBrush(Color.FromRgb(60, 60, 60))));
        style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Button.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(200, 200, 200))));
        style.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(20, 12, 20, 12)));
        style.Setters.Add(new Setter(Button.FontSizeProperty, 14.0));
        style.Setters.Add(new Setter(Button.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(240, 240, 240)));
        var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(contentPresenter);
        template.VisualTree = border;
        style.Setters.Add(new Setter(Button.TemplateProperty, template));
        return style;
    }

    private Style CreateMaterialComboBoxStyle()
    {
        var style = new Style(typeof(ComboBox));
        
        // Main ComboBox style
        style.Setters.Add(new Setter(ComboBox.BackgroundProperty, Brushes.White));
        style.Setters.Add(new Setter(ComboBox.ForegroundProperty, new SolidColorBrush(Color.FromRgb(33, 33, 33))));
        style.Setters.Add(new Setter(ComboBox.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(224, 224, 224))));
        style.Setters.Add(new Setter(ComboBox.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(ComboBox.PaddingProperty, new Thickness(12, 10, 12, 10)));
        style.Setters.Add(new Setter(ComboBox.FontSizeProperty, 14.0));
        style.Setters.Add(new Setter(ComboBox.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        
        // Add triggers for hover/focus effects
        var hoverTrigger = new Trigger { Property = ComboBox.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(ComboBox.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0, 120, 212))));
        style.Triggers.Add(hoverTrigger);
        
        var focusedTrigger = new Trigger { Property = ComboBox.IsKeyboardFocusWithinProperty, Value = true };
        focusedTrigger.Setters.Add(new Setter(ComboBox.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0, 120, 212))));
        focusedTrigger.Setters.Add(new Setter(ComboBox.BorderThicknessProperty, new Thickness(2)));
        style.Triggers.Add(focusedTrigger);
        
        // Style the items in dropdown
        var itemContainerStyle = new Style(typeof(ComboBoxItem));
        itemContainerStyle.Setters.Add(new Setter(ComboBoxItem.PaddingProperty, new Thickness(12, 8, 12, 8)));
        itemContainerStyle.Setters.Add(new Setter(ComboBoxItem.FontSizeProperty, 14.0));
        
        var itemHoverTrigger = new Trigger { Property = ComboBoxItem.IsMouseOverProperty, Value = true };
        itemHoverTrigger.Setters.Add(new Setter(ComboBoxItem.BackgroundProperty, new SolidColorBrush(Color.FromRgb(245, 245, 245))));
        itemContainerStyle.Triggers.Add(itemHoverTrigger);
        
        var itemSelectedTrigger = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
        itemSelectedTrigger.Setters.Add(new Setter(ComboBoxItem.BackgroundProperty, new SolidColorBrush(Color.FromRgb(224, 242, 255))));
        itemContainerStyle.Triggers.Add(itemSelectedTrigger);
        
        style.Setters.Add(new Setter(ComboBox.ItemContainerStyleProperty, itemContainerStyle));
        
        return style;
    }
}
