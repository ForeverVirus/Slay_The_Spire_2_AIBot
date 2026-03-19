using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;

namespace aibot.Scripts.Config;

public static class AiBotConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public static AiBotConfig Load(string modDirectory)
    {
        var configPath = Path.Combine(modDirectory, "config.json");
        if (!File.Exists(configPath))
        {
            var config = new AiBotConfig();
            Save(configPath, config);
            Log.Warn($"[AiBot] config.json not found. Created default config at {configPath}");
            return config;
        }

        try
        {
            var json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<AiBotConfig>(json, JsonOptions) ?? new AiBotConfig();
        }
        catch (Exception ex)
        {
            Log.Error($"[AiBot] Failed to read config.json: {ex}");
            return new AiBotConfig();
        }
    }

    public static void Save(string configPath, AiBotConfig config)
    {
        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(configPath, JsonSerializer.Serialize(config, JsonOptions));
    }
}
