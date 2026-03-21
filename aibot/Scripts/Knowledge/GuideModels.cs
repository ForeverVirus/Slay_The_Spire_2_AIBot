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

    [JsonPropertyName("targetType")]
    public string? TargetType { get; set; }

    [JsonPropertyName("effectFormulaEn")]
    public string? EffectFormulaEn { get; set; }

    [JsonPropertyName("effectFormulaZh")]
    public string? EffectFormulaZh { get; set; }

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

    [JsonPropertyName("stackType")]
    public string? StackType { get; set; }

    [JsonPropertyName("stackRuleEn")]
    public string? StackRuleEn { get; set; }

    [JsonPropertyName("stackRuleZh")]
    public string? StackRuleZh { get; set; }

    [JsonPropertyName("resolutionRuleEn")]
    public string? ResolutionRuleEn { get; set; }

    [JsonPropertyName("resolutionRuleZh")]
    public string? ResolutionRuleZh { get; set; }

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

    [JsonPropertyName("hpRange")]
    public string? HpRange { get; set; }

    [JsonPropertyName("intentPatternEn")]
    public string? IntentPatternEn { get; set; }

    [JsonPropertyName("intentPatternZh")]
    public string? IntentPatternZh { get; set; }

    [JsonPropertyName("specialMechanicsEn")]
    public string? SpecialMechanicsEn { get; set; }

    [JsonPropertyName("specialMechanicsZh")]
    public string? SpecialMechanicsZh { get; set; }

    [JsonPropertyName("threatSummaryEn")]
    public string? ThreatSummaryEn { get; set; }

    [JsonPropertyName("threatSummaryZh")]
    public string? ThreatSummaryZh { get; set; }

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

    [JsonPropertyName("triggerRestrictionEn")]
    public string? TriggerRestrictionEn { get; set; }

    [JsonPropertyName("triggerRestrictionZh")]
    public string? TriggerRestrictionZh { get; set; }

    [JsonPropertyName("options")]
    public List<EventOptionGuideEntry> Options { get; set; } = new();

    [JsonPropertyName("source")]
    public string Source { get; set; } = "core";
}

public sealed class EventOptionGuideEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("titleEn")]
    public string? TitleEn { get; set; }

    [JsonPropertyName("titleZh")]
    public string? TitleZh { get; set; }

    [JsonPropertyName("resultEn")]
    public string? ResultEn { get; set; }

    [JsonPropertyName("resultZh")]
    public string? ResultZh { get; set; }
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
