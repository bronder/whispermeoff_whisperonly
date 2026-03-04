using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        var localModelCombo = new ComboBox { Width = 200, Padding = new Thickness(8, 6, 8, 6) };
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
        var translationLang = new ComboBox { Width = 100, Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(8, 6, 8, 6) };
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

        // ============ TAB 2: Audio ============
        var audioTab = new TabItem { Header = "🔊 Audio" };
        var audioPanel = new StackPanel { Margin = new Thickness(15) };
        
        // Microphone device
        var micLabel = new TextBlock { Text = "Microphone Device", FontSize = 13, Margin = new Thickness(0, 0, 0, 6) };
        audioPanel.Children.Add(micLabel);
        var micComboBoxBorder = CreateBorder();
        var micComboBox = new ComboBox { IsEditable = false, Padding = new Thickness(12, 10, 12, 10), FontSize = 14, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
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
            var fileNames = new[] { "ggml-tiny.bin", "ggml-base.bin", "ggml-small.bin", "ggml-medium.bin" };
            _settings.WhisperModelFileName = fileNames[localModelCombo.SelectedIndex];
            _settings.WhisperModelFolder = modelFolderBox.Text;
            _settings.TranslationEnabled = translationCheck.IsChecked == true;
            _settings.TranslationTargetLanguage = translationLang.SelectedItem?.ToString() ?? "en";
            
            if (micComboBox.SelectedIndex > 0)
                _settings.SelectedMicrophoneDevice = micComboBox.SelectedItem?.ToString() ?? "";
            else
                _settings.SelectedMicrophoneDevice = "";
            
            _settings.Save();
            settingsWindow.Close();
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
}
