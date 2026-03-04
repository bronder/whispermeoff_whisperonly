using System.IO;
using System.Text.Json;

namespace whisperMeOff;

public class AppSettings
{
    // Window position and size
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 950;
    public double WindowHeight { get; set; } = 650;
    public bool WindowMaximized { get; set; } = false;

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

    // Global hotkey settings
    public bool HotkeyCtrl { get; set; } = true;
    public bool HotkeyShift { get; set; } = true;
    public bool HotkeyAlt { get; set; } = false;
    public bool HotkeyWin { get; set; } = false;
    public string HotkeyKey { get; set; } = "R";

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

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Silently fail if we can't save
        }
    }
}
