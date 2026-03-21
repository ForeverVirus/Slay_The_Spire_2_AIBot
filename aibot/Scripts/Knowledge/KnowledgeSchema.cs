namespace aibot.Scripts.Knowledge;

public static class KnowledgeSchema
{
    public const int DefaultJsonMaxBytes = 1_048_576;
    public const int DefaultMarkdownMaxBytes = 524_288;
    public const int DefaultStringMaxLength = 8_192;

    public static IReadOnlyDictionary<string, JsonKnowledgeFileRule> JsonFiles { get; } =
        new Dictionary<string, JsonKnowledgeFileRule>(StringComparer.OrdinalIgnoreCase)
        {
            ["characters_full.json"] = new("characters_full.json", typeof(List<CharacterGuideEntry>), true),
            ["builds_full.json"] = new("builds_full.json", typeof(List<BuildGuideEntry>), true),
            ["cards_full.json"] = new("cards_full.json", typeof(List<CardGuideEntry>), true),
            ["relics_full.json"] = new("relics_full.json", typeof(List<RelicGuideEntry>), true),
            ["characters.json"] = new("characters.json", typeof(List<CharacterGuideEntry>), true),
            ["builds.json"] = new("builds.json", typeof(List<BuildGuideEntry>), true),
            ["cards.json"] = new("cards.json", typeof(List<CardGuideEntry>), true),
            ["relics.json"] = new("relics.json", typeof(List<RelicGuideEntry>), true),
            ["potions.json"] = new("potions.json", typeof(List<PotionEntry>), false),
            ["powers.json"] = new("powers.json", typeof(List<PowerEntry>), false),
            ["enemies.json"] = new("enemies.json", typeof(List<EnemyEntry>), false),
            ["events.json"] = new("events.json", typeof(List<EventEntry>), false),
            ["enchantments.json"] = new("enchantments.json", typeof(List<EnchantmentEntry>), false),
            ["game_mechanics.json"] = new("game_mechanics.json", typeof(List<MechanicRule>), false),
            ["schema.json"] = new("schema.json", typeof(object), false)
        };

    public static IReadOnlySet<string> ReservedMarkdownFiles { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "00_OVERVIEW.md",
            "sts2_knowledge_base.md",
            "README.md"
        };

    public static bool IsGuideMarkdown(string fileName)
    {
        return fileName.EndsWith("_complete_guide.md", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !ReservedMarkdownFiles.Contains(fileName);
    }
}

public sealed record JsonKnowledgeFileRule(string FileName, Type ModelType, bool SupportsMerging);