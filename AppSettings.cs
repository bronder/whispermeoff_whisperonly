using System.IO;
using System.Text.Json;

namespace whisperMeOff;

public class AppSettings
{
    // Audio input device for speech recognition
    public string SelectedMicrophoneDevice { get; set; } = "";

    // Translation settings (Whisper.net built-in)
    public bool TranslationEnabled { get; set; } = false;
    public string TranslationTargetLanguage { get; set; } = "en";

    // Local Whisper model settings
    public string WhisperModelFileName { get; set; } = "ggml-small.bin";
    public string WhisperModelFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "whisperMeOff",
        "models");

    // LLamaSharp settings
    public bool LLamaEnabled { get; set; } = false;
    public string LLamaModelFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "whisperMeOff",
        "llama-models");
    public string LLamaModelFileName { get; set; } = "";
    public int LLamaMaxTokens { get; set; } = 512;
    public double LLamaTemperature { get; set; } = 0.7;

    // Global hotkey settings
    public bool HotkeyCtrl { get; set; } = true;
    public bool HotkeyShift { get; set; } = true;
    public bool HotkeyAlt { get; set; } = false;
    public bool HotkeyWin { get; set; } = false;
    public string HotkeyKey { get; set; } = "R";
    
    // Push-to-talk mode (hold hotkey to record, release to transcribe)
    public bool PushToTalk { get; set; } = false;

    // Start with Windows
    public bool StartWithWindows { get; set; } = false;

    private static string SettingsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "whisperMeOff",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // If loading fails, return defaults
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
            };
            var json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SAVE ERROR] {ex.Message}");
        }
    }
}
