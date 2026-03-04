using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingColor = System.Drawing.Color;
using DrawingBrush = System.Drawing.Brushes;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingSolidBrush = System.Drawing.SolidBrush;
using DrawingPen = System.Drawing.Pen;
using Drawing2DSmoothingMode = System.Drawing.Drawing2D.SmoothingMode;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using Color = System.Windows.Media.Color;
using TabControl = System.Windows.Controls.TabControl;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;
using StackPanel = System.Windows.Controls.StackPanel;
using Grid = System.Windows.Controls.Grid;
using Border = System.Windows.Controls.Border;
using Orientation = System.Windows.Controls.Orientation;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using Separator = System.Windows.Controls.Separator;
using MessageBox = System.Windows.MessageBox;
using FontWeights = System.Windows.FontWeights;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Cursors = System.Windows.Input.Cursors;
using Application = System.Windows.Application;

namespace whisperMeOff;

public class ChatMessage
{
    public string Sender { get; set; } = "";
    public string Message { get; set; } = "";
    public SolidColorBrush BackgroundColor { get; set; } = MediaBrushes.White;
    public SolidColorBrush SenderColor { get; set; } = MediaBrushes.Black;
    public SolidColorBrush TextColor { get; set; } = MediaBrushes.Black;
    public System.Windows.HorizontalAlignment HorizontalAlignment { get; set; } = System.Windows.HorizontalAlignment.Left;
}

public partial class MainWindow : Window
{
    public static RoutedCommand ZoomInCommand = new RoutedCommand();
    public static RoutedCommand ZoomOutCommand = new RoutedCommand();
    public static RoutedCommand ToggleMicCommand = new RoutedCommand();
    
    private ObservableCollection<ChatMessage> _messages = new();
    private OllamaService _aiService = new();
    private List<(string sender, string message)> _conversationHistory = new();
    private AppSettings _settings = new();
    private SpeechRecognitionService? _speechService;
    private bool _isRecording;

    public MainWindow()
    {
        InitializeComponent();
        
        // Set window icon
        Icon = CreateRobotIcon();
        
        // Load saved settings
        _settings = AppSettings.Load();
        
        // Configure AI service with selected profile
        var profile = _settings.Profiles[_settings.SelectedProfileIndex];
        _aiService.Configure(profile.ServerUrl, profile.Model, profile.ApiKey, profile.Token, profile.AgentId, profile.Name);
        
        // Restore window position and size
        if (!double.IsNaN(_settings.WindowLeft) && !double.IsNaN(_settings.WindowTop))
        {
            Left = _settings.WindowLeft;
            Top = _settings.WindowTop;
        }
        Width = _settings.WindowWidth;
        Height = _settings.WindowHeight;
        if (_settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
        
        // Save window position on close
        Closing += (s, e) =>
        {
            if (WindowState == WindowState.Normal)
            {
                _settings.WindowLeft = Left;
                _settings.WindowTop = Top;
                _settings.WindowWidth = Width;
                _settings.WindowHeight = Height;
            }
            _settings.WindowMaximized = WindowState == WindowState.Maximized;
            _settings.Save();
        };
        
        // Bind commands to handlers
        CommandBindings.Add(new CommandBinding(ZoomInCommand, ZoomIn_Executed));
        CommandBindings.Add(new CommandBinding(ZoomOutCommand, ZoomOut_Executed));
        CommandBindings.Add(new CommandBinding(ToggleMicCommand, ToggleMic_Executed));
        
        ChatMessages.ItemsSource = _messages;
        
        // Add welcome message
        AddMessage("AI", "Hello! I'm ready to chat.\n\nYour Ollama server is running on http://localhost:11434 with models:\n• qwen3:4b\n• karanchopda333/whisper:latest\n\nGo to File > Settings to configure or start chatting!", true);
        
        // Try to connect to saved settings
        _ = TryConnectToAI();
    }

    private async Task TryConnectToAI()
    {
        try
        {
            var models = await _aiService.GetAvailableModelsAsync();
            if (models.Count > 0)
            {
                AddMessage("AI", $"✓ Connected to AI server! Available models: {string.Join(", ", models)}", true);
            }
            else
            {
                AddMessage("AI", "⚠ Connected to server, but no models found. Load a model in Ollama and try again.", true);
            }
        }
        catch (Exception ex)
        {
            AddMessage("AI", $"✗ Could not connect to AI server: {ex.Message}", true);
        }
    }

    private void ZoomIn_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        ZoomIn_Click(sender, e);
    }

