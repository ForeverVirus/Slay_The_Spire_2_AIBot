using System.Text.Json.Serialization;

namespace aibot.Scripts.Knowledge;

public sealed class CharacterGuideEntry
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("nameEn")]
    public string NameEn { get; set; } = string.Empty;

    [JsonPropertyName("nameZh")]
    public string NameZh { get; set; } = string.Empty;

    [JsonPropertyName("playstyleEn")]
    public string? PlaystyleEn { get; set; }

    [JsonPropertyName("playstyleZh")]
    public string? PlaystyleZh { get; set; }
}

public sealed class BuildGuideEntry
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("nameEn")]
    public string NameEn { get; set; } = string.Empty;

    [JsonPropertyName("nameZh")]
    public string NameZh { get; set; } = string.Empty;

    [JsonPropertyName("characterId")]
    public int CharacterId { get; set; }

    [JsonPropertyName("tier")]
    public string? Tier { get; set; }

    [JsonPropertyName("summaryEn")]
    public string? SummaryEn { get; set; }

    [JsonPropertyName("summaryZh")]
    public string? SummaryZh { get; set; }

    [JsonPropertyName("strategyEn")]
    public string? StrategyEn { get; set; }

    [JsonPropertyName("strategyZh")]
    public string? StrategyZh { get; set; }

    [JsonPropertyName("tipsEn")]
    public string? TipsEn { get; set; }

    [JsonPropertyName("tipsZh")]
    public string? TipsZh { get; set; }
}

public sealed class CardGuideEntry
{
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("nameEn")]
    public string NameEn { get; set; } = string.Empty;

    [JsonPropertyName("nameZh")]
    public string? NameZh { get; set; }

    [JsonPropertyName("characterId")]
    public int CharacterId { get; set; }

    [JsonPropertyName("cardType")]
    public string? CardType { get; set; }

    [JsonPropertyName("descriptionEn")]
    public string? DescriptionEn { get; set; }
}

public sealed class RelicGuideEntry
{
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("nameEn")]
    public string NameEn { get; set; } = string.Empty;

    [JsonPropertyName("nameZh")]
    public string? NameZh { get; set; }

    [JsonPropertyName("characterId")]
    public int? CharacterId { get; set; }

    [JsonPropertyName("descriptionEn")]
    public string? DescriptionEn { get; set; }
}
