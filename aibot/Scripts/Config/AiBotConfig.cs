using System.Text.Json.Serialization;
using aibot.Scripts.Agent;

namespace aibot.Scripts.Config;

public sealed class AiBotConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("preferCloud")]
    public bool PreferCloud { get; set; } = true;

    [JsonPropertyName("autoTakeOverNewRun")]
    public bool AutoTakeOverNewRun { get; set; } = true;

    [JsonPropertyName("autoTakeOverContinueRun")]
    public bool AutoTakeOverContinueRun { get; set; } = true;

    [JsonPropertyName("pollIntervalMs")]
    public int PollIntervalMs { get; set; } = 250;

    [JsonPropertyName("decisionTimeoutSeconds")]
    public int DecisionTimeoutSeconds { get; set; } = 20;

    [JsonPropertyName("screenActionDelayMs")]
    public int ScreenActionDelayMs { get; set; } = 900;

    [JsonPropertyName("combatActionDelayMs")]
    public int CombatActionDelayMs { get; set; } = 650;

    [JsonPropertyName("mapActionDelayMs")]
    public int MapActionDelayMs { get; set; } = 1000;

    [JsonPropertyName("showDecisionPanel")]
    public bool ShowDecisionPanel { get; set; } = true;

    [JsonPropertyName("decisionPanelMaxEntries")]
    public int DecisionPanelMaxEntries { get; set; } = 16;

    [JsonPropertyName("provider")]
    public LlmProviderConfig Provider { get; set; } = new();

    [JsonPropertyName("logging")]
    public LoggingConfig Logging { get; set; } = new();

    [JsonPropertyName("agent")]
    public AgentRuntimeConfig Agent { get; set; } = new();

    [JsonPropertyName("knowledge")]
    public KnowledgeConfig Knowledge { get; set; } = new();

    [JsonPropertyName("ui")]
    public AgentUiConfig Ui { get; set; } = new();

    [JsonIgnore]
    public bool CanUseCloud =>
        PreferCloud &&
        !string.IsNullOrWhiteSpace(Provider.ApiKey) &&
        !string.IsNullOrWhiteSpace(Provider.Model) &&
        !string.IsNullOrWhiteSpace(Provider.BaseUrl);
}

public sealed class LlmProviderConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "deepseek";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "deepseek-chat";

    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; set; } = "https://api.deepseek.com";

    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class LoggingConfig
{
    [JsonPropertyName("verbose")]
    public bool Verbose { get; set; } = true;

    [JsonPropertyName("logDecisionPrompt")]
    public bool LogDecisionPrompt { get; set; }
}

public sealed class AgentRuntimeConfig
{
    [JsonPropertyName("defaultMode")]
    public string DefaultMode { get; set; } = "fullAuto";

    [JsonPropertyName("confirmOnModeSwitch")]
    public bool ConfirmOnModeSwitch { get; set; } = true;

    [JsonPropertyName("maxConversationHistory")]
    public int MaxConversationHistory { get; set; } = 50;

    public AgentMode GetDefaultMode()
    {
        return DefaultMode.Trim().ToLowerInvariant() switch
        {
            "fullauto" => AgentMode.FullAuto,
            "semiauto" => AgentMode.SemiAuto,
            "assist" => AgentMode.Assist,
            "qna" => AgentMode.QnA,
            _ => AgentMode.FullAuto
        };
    }
}

public sealed class KnowledgeConfig
{
    [JsonPropertyName("enableCustom")]
    public bool EnableCustom { get; set; } = true;

    [JsonPropertyName("customDir")]
    public string CustomDir { get; set; } = "custom";

    [JsonPropertyName("maxCustomFileSize")]
    public int MaxCustomFileSize { get; set; } = 262144;
}

public sealed class AgentUiConfig
{
    [JsonPropertyName("showChatDialog")]
    public bool ShowChatDialog { get; set; } = true;

    [JsonPropertyName("chatHotkey")]
    public string ChatHotkey { get; set; } = "Tab";

    [JsonPropertyName("modeHotkeys")]
    public AgentModeHotkeyConfig ModeHotkeys { get; set; } = new();

    [JsonPropertyName("showModePanel")]
    public bool ShowModePanel { get; set; } = true;

    [JsonPropertyName("modePanelHotkey")]
    public string ModePanelHotkey { get; set; } = "F4";

    [JsonPropertyName("modePanelStartVisible")]
    public bool ModePanelStartVisible { get; set; } = true;

    [JsonPropertyName("showRecommendOverlay")]
    public bool ShowRecommendOverlay { get; set; } = true;
}

public sealed class AgentModeHotkeyConfig
{
    [JsonPropertyName("fullAuto")]
    public string FullAuto { get; set; } = "F5";

    [JsonPropertyName("semiAuto")]
    public string SemiAuto { get; set; } = "F6";

    [JsonPropertyName("assist")]
    public string Assist { get; set; } = "F7";

    [JsonPropertyName("qna")]
    public string QnA { get; set; } = "F8";

    public string GetHotkey(AgentMode mode)
    {
        return mode switch
        {
            AgentMode.FullAuto => FullAuto,
            AgentMode.SemiAuto => SemiAuto,
            AgentMode.Assist => Assist,
            AgentMode.QnA => QnA,
            _ => string.Empty
        };
    }
}
