using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;
using Whisper.net.Logger;

namespace whisperMeOff;

public class SpeechRecognitionService : IDisposable
{
    private bool _isListening;
    private string _recognizedText = "";
    private int _deviceIndex;
    private CancellationTokenSource? _cts;

    // For WAV recording
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _waveWriter;
    private string? _tempWavPath;

    // For Whisper via Ollama
    private string _whisperOllamaUrl = "";
    private string _whisperModel = "";
    
    // Use Whisper.net built-in translation
    public bool TranslateWithWhisper { get; set; } = false;
    public string WhisperTranslationTarget { get; set; } = "en";
    
    // Path to local Whisper model file (full path). If null or empty, default path is used.
    public string? LocalWhisperModelPath { get; set; }
    
    // Flag to signal recording should stop (separate from cancellation token)
    private bool _stopRecordingRequested = false;

    public event EventHandler<string>? TextRecognized;
    public event EventHandler? ListeningStarted;
    public event EventHandler? ListeningStopped;
    public event EventHandler<int>? AudioLevelChanged;

    public bool IsListening => _isListening;

    /// <summary>
    /// Configure Whisper via Ollama for transcription.
    /// </summary>
    public void ConfigureWhisper(string ollamaUrl, string model)
    {
        _whisperOllamaUrl = ollamaUrl.TrimEnd('/');
        _whisperModel = model;
    }

    /// <summary>
    /// Check if Whisper via Ollama or local Whisper.net is configured.
    /// </summary>
    public bool IsWhisperConfigured => !string.IsNullOrEmpty(_whisperModel) || !string.IsNullOrEmpty(LocalWhisperModelPath);

    /// <summary>
    /// Gets the list of available audio input devices.
    /// </summary>
    public static List<(int index, string name)> GetAvailableDevices()
    {
        var devices = new List<(int, string)>();
        try
        {
            var waveInDevices = WaveIn.DeviceCount;
            for (int i = 0; i < waveInDevices; i++)
            {
                var capabilities = WaveIn.GetCapabilities(i);
                devices.Add((i, capabilities.ProductName));
            }
        }
        catch
        {
            // Return empty list if we can't enumerate devices
        }
        return devices;
    }

    public void StopRecognition()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[Speech] StopRecognition called");
            
            // Signal recording to stop (don't cancel the token - we need it for API call)
            _stopRecordingRequested = true;
            
