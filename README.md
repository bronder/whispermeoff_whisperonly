# whisperMeOff

A lightweight Windows desktop app for voice-to-text transcription using Whisper. Press a hotkey, speak, and your words are automatically typed into any application.

![whisperMeOff Screenshot](https://via.placeholder.com/800x450?text=whisperMeOff+Screenshot)

## Features

- 🎙️ **Voice Transcription** - Press a hotkey, speak, and see your words appear in any app
- 📋 **Auto-Paste** - Transcribed text is automatically typed into the active application
- ⚡ **Local Processing** - Runs Whisper locally on your machine - no internet required
- 🚀 **GPU Acceleration** - Vulkan support for faster transcription on compatible GPUs
- ⌨️ **Global Hotkey** - Works from anywhere, even when the app is minimized to the system tray
- 📊 **VU Meter** - Visual audio level indicator during recording
- 🌐 **Translation** - Optional translation to English using Whisper's built-in translation

## Requirements

- Windows 10 or later
- .NET 10.0 Runtime
- A local Whisper model (downloaded within the app)
- Optional: GPU with Vulkan support for faster transcription

## Installation

### From Source

1. Clone the repository:
   ```bash
   git clone https://github.com/bronder/whisperMeOff.git
   cd whisperMeOff
   ```

2. Build the project:
   ```bash
   dotnet build
   ```

3. Run the application:
   ```bash
   dotnet run
   ```

### Pre-built

Download the latest release from the [Releases](https://github.com/bronder/whisperMeOff/releases) page.

## Usage

1. Launch the application - it minimizes to the system tray
2. Right-click the tray icon and select **Settings** to configure:
   - **Whisper Tab**: Select and download a model size
   - **Hotkey Tab**: Set your preferred global hotkey (default: Ctrl+Shift+R)
   - **Audio Tab**: Select your microphone
3. Press your hotkey to start recording
4. Press it again to stop - your speech will be transcribed and typed into the previously focused app

### Tray Icon

- **Left-click**: Show/hide the main window
- **Right-click**: Context menu with Show, Settings, and Exit options

### Model Sizes

| Model | Size | Description |
|-------|------|-------------|
| tiny | ~75 MB | Fastest, lowest accuracy |
| base | ~150 MB | Good balance |
| small | ~500 MB | Recommended default |
| medium | ~1.5 GB | High accuracy, slower |

## Configuration

Settings are stored in `%APPDATA%\whisperMeOff\settings.json` and include:
- Selected Whisper model and folder
- Microphone device
- Global hotkey configuration
- Translation settings

## Building for Release

```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

This will create a self-contained executable in `bin/Release/net10.0-windows/win-x64/publish/`.

## Coming Soon

- [ ] **Push-to-talk** - Hold the hotkey to record, release to transcribe
- [ ] **Real Time** - Hold the hotkey to record and perform real-time vs end of chat batch.
- [ ] **Custom hotkeys** - Different hotkeys for different modes
- [ ] **Punctuation control** - Choose how punctuation is added
- [ ] **Dark/Light theme** - UI theme options
- [ ] **theming** - UI theming

## Technologies

- C# / .NET 10
- WPF (Windows Presentation Foundation)
- [Whisper.net](https://github.com/arianon/Whisper.net) - .NET bindings for Whisper
- NAudio - Audio capture

## License

MIT License - see [LICENSE](LICENSE) for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