    private void ZoomOut_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        ZoomOut_Click(sender, e);
    }

    private void ToggleMic_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        MicButton_Click(sender, e);
    }

    private void AddMessage(string sender, string message, bool isAI = false)
    {
        var chatMessage = new ChatMessage
        {
            Sender = sender,
            Message = message,
            BackgroundColor = isAI 
                ? new SolidColorBrush(MediaColor.FromRgb(255, 255, 255)) 
                : new SolidColorBrush(MediaColor.FromRgb(220, 235, 250)), // Very faint blue
            SenderColor = new SolidColorBrush(MediaColor.FromRgb(0, 90, 158)), // Darker blue for sender name
            TextColor = MediaBrushes.Black, // Black font
            HorizontalAlignment = isAI ? System.Windows.HorizontalAlignment.Left : System.Windows.HorizontalAlignment.Right
        };
        _messages.Add(chatMessage);
        ChatMessages.ScrollIntoView(chatMessage);
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        SendMessage();
    }

    private async void MicButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRecording)
        {
            // Stop recording
            _isRecording = false;
            MicButton.Content = "🎤";
            MicButton.ToolTip = "Click to start voice input";
            
            System.Diagnostics.Debug.WriteLine("[MicButton] Stop clicked, calling StopRecognition");
            
            // Signal stop - don't clear text here, let the async method handle it
            _speechService?.StopRecognition();
            return;
        }

        // Start recording
        _isRecording = true;
        MicButton.Content = "⏹";
        MicButton.ToolTip = "Click to stop voice input";
        MessageInput.Text = "Listening...";

        try
        {
            _speechService = new SpeechRecognitionService();
            
            // Parse device index from settings
            int deviceIndex = -1;
            var savedDevice = _settings.SelectedMicrophoneDevice;
            if (!string.IsNullOrEmpty(savedDevice) && savedDevice.Contains(":"))
            {
                var parts = savedDevice.Split(':');
                if (parts.Length > 0 && int.TryParse(parts[0].Trim(), out int parsedIndex))
                {
                    deviceIndex = parsedIndex;
                }
            }
            
            string result;

            // Apply translation and model settings to the speech service
            _speechService.TranslateWithWhisper = _settings.TranslationEnabled;
            _speechService.WhisperTranslationTarget = _settings.TranslationTargetLanguage;
            var modelFolder = _settings.WhisperModelFolder;
            var modelFile = _settings.WhisperModelFileName;
            if (string.IsNullOrEmpty(modelFolder))
            {
                modelFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "whisperMeOff", "models");
            }
            _speechService.LocalWhisperModelPath = Path.Combine(modelFolder, modelFile);
            
            // Use Whisper.net for transcription (local, fast with GPU)
            System.Diagnostics.Debug.WriteLine("[MicButton] Starting Whisper transcription");
            result = await _speechService.RecognizeSpeechWithWhisperAsync(deviceIndex);
            System.Diagnostics.Debug.WriteLine($"[MicButton] Whisper result: '{result}'");
            
            // Only process result if we're not recording (user clicked stop or it completed)
            System.Diagnostics.Debug.WriteLine($"[MicButton] Processing result, _isRecording={_isRecording}");
            if (!_isRecording)
            {
                System.Diagnostics.Debug.WriteLine($"[MicButton] Result: '{result}'");
                
                // Clear the "Listening..." placeholder
                if (MessageInput.Text == "Listening...")
                {
                    MessageInput.Text = "";
                }
                
                // Append recognized text if any
                if (!string.IsNullOrEmpty(result) && !result.StartsWith("Speech recognition error") && !result.StartsWith("Whisper"))
                {
                    MessageInput.Text = result.Trim();
                    
                    // Auto-send the message after voice recognition
                    SendMessage();
                }
                else if (result.StartsWith("Speech recognition error") || result.StartsWith("Whisper"))
                {
                    AddMessage("System", result, true);
                }
            }
        }
        catch (Exception ex)
        {
            AddMessage("System", $"Voice input error: {ex.Message}", true);
        }
        finally
        {
            _isRecording = false;
            MicButton.Content = "🎤";
            MicButton.ToolTip = "Click to start voice input";
        }
    }

    private void MessageInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            SendMessage();
            e.Handled = true;
        }
    }

    private void SendMessage()
    {
        var userMessage = MessageInput.Text.Trim();
        if (string.IsNullOrEmpty(userMessage)) return;

        // Add user message
        AddMessage("You", userMessage, false);
        MessageInput.Clear();

        // Add to conversation history
        _conversationHistory.Add(("You", userMessage));

        // Call AI service
        Task.Run(async () =>
        {
            var aiResponse = await _aiService.SendMessageAsync(userMessage, _conversationHistory);
            
            Dispatcher.Invoke(() =>
            {
                AddMessage("AI", aiResponse, true);
                _conversationHistory.Add(("AI", aiResponse));
            });
        });
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        if (WindowScale.ScaleX < 2.0)
        {
            WindowScale.ScaleX += 0.1;
            WindowScale.ScaleY += 0.1;
        }
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        if (WindowScale.ScaleX > 0.5)
        {
            WindowScale.ScaleX -= 0.1;
            WindowScale.ScaleY -= 0.1;
        }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        // Show a simple settings dialog with tabs
        var settingsWindow = new Window
        {
            Title = "Settings",
            Width = 560,
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
        
        // ============ TAB 1: LLM Server ============
        var llmTab = new TabItem { Header = "🤖 LLM Server" };
        var llmPanel = new StackPanel { Margin = new Thickness(15) };
        
        // Profile selector
        var profileLabel = new TextBlock { Text = "Server Profile", FontSize = 13, Margin = new Thickness(0, 0, 0, 6) };
        llmPanel.Children.Add(profileLabel);
        var profileComboBoxBorder = CreateBorder();
        var profileComboBox = new ComboBox { IsEditable = true, Padding = new Thickness(12, 10, 12, 10), FontSize = 14, Background = MediaBrushes.Transparent, BorderThickness = new Thickness(0) };
        profileComboBoxBorder.Child = profileComboBox;
        foreach (var profile in _settings.Profiles) profileComboBox.Items.Add(profile.Name);
        profileComboBox.SelectedIndex = _settings.SelectedProfileIndex;
        llmPanel.Children.Add(profileComboBoxBorder);

        // Server URL
        var urlLabel = new TextBlock { Text = "Server URL", FontSize = 13, Margin = new Thickness(0, 15, 0, 6) };
        llmPanel.Children.Add(urlLabel);
        var urlBorder = CreateBorder();
        var urlTextBox = new TextBox { Padding = new Thickness(12, 10, 12, 10), FontSize = 14, Background = MediaBrushes.Transparent, BorderThickness = new Thickness(0) };
        urlBorder.Child = urlTextBox;
        llmPanel.Children.Add(urlBorder);

        // Model name
        var modelLabel = new TextBlock { Text = "Model Name", FontSize = 13, Margin = new Thickness(0, 15, 0, 6) };
        llmPanel.Children.Add(modelLabel);
        var modelBorder = CreateBorder();
        var modelComboBox = new ComboBox { IsEditable = true, Padding = new Thickness(12, 10, 12, 10), FontSize = 14, Background = MediaBrushes.Transparent, BorderThickness = new Thickness(0) };
        modelBorder.Child = modelComboBox;
        llmPanel.Children.Add(modelBorder);

        // API Key
        var apiKeyLabel = new TextBlock { Text = "API Key (optional)", FontSize = 13, Margin = new Thickness(0, 15, 0, 6) };
        llmPanel.Children.Add(apiKeyLabel);
        var apiKeyBorder = CreateBorder();
        var apiKeyTextBox = new TextBox { Padding = new Thickness(12, 10, 12, 10), FontSize = 14, Background = MediaBrushes.Transparent, BorderThickness = new Thickness(0) };
        apiKeyBorder.Child = apiKeyTextBox;
        llmPanel.Children.Add(apiKeyBorder);

        // Token (for OpenClaw)
        var tokenLabel = new TextBlock { Text = "Token (optional)", FontSize = 13, Margin = new Thickness(0, 15, 0, 6) };
        llmPanel.Children.Add(tokenLabel);
        var tokenBorder = CreateBorder();
        var tokenTextBox = new TextBox { Padding = new Thickness(12, 10, 12, 10), FontSize = 14, Background = MediaBrushes.Transparent, BorderThickness = new Thickness(0) };
        tokenBorder.Child = tokenTextBox;
        llmPanel.Children.Add(tokenBorder);

        // Agent ID
        var agentIdLabel = new TextBlock { Text = "Agent ID (optional)", FontSize = 13, Margin = new Thickness(0, 15, 0, 6) };
        llmPanel.Children.Add(agentIdLabel);
        var agentIdBorder = CreateBorder();
        var agentIdTextBox = new TextBox { Padding = new Thickness(12, 10, 12, 10), FontSize = 14, Background = MediaBrushes.Transparent, BorderThickness = new Thickness(0) };
        agentIdBorder.Child = agentIdTextBox;
        llmPanel.Children.Add(agentIdBorder);
        
        llmTab.Content = llmPanel;
        tabControl.Items.Add(llmTab);

        // ============ TAB 2: Whisper ============
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
        var modelFolderBox = new TextBox { Padding = new Thickness(12, 10, 12, 10), FontSize = 14, Background = MediaBrushes.Transparent, BorderThickness = new Thickness(0) };
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

        // ============ TAB 3: Audio ============
        var audioTab = new TabItem { Header = "🔊 Audio" };
        var audioPanel = new StackPanel { Margin = new Thickness(15) };
        
        // Microphone device
        var micLabel = new TextBlock { Text = "Microphone Device", FontSize = 13, Margin = new Thickness(0, 0, 0, 6) };
        audioPanel.Children.Add(micLabel);
        var micComboBoxBorder = CreateBorder();
        var micComboBox = new ComboBox { IsEditable = false, Padding = new Thickness(12, 10, 12, 10), FontSize = 14, Background = MediaBrushes.Transparent, BorderThickness = new Thickness(0) };
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
            Background = MediaBrushes.White,
            BorderBrush = new SolidColorBrush(MediaColor.FromRgb(224, 224, 224)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 0)
        };

        // ============ BUTTONS ============
        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(15, 15, 15, 15) };
        
        var primaryStyle = CreatePrimaryButtonStyle();
        var cancelStyle = CreateCancelButtonStyle();
        
        var testButton = new Button { Content = "Test Connection", Style = primaryStyle, Margin = new Thickness(0, 0, 12, 0), Height = 44, MinWidth = 120 };
        var saveButton = new Button { Content = "Save", Style = primaryStyle, Margin = new Thickness(0, 0, 0, 0), Height = 44, MinWidth = 80 };
        var cancelButton = new Button { Content = "Cancel", Style = cancelStyle, Margin = new Thickness(12, 0, 0, 0), Height = 44, MinWidth = 80 };

        // Event handlers
        void UpdateFields()
        {
            var idx = profileComboBox.SelectedIndex;
            if (idx >= 0 && idx < _settings.Profiles.Count)
            {
                urlTextBox.Text = _settings.Profiles[idx].ServerUrl;
                modelComboBox.Text = _settings.Profiles[idx].Model;
                apiKeyTextBox.Text = _settings.Profiles[idx].ApiKey;
                tokenTextBox.Text = _settings.Profiles[idx].Token;
                agentIdTextBox.Text = _settings.Profiles[idx].AgentId;
            }
        }
        profileComboBox.SelectionChanged += (s, args) => UpdateFields();
        UpdateFields();

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

        testButton.Click += async (s, args) =>
        {
            var testService = new OllamaService();
            testService.Configure(urlTextBox.Text, "", apiKeyTextBox.Text);
            var models = await testService.GetAvailableModelsAsync();
            if (models.Count > 0)
            {
                modelComboBox.Items.Clear();
                foreach (var m in models) modelComboBox.Items.Add(m);
                if (modelComboBox.SelectedIndex < 0 && modelComboBox.Items.Count > 0) modelComboBox.SelectedIndex = 0;
                MessageBox.Show($"✓ Connected! Found {models.Count} model(s).", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("✗ Could not connect. Is the server running?", "Connection Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        saveButton.Click += async (s, args) =>
        {
            var idx = profileComboBox.SelectedIndex;
            if (idx >= 0 && idx < _settings.Profiles.Count)
            {
                _settings.Profiles[idx].ServerUrl = urlTextBox.Text;
                _settings.Profiles[idx].Model = modelComboBox.Text;
                _settings.Profiles[idx].ApiKey = apiKeyTextBox.Text;
                _settings.Profiles[idx].Token = tokenTextBox.Text;
                _settings.Profiles[idx].AgentId = agentIdTextBox.Text;
                _settings.SelectedProfileIndex = idx;
            }
            
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
            
            var selectedProfileName = profileComboBox.SelectedItem?.ToString() ?? "OpenClaw";
            _aiService.Configure(urlTextBox.Text, modelComboBox.Text, apiKeyTextBox.Text, tokenTextBox.Text, agentIdTextBox.Text, selectedProfileName);
            _conversationHistory.Clear();
            
            try
            {
                var models = await _aiService.GetAvailableModelsAsync();
                AddMessage("AI", models.Count > 0 ? $"✓ Connected! Models: {string.Join(", ", models)}" : "⚠ Connected, but no models found.", true);
            }
            catch (Exception ex)
            {
                AddMessage("AI", $"✗ Could not connect: {ex.Message}", true);
            }
            
            settingsWindow.Close();
        };

        cancelButton.Click += (s, args) => settingsWindow.Close();

        buttonPanel.Children.Add(testButton);
        buttonPanel.Children.Add(saveButton);
        buttonPanel.Children.Add(cancelButton);
        Grid.SetRow(buttonPanel, 1);
        mainGrid.Children.Add(buttonPanel);

        // Wrap content in a border for rounded corners
        var contentBorder = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = MediaBrushes.White,
            Margin = new Thickness(10)
        };
        contentBorder.Child = mainGrid;
        
        // Add a simple title bar with close button
        var titleBar = new Grid { Height = 44, Background = new SolidColorBrush(MediaColor.FromRgb(245, 245, 245)) };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        
        var titleText = new TextBlock { Text = "Settings", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(15, 0, 0, 0) };
        var closeButton = new Button { Content = "✕", Width = 44, Height = 44, Background = MediaBrushes.Transparent, BorderThickness = new Thickness(0), FontSize = 16, Cursor = Cursors.Hand };
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
        style.Setters.Add(new Setter(Button.ForegroundProperty, MediaBrushes.White));
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
        style.Setters.Add(new Setter(Button.ForegroundProperty, new SolidColorBrush(MediaColor.FromRgb(60, 60, 60))));
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
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(200, 200, 200)));
        var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(contentPresenter);
        template.VisualTree = border;
        style.Setters.Add(new Setter(Button.TemplateProperty, template));
        return style;
    }

    private ImageSource CreateRobotIcon()
    {
        // Create a simple robot icon programmatically
        using var bitmap = new DrawingBitmap(32, 32);
        using var g = DrawingGraphics.FromImage(bitmap);
        
        // Background - rounded rectangle
        g.SmoothingMode = Drawing2DSmoothingMode.AntiAlias;
        g.Clear(DrawingColor.FromArgb(0, 120, 212)); // Blue background
        
        // Robot head - rounded rectangle
        var headBrush = new DrawingSolidBrush(DrawingColor.White);
        g.FillRectangle(headBrush, 6, 4, 20, 18);
        
        // Eyes
        g.FillEllipse(new DrawingSolidBrush(DrawingColor.FromArgb(0, 120, 212)), 10, 9, 4, 4);
        g.FillEllipse(new DrawingSolidBrush(DrawingColor.FromArgb(0, 120, 212)), 18, 9, 4, 4);
        
        // Mouth
        g.DrawLine(new DrawingPen(DrawingColor.FromArgb(0, 120, 212), 2), 11, 17, 21, 17);
        
        // Antenna
        g.DrawLine(new DrawingPen(DrawingColor.White, 2), 16, 4, 16, 1);
        g.FillEllipse(headBrush, 14, 0, 4, 3);
        
        // Convert to WPF ImageSource
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("whisperMeOff v1.0\nA Windows native application", "About");
    }
}