            // Stop Whisper/WAV recording if active
            if (_waveIn != null)
            {
                System.Diagnostics.Debug.WriteLine("[Speech] Stopping WaveIn recording");
                try
                {
                    _waveIn.StopRecording();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[Speech] Error stopping WaveIn: " + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[Speech] Error in StopRecognition: " + ex.Message);
        }
    }

    private void Cleanup()
    {
        // Nothing to clean up for Whisper-only mode
    }

    public void Dispose()
    {
        Cleanup();
    }

    /// <summary>
    /// Records audio to a temporary WAV file using NAudio.
    /// </summary>
    private async Task<string> RecordAudioToWavAsync(int deviceIndex, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"whisperMeOff_{DateTime.Now:yyyyMMddHHmmss}.wav");
        _tempWavPath = tempPath;

        var tcs = new TaskCompletionSource<bool>();
        Exception? recordingError = null;

        // Register cancellation to stop recording
        cancellationToken.Register(() =>
        {
            if (_waveIn != null)
            {
                try
                {
                    _waveIn.StopRecording();
                }
                catch
                {
                    // Ignore
                }
            }
            tcs.TrySetResult(true);
        });

        try
        {
            // Create WaveInEvent with specified device or default
            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 1), // 16kHz mono for Whisper
                DeviceNumber = deviceIndex >= 0 ? deviceIndex : 0
            };

            // Create WAV file writer
            _waveWriter = new WaveFileWriter(tempPath, _waveIn.WaveFormat);

            // Handle incoming data - also check for stop flag
            _waveIn.DataAvailable += (s, e) =>
            {
                if (_waveWriter != null)
                {
                    _waveWriter.Write(e.Buffer, 0, e.BytesRecorded);
                }
                
                // Calculate audio level from the buffer
                // Convert bytes to samples and calculate RMS
                int bytesPerSample = _waveIn.WaveFormat.BitsPerSample / 8;
                int sampleCount = e.BytesRecorded / bytesPerSample;
                if (sampleCount > 0 && _waveIn.WaveFormat.BitsPerSample == 16) // 16-bit audio
                {
                    unsafe
                    {
                        fixed (byte* bufferPtr = e.Buffer)
                        {
                            short* samples = (short*)bufferPtr;
                            double sum = 0;
                            for (int i = 0; i < sampleCount; i++)
                            {
                                double sample = samples[i] / 32768.0; // Normalize to -1 to 1
                                sum += sample * sample;
                            }
                            double rms = Math.Sqrt(sum / sampleCount);
                            // Convert to 0-100 scale (logarithmic for better visual)
                            int level = (int)(20 * Math.Log10(rms + 0.0001) + 60); // Shift and scale
                            level = Math.Max(0, Math.Min(100, level)); // Clamp to 0-100
                            AudioLevelChanged?.Invoke(this, level);
                        }
                    }
                }
                
                // Check if stop was requested
                if (_stopRecordingRequested)
                {
                    System.Diagnostics.Debug.WriteLine("[Whisper] Stop requested, stopping recording");
                    _waveIn.StopRecording();
                }
            };

            _waveIn.RecordingStopped += (s, e) =>
            {
                if (e.Exception != null)
                {
                    recordingError = e.Exception;
                }
                tcs.TrySetResult(true);
            };

            System.Diagnostics.Debug.WriteLine("[Whisper] Starting recording to: " + tempPath);
            ListeningStarted?.Invoke(this, EventArgs.Empty);

            // Start recording
            _waveIn.StartRecording();

            // Wait for recording to stop (via cancellation or user stopped)
            await tcs.Task;

            // Ensure WAV writer is properly finished before transcribing
            _waveWriter?.Flush();
            
            if (recordingError != null)
            {
                throw recordingError;
            }

            System.Diagnostics.Debug.WriteLine("[Whisper] Recording complete, file: " + tempPath);
            return tempPath;
        }
        finally
        {
            _waveIn?.Dispose();
            _waveIn = null;
            _waveWriter?.Dispose();
            _waveWriter = null;
        }
    }

    /// <summary>
    /// Transcribes audio using Whisper.net library.
    /// </summary>
    private async Task<string> TranscribeWithWhisperAsync(string wavFilePath, CancellationToken cancellationToken)
    {
        // Use the new WhisperNetService helper which supports translation
        var modelPath = LocalWhisperModelPath;
        if (string.IsNullOrEmpty(modelPath))
        {
            modelPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "whisperMeOff", "models", "ggml-small.bin");
        }

        // Ensure model exists (download if not)
        if (!File.Exists(modelPath))
        {
            System.Diagnostics.Debug.WriteLine("[Whisper] Downloading model: " + modelPath);
            var fileName = Path.GetFileName(modelPath);
            var folder = Path.GetDirectoryName(modelPath) ?? Path.GetDirectoryName(Environment.CurrentDirectory)!;
            Directory.CreateDirectory(folder);
            await DownloadModelAsync(modelPath, cancellationToken);
        }

        try
        {
            using var fileStream = File.OpenRead(wavFilePath);
            using var whisperService = new WhisperNetService(modelPath);

            var transcription = await whisperService.ProcessAudioAsync(fileStream, TranslateWithWhisper, WhisperTranslationTarget, cancellationToken);
            System.Diagnostics.Debug.WriteLine("[Whisper] Result: " + transcription);
            return transcription.Trim();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[Whisper] Error: " + ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Downloads the Whisper model if not present.
    /// </summary>
    private async Task DownloadModelAsync(string modelName, CancellationToken cancellationToken)
    {
        try
        {
            // Map model name to GgmlType
            var ggmlType = modelName switch
            {
                "ggml-tiny.bin" => GgmlType.Tiny,
                "ggml-base.bin" => GgmlType.Base,
                "ggml-small.bin" => GgmlType.Small,
                "ggml-medium.bin" => GgmlType.Medium,
                _ => GgmlType.Small
            };
            
            System.Diagnostics.Debug.WriteLine("[Whisper] Downloading Whisper model: " + ggmlType);
            
            await using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(ggmlType);
            await using var fileWriter = File.OpenWrite(modelName);
            await modelStream.CopyToAsync(fileWriter, cancellationToken);
            
            System.Diagnostics.Debug.WriteLine("[Whisper] Model downloaded successfully");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[Whisper] Model download error: " + ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Public helper to download a specified model file to a target path.
    /// </summary>
    public static async Task DownloadModelToPathAsync(string targetFilePath, CancellationToken cancellationToken = default)
    {
        // Map filename to GgmlType
        var fileName = Path.GetFileName(targetFilePath);
        var ggmlType = fileName switch
        {
            "ggml-tiny.bin" => GgmlType.Tiny,
            "ggml-base.bin" => GgmlType.Base,
            "ggml-small.bin" => GgmlType.Small,
            "ggml-medium.bin" => GgmlType.Medium,
            _ => GgmlType.Small
        };

        Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath) ?? Path.GetDirectoryName(Environment.CurrentDirectory)!);

        await using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(ggmlType);
        await using var fileWriter = File.OpenWrite(targetFilePath);
        await modelStream.CopyToAsync(fileWriter, cancellationToken);
    }

    /// <summary>
    /// Recognizes speech using Whisper via Ollama (records audio first, then transcribes).
    /// </summary>
    public async Task<string> RecognizeSpeechWithWhisperAsync(int deviceIndex = -1, CancellationToken cancellationToken = default)
    {
        if (!IsWhisperConfigured)
        {
            return "Whisper not configured. Please set up Whisper via Ollama in settings.";
        }

        _recognizedText = "";
        _isListening = true;
        _deviceIndex = deviceIndex;
        _stopRecordingRequested = false;
        
        // Create a CTS for recording only (not for API call)
        using var recordingCts = new CancellationTokenSource();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, recordingCts.Token);

        try
        {
            // Record audio to WAV file using recordingCts (can be cancelled by StopRecognition)
            var wavPath = await RecordAudioToWavAsync(deviceIndex, recordingCts.Token);

            try
            {
                // Send to Whisper for transcription using original cancellationToken (not the one we might have cancelled)
                // Use CancellationToken.None to ensure the API call is not cancelled
                var transcription = await TranscribeWithWhisperAsync(wavPath, CancellationToken.None);
                _recognizedText = transcription.Trim();
                System.Diagnostics.Debug.WriteLine("[Whisper] Final transcription: " + _recognizedText);
                TextRecognized?.Invoke(this, _recognizedText);
                return _recognizedText;
            }
            finally
            {
                // Clean up temp WAV file
                try
                {
                    if (File.Exists(wavPath))
                    {
                        File.Delete(wavPath);
                        System.Diagnostics.Debug.WriteLine("[Whisper] Deleted temp file: " + wavPath);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
        catch (OperationCanceledException)
        {
            return _recognizedText.Trim();
        }
        catch (Exception ex)
        {
            return $"Whisper transcription error: {ex.Message}";
        }
        finally
        {
            _isListening = false;
            ListeningStopped?.Invoke(this, EventArgs.Empty);
            _cts?.Dispose();
            _cts = null;
        }
    }
}
