using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopAssist.Settings;

public sealed record AppSettings
{
    private const string SettingsFolder = "";
    private const string SettingsFileName = "settings.json";

    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "gpt-4.1";
    public string BaseUrl { get; init; } = "https://api.openai.com/v1/";
    public string DefaultBrowser { get; init; } = "";
    public string CustomLlmInstructions { get; init; } = string.Empty;
    public int MaxSteps { get; init; } = 12;
    public bool PlayBeepOnCompletion { get; init; } = true;
    
    // New tuning / diagnostics settings
    public int RequestTimeoutSeconds { get; init; } = 60; // per LLM request
    public bool VerboseNetworkLogging { get; init; } = true; // logs sizes & timings
    public bool ReducedScreenshotMode { get; init; } = true; // downscale & compress aggressively
    public int ScreenshotMaxWidth { get; init; } = 1024; // default max dimensions when ReducedScreenshotMode
    public int ScreenshotMaxHeight { get; init; } = 640;
    public int ScreenshotJpegQuality { get; init; } = 65; // 1-100
    public int MaxCompletionTokens { get; init; } = 2320; // keep JSON concise
    public int StepDelayMs { get; init; } = 500; // artificial delay between executed steps
    public bool ForceEnglishLayoutForTyping { get; init; } = true; // ensure EN layout while writing
    public string EnglishLayoutHex { get; init; } = "00000409"; // default US English layout id
    public bool EnablePostCompletionVerification { get; init; } = false; // verify after completion via screenshot
    public int VerificationMaxAttempts { get; init; } = 2; // number of verification passes (including first) before giving up
    public bool ShowProgressOverlay { get; init; } = true; // show floating UI with thinking/step updates
    public bool DebugConsole { get; init; } = true; // allocate/show console window (if detached) for diagnostics

    // Adaptive / performance tuning additions
    public int InitialStepMaxTokens { get; init; } = 700; // step 0 upper bound
    public int FollowupStepMaxTokens { get; init; } = 320; // subsequent steps bound
    public bool EnableAdaptiveTokenScaling { get; init; } = true; // shrink after latency
    public int AdaptiveLatencyMsThreshold { get; init; } = 30000; // if header latency > threshold shrink budget
    public int ConstrainedRetryMaxTokens { get; init; } = 400; // used after empty-content length exhaustion
    public string FallbackModel { get; init; } = "gpt-4o"; // faster non-reasoning model
    public bool UseJsonSchemaResponseFormat { get; init; } = true; // request JSON schema if supported
    public int EarlyHeaderCutoffMs { get; init; } = 30000; // abort & retry sooner if no headers by then
    public int ReuseScreenshotWithinMs { get; init; } = 1800; // reuse last screenshot if within this window
    public int FollowupScreenshotMaxWidth { get; init; } = 640; // extra downscale for followups
    public int FollowupScreenshotMaxHeight { get; init; } = 400;

    [JsonIgnore]
    public string SettingsPath => Path.Combine(AppContext.BaseDirectory, SettingsFolder, SettingsFileName);

    public static AppSettings Load()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, SettingsFolder);
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, SettingsFileName);

        AppSettings settings;
        if (File.Exists(file))
        {
            try
            {
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), JsonOptions()) ?? new AppSettings();
                
            }
            catch
            {
                settings = new AppSettings();
            }
        }
        else
        {
            settings = new AppSettings();
            File.WriteAllText(file, JsonSerializer.Serialize(settings, JsonOptions(new JsonSerializerOptions { WriteIndented = true })));
        }

        // Env var fallback
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            var env = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (!string.IsNullOrWhiteSpace(env))
            {
                settings = settings with { ApiKey = env };
            }
        }

        settings = settings with { BaseUrl = NormalizeBaseUrl(settings.BaseUrl) };
        return settings;
    }

    private static string NormalizeBaseUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "https://api.openai.com/v1/";
        url = url.Trim();
        if (!url.EndsWith('/')) url += '/';
        return url;
    }

    private static JsonSerializerOptions JsonOptions(JsonSerializerOptions? baseOptions = null)
    {
        var opt = baseOptions ?? new JsonSerializerOptions();
        opt.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        opt.ReadCommentHandling = JsonCommentHandling.Skip;
        opt.AllowTrailingCommas = true;
        return opt;
    }
}
