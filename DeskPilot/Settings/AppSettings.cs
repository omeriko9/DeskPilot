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
    public int MaxSteps { get; init; } = 30;
    public int StepDelayMs { get; init; } = 500; // artificial delay between executed steps
    public bool ShowProgressOverlay { get; init; } = true; // show floating UI with thinking/step updates
    public bool DebugConsole { get; init; } = true; // allocate/show console window (if detached) for diagnostics
    public string LlmProvider { get; init; } = "remote"; // default switched to remote proxy (python)
    public string RemoteUrl { get; init; } = "http://localhost:8009/"; // python FastAPI root endpoint

    [JsonIgnore]
    public string SettingsPath => Path.Combine(AppContext.BaseDirectory, SettingsFolder, SettingsFileName);

    public bool KeyboardOnlyMode { get; init; } = false;

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

        if (settings.LlmProvider.Equals("local", StringComparison.OrdinalIgnoreCase))
        {
            settings = settings with { BaseUrl = NormalizeBaseUrl(settings.BaseUrl) };
        }
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
