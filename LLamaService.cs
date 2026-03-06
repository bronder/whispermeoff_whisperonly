using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace whisperMeOff;

public class LLamaService : IDisposable
{
    private LLamaWeights? _model;
    private LLamaContext? _context;
    private InteractiveExecutor? _executor;
    private ChatSession? _session;
    private bool _isLoaded = false;
    private string _modelPath = "";
    private readonly object _lock = new();

    public bool IsLoaded => _isLoaded;
    public string ModelPath => _modelPath;

    public async Task<bool> LoadModelAsync(string modelFolder, string modelFileName, IProgress<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(modelFileName))
        {
            progress?.Report("No model filename specified");
            return false;
        }

        var modelPath = Path.Combine(modelFolder, modelFileName);
        if (!File.Exists(modelPath))
        {
            progress?.Report($"Model file not found: {modelPath}");
            return false;
        }

        try
        {
            progress?.Report("Loading LLama model...");

            var parameters = new ModelParams(modelPath)
            {
                ContextSize = 2048,
                GpuLayerCount = 35,
                UseMemorymap = true
            };

            _model = await Task.Run(() => LLamaWeights.LoadFromFile(parameters));
            _context = await Task.Run(() => _model.CreateContext(parameters));
            _executor = new InteractiveExecutor(_context);

            var chatHistory = new ChatHistory();
            _session = new ChatSession(_executor, chatHistory);

            _modelPath = modelPath;
            _isLoaded = true;

            progress?.Report("LLama model loaded successfully");
            return true;
        }
        catch (Exception ex)
        {
            progress?.Report($"Failed to load model: {ex.Message}");
            return false;
        }
    }

    public async Task<string> InferAsync(string prompt, int maxTokens = 512, double temperature = 0.7, IProgress<string>? progress = null)
    {
        if (!_isLoaded || _session == null)
        {
            return "Error: LLama model not loaded";
        }

        try
        {
            var inferenceParams = new InferenceParams
            {
                MaxTokens = 256,
                AntiPrompts = new List<string> { "User:", "\n" },
                SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0.1f, RepeatPenalty = 1.2f }
            };

            var response = new System.Text.StringBuilder();

            await foreach (var text in _session.ChatAsync(new ChatHistory.Message(AuthorRole.User, prompt), inferenceParams))
            {
                response.Append(text);
                progress?.Report(text);
            }

            return response.ToString().Trim();
        }
        catch (Exception ex)
        {
            return $"Error during inference: {ex.Message}";
        }
    }

    public async Task<string> EnhanceTranscriptionAsync(string transcription, AppSettings settings, IProgress<string>? progress = null)
    {
        if (!_isLoaded)
        {
            return transcription;
        }

        var prompt = "Convert any file paths in this text to proper format. Output only the result, nothing else.\n\nText: " + transcription + "\nResult:";

        return await InferAsync(prompt, settings.LLamaMaxTokens, settings.LLamaTemperature, progress);
    }

    public void Unload()
    {
        lock (_lock)
        {
            _session = null;
            _executor = null;
            _context?.Dispose();
            _context = null;
            _model?.Dispose();
            _model = null;
            _isLoaded = false;
            _modelPath = "";
        }
    }

    public void Dispose()
    {
        Unload();
        GC.SuppressFinalize(this);
    }
}
