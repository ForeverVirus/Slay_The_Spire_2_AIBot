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

    [JsonPropertyName("source")]
    public string Source { get; set; } = "core";
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

    [JsonPropertyName("source")]
    public string Source { get; set; } = "core";
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

    [JsonPropertyName("descriptionZh")]
    public string? DescriptionZh { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "core";
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

    [JsonPropertyName("descriptionZh")]
    public string? DescriptionZh { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "core";
}

public sealed class PotionEntry
{
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("nameEn")]
    public string NameEn { get; set; } = string.Empty;

    [JsonPropertyName("nameZh")]
    public string? NameZh { get; set; }

    [JsonPropertyName("rarity")]
    public string? Rarity { get; set; }

    [JsonPropertyName("usage")]
    public string? Usage { get; set; }

    [JsonPropertyName("descriptionEn")]
    public string? DescriptionEn { get; set; }

    [JsonPropertyName("descriptionZh")]
    public string? DescriptionZh { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "core";
}

public sealed class PowerEntry
{
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("nameEn")]
    public string NameEn { get; set; } = string.Empty;

    [JsonPropertyName("nameZh")]
    public string? NameZh { get; set; }

    [JsonPropertyName("powerType")]
    public string? PowerType { get; set; }

    [JsonPropertyName("descriptionEn")]
    public string? DescriptionEn { get; set; }

    [JsonPropertyName("descriptionZh")]
    public string? DescriptionZh { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "core";
}

public sealed class EnemyMoveEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("titleEn")]
    public string? TitleEn { get; set; }

    [JsonPropertyName("titleZh")]
    public string? TitleZh { get; set; }

    [JsonPropertyName("banterEn")]
    public string? BanterEn { get; set; }

    [JsonPropertyName("banterZh")]
    public string? BanterZh { get; set; }
}

public sealed class EnemyEntry
{
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("nameEn")]
    public string NameEn { get; set; } = string.Empty;

    [JsonPropertyName("nameZh")]
    public string? NameZh { get; set; }

    [JsonPropertyName("descriptionEn")]
    public string? DescriptionEn { get; set; }

    [JsonPropertyName("descriptionZh")]
    public string? DescriptionZh { get; set; }

    [JsonPropertyName("moves")]
    public List<EnemyMoveEntry> Moves { get; set; } = new();

    [JsonPropertyName("source")]
    public string Source { get; set; } = "core";
}

public sealed class EventEntry
{
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("nameEn")]
    public string NameEn { get; set; } = string.Empty;

    [JsonPropertyName("nameZh")]
    public string? NameZh { get; set; }

    [JsonPropertyName("descriptionEn")]
    public string? DescriptionEn { get; set; }

    [JsonPropertyName("descriptionZh")]
    public string? DescriptionZh { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "core";
}

public sealed class EnchantmentEntry
{
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("nameEn")]
    public string NameEn { get; set; } = string.Empty;

    [JsonPropertyName("nameZh")]
    public string? NameZh { get; set; }

    [JsonPropertyName("descriptionEn")]
    public string? DescriptionEn { get; set; }

    [JsonPropertyName("descriptionZh")]
    public string? DescriptionZh { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "core";
}

public sealed class MechanicRule
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "core";
}
