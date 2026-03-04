using System.IO;
using System.Text.Json;

namespace whisperMeOff;

public class ServerProfile
{
    public string Name { get; set; } = "";
    public string ServerUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Token { get; set; } = "";
    public string AgentId { get; set; } = "";
}

public class AppSettings
{
    public List<ServerProfile> Profiles { get; set; } = new()
    {
        new ServerProfile { Name = "Ollama", ServerUrl = "http://localhost:11434", Model = "" },
        new ServerProfile { Name = "LM Studio", ServerUrl = "http://localhost:1234/v1", Model = "" },
        new ServerProfile { Name = "Jan AI", ServerUrl = "http://localhost:1337/v1", Model = "" },
        new ServerProfile { Name = "OpenClaw", ServerUrl = "", Token = "", AgentId = "", Model = "" }
    };
    
    public int SelectedProfileIndex { get; set; } = 0;
    
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
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                
                // Ensure OpenClaw profile exists
                if (!settings.Profiles.Any(p => p.Name == "OpenClaw"))
                {
                    settings.Profiles.Add(new ServerProfile { Name = "OpenClaw", ServerUrl = "", Token = "", AgentId = "", Model = "" });
                }
                
                return settings;
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
