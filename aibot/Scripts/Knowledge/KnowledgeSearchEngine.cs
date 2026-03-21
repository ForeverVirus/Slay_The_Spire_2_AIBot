using System.Text;
using aibot.Scripts.Decision;

namespace aibot.Scripts.Knowledge;

public sealed class KnowledgeSearchEngine
{
    private readonly GuideKnowledgeBase _knowledgeBase;
    private static readonly string[] CardMarkers = { "卡牌", "card", "牌" };
    private static readonly string[] RelicMarkers = { "遗物", "relic" };
    private static readonly string[] BuildMarkers = { "构筑", "build", "流派", "套路" };
    private static readonly string[] PotionMarkers = { "药水", "potion", "瓶" };
    private static readonly string[] EnemyMarkers = { "敌人", "enemy", "怪", "boss" };
    private static readonly string[] PowerMarkers = { "power", "buff", "debuff", "状态", "增益", "减益" };
    private static readonly string[] EventMarkers = { "事件", "event", "选项" };
    private static readonly string[] EnchantmentMarkers = { "附魔", "enchantment" };

    public KnowledgeSearchEngine(GuideKnowledgeBase knowledgeBase)
    {
        _knowledgeBase = knowledgeBase;
    }

    public KnowledgeAnswer Search(string question, RunAnalysis analysis)
    {
        var query = question?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            return KnowledgeAnswer.Empty;
        }

        var sections = new List<string>();
        var sources = new List<string>();
        var normalized = GuideKnowledgeBase.Normalize(query);
        var terms = GuideKnowledgeBase.TokenizeSearchTerms(query);
        var cardName = ExtractNamedTarget(query, CardMarkers);
        var relicName = ExtractNamedTarget(query, RelicMarkers);
        var buildName = ExtractNamedTarget(query, BuildMarkers);
        var potionName = ExtractNamedTarget(query, PotionMarkers);
        var enemyName = ExtractNamedTarget(query, EnemyMarkers);
        var powerName = ExtractNamedTarget(query, PowerMarkers);
        var eventName = ExtractNamedTarget(query, EventMarkers);
        var enchantmentName = ExtractNamedTarget(query, EnchantmentMarkers);

        var card = ResolveCard(cardName, terms, analysis.CharacterId);
        if (card is not null)
        {
            sections.Add(BuildCardSection(card));
            sources.Add($"card:{card.Slug}:{card.Source}");
        }

        var relic = ResolveRelic(relicName, terms, analysis.CharacterId);
        if (relic is not null)
        {
            sections.Add(BuildRelicSection(relic));
            sources.Add($"relic:{relic.Slug}:{relic.Source}");
        }

        var potion = ResolvePotion(potionName, terms);
        if (potion is not null)
        {
            sections.Add(BuildPotionSection(potion));
            sources.Add($"potion:{potion.Slug}:{potion.Source}");
        }

        var enemy = ResolveEnemy(enemyName, terms);
        if (enemy is not null)
        {
            sections.Add(BuildEnemySection(enemy));
            sources.Add($"enemy:{enemy.Slug}:{enemy.Source}");
        }

        var power = ResolvePower(powerName, terms);
        if (power is not null)
        {
            sections.Add(BuildPowerSection(power));
            sources.Add($"power:{power.Slug}:{power.Source}");
        }

        var gameEvent = ResolveEvent(eventName, terms);
        if (gameEvent is not null)
        {
            sections.Add(BuildEventSection(gameEvent));
            sources.Add($"event:{gameEvent.Slug}:{gameEvent.Source}");
        }

        var enchantment = ResolveEnchantment(enchantmentName, terms);
        if (enchantment is not null)
        {
            sections.Add(BuildEnchantmentSection(enchantment));
            sources.Add($"enchantment:{enchantment.Slug}:{enchantment.Source}");
        }

        var buildMatches = FindBuildMatches(buildName ?? query, analysis.CharacterId);
        if (buildMatches.Count > 0)
        {
            sections.Add(BuildBuildSection(buildMatches));
            sources.AddRange(buildMatches.Select(build => $"build:{build.Slug}:{build.Source}"));
        }

        if (LooksLikeCharacterQuestion(normalized))
        {
            var characterBrief = _knowledgeBase.BuildCharacterBrief(analysis.CharacterId);
            var characterGuide = _knowledgeBase.BuildCharacterGuideSummary(analysis.CharacterId);
            var characterSection = ComposeSection("角色攻略", characterBrief, characterGuide);
            if (!string.IsNullOrWhiteSpace(characterSection))
            {
                sections.Add(characterSection);
                sources.Add($"character:{analysis.CharacterId}");
            }
        }

        if (LooksLikeMechanicsQuestion(normalized))
        {
            var rules = _knowledgeBase.SearchMechanicRules(query, 4);
            if (rules.Count > 0)
            {
                sections.Add(BuildMechanicsSection(rules));
                sources.AddRange(rules.Select(rule => $"mechanic:{rule.Id}:{rule.Source}"));
            }

            if (!string.IsNullOrWhiteSpace(_knowledgeBase.CoreMechanicsSummary))
            {
                sections.Add("核心机制：\n" + _knowledgeBase.CoreMechanicsSummary);
                sources.Add("mechanics:core");
            }
        }

        var snippets = ExtractSnippets(query, terms, analysis);
        if (snippets.Count > 0)
        {
            sections.Add("相关知识片段：\n" + string.Join("\n", snippets.Select(snippet => $"- {snippet}")));
            sources.Add("markdown:snippets");
        }

        if (sections.Count == 0)
        {
            var fallback = BuildFallbackAnswer(analysis);
            return new KnowledgeAnswer(false, fallback, Array.Empty<string>());
        }

        var answer = string.Join("\n\n", sections.Distinct());
        return new KnowledgeAnswer(true, Trim(answer, 3200), sources.Distinct().ToList());
    }

    private List<BuildGuideEntry> FindBuildMatches(string query, int characterId)
    {
        return _knowledgeBase.FindBuilds(query, characterId, 3).ToList();
    }

    private List<string> ExtractSnippets(string query, IReadOnlyList<string> terms, RunAnalysis analysis)
    {
        var candidates = terms
            .Concat(analysis.DeckCardNames.Take(4))
            .Concat(analysis.RelicNames.Take(3))
            .Concat(analysis.PotionNames.Take(2))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Take(6)
            .ToList();

        if (candidates.Count == 0)
        {
            return new List<string>();
        }

        return _knowledgeBase.SearchMarkdownSnippets(candidates, 6, 180, analysis.CharacterId)
            .Distinct()
            .Take(6)
            .ToList();
    }

    private CardGuideEntry? ResolveCard(string? explicitName, IReadOnlyList<string> terms, int characterId)
    {
        if (!string.IsNullOrWhiteSpace(explicitName))
        {
            return _knowledgeBase.FindCard(explicitName, characterId) ?? _knowledgeBase.FindCard(explicitName);
        }

        return terms
            .Select(term => _knowledgeBase.FindCard(term, characterId) ?? _knowledgeBase.FindCard(term))
            .FirstOrDefault(card => card is not null);
    }

    private RelicGuideEntry? ResolveRelic(string? explicitName, IReadOnlyList<string> terms, int characterId)
    {
        if (!string.IsNullOrWhiteSpace(explicitName))
        {
            return _knowledgeBase.FindRelic(explicitName, characterId) ?? _knowledgeBase.FindRelic(explicitName);
        }

        return terms
            .Select(term => _knowledgeBase.FindRelic(term, characterId) ?? _knowledgeBase.FindRelic(term))
            .FirstOrDefault(relic => relic is not null);
    }

    private PotionEntry? ResolvePotion(string? explicitName, IReadOnlyList<string> terms)
    {
        if (!string.IsNullOrWhiteSpace(explicitName))
        {
            return _knowledgeBase.FindPotion(explicitName);
        }

        return terms.Select(_knowledgeBase.FindPotion).FirstOrDefault(potion => potion is not null);
    }

    private EnemyEntry? ResolveEnemy(string? explicitName, IReadOnlyList<string> terms)
    {
        if (!string.IsNullOrWhiteSpace(explicitName))
        {
            return _knowledgeBase.FindEnemy(explicitName);
        }

        return terms.Select(_knowledgeBase.FindEnemy).FirstOrDefault(enemy => enemy is not null);
    }

    private PowerEntry? ResolvePower(string? explicitName, IReadOnlyList<string> terms)
    {
        if (!string.IsNullOrWhiteSpace(explicitName))
        {
            return _knowledgeBase.FindPower(explicitName);
        }

        return terms.Select(_knowledgeBase.FindPower).FirstOrDefault(power => power is not null);
    }

    private EventEntry? ResolveEvent(string? explicitName, IReadOnlyList<string> terms)
    {
        if (!string.IsNullOrWhiteSpace(explicitName))
        {
            return _knowledgeBase.FindEvent(explicitName);
        }

        return terms.Select(_knowledgeBase.FindEvent).FirstOrDefault(entry => entry is not null);
    }

    private EnchantmentEntry? ResolveEnchantment(string? explicitName, IReadOnlyList<string> terms)
    {
        if (!string.IsNullOrWhiteSpace(explicitName))
        {
            return _knowledgeBase.FindEnchantment(explicitName);
        }

        return terms.Select(_knowledgeBase.FindEnchantment).FirstOrDefault(entry => entry is not null);
    }

    private static string BuildCardSection(CardGuideEntry card)
    {
        var lines = new List<string>
        {
            $"卡牌：{card.NameEn} / {card.NameZh}",
            $"类型：{card.CardType ?? "Unknown"}",
            $"来源：{card.Source}"
        };

        if (!string.IsNullOrWhiteSpace(card.DescriptionEn))
        {
            lines.Add($"描述(EN)：{KnowledgeTextFormatter.FormatCardText(card, card.DescriptionEn)}");
        }

        if (!string.IsNullOrWhiteSpace(card.DescriptionZh))
        {
            lines.Add($"描述(ZH)：{KnowledgeTextFormatter.FormatCardText(card, card.DescriptionZh)}");
        }

        return string.Join("\n", lines);
    }

    private static string BuildRelicSection(RelicGuideEntry relic)
    {
        var lines = new List<string>
        {
            $"遗物：{relic.NameEn} / {relic.NameZh}",
            $"来源：{relic.Source}"
        };

        if (!string.IsNullOrWhiteSpace(relic.DescriptionEn))
        {
            lines.Add($"描述(EN)：{KnowledgeTextFormatter.FormatRelicText(relic, relic.DescriptionEn)}");
        }

        if (!string.IsNullOrWhiteSpace(relic.DescriptionZh))
        {
            lines.Add($"描述(ZH)：{KnowledgeTextFormatter.FormatRelicText(relic, relic.DescriptionZh)}");
        }

        return string.Join("\n", lines);
    }

    private static string BuildPotionSection(PotionEntry potion)
    {
        var lines = new List<string>
        {
            $"药水：{potion.NameEn} / {potion.NameZh}",
            $"来源：{potion.Source}"
        };

        if (!string.IsNullOrWhiteSpace(potion.Rarity))
        {
            lines.Add($"稀有度：{potion.Rarity}");
        }

        if (!string.IsNullOrWhiteSpace(potion.Usage))
        {
            lines.Add($"使用建议：{KnowledgeTextFormatter.FormatPlainText(potion.Usage)}");
        }

        if (!string.IsNullOrWhiteSpace(potion.DescriptionZh))
        {
            lines.Add($"描述(ZH)：{KnowledgeTextFormatter.FormatPotionText(potion, potion.DescriptionZh)}");
        }

        if (!string.IsNullOrWhiteSpace(potion.DescriptionEn))
        {
            lines.Add($"描述(EN)：{KnowledgeTextFormatter.FormatPotionText(potion, potion.DescriptionEn)}");
        }

        return string.Join("\n", lines);
    }

    private static string BuildEnemySection(EnemyEntry enemy)
    {
        var lines = new List<string>
        {
            $"敌人：{enemy.NameEn} / {enemy.NameZh}",
            $"来源：{enemy.Source}"
        };

        if (!string.IsNullOrWhiteSpace(enemy.DescriptionZh))
        {
            lines.Add($"描述(ZH)：{KnowledgeTextFormatter.FormatPlainText(enemy.DescriptionZh)}");
        }

        if (!string.IsNullOrWhiteSpace(enemy.DescriptionEn))
        {
            lines.Add($"描述(EN)：{KnowledgeTextFormatter.FormatPlainText(enemy.DescriptionEn)}");
        }

        if (enemy.Moves.Count > 0)
        {
            lines.Add("技能：");
            foreach (var move in enemy.Moves.Take(8))
            {
                var title = string.IsNullOrWhiteSpace(move.TitleZh)
                    ? move.TitleEn ?? move.Id
                    : $"{move.TitleEn} / {move.TitleZh}";
                var entry = string.IsNullOrWhiteSpace(move.Id)
                    ? title
                    : $"{move.Id}: {title}";
                var banter = string.IsNullOrWhiteSpace(move.BanterZh) ? move.BanterEn : move.BanterZh;
                if (!string.IsNullOrWhiteSpace(banter))
                {
                    entry += $"；台词：{Trim(KnowledgeTextFormatter.FormatPlainText(banter), 80)}";
                }

                lines.Add($"- {entry}");
            }

            if (enemy.Moves.Count > 8)
            {
                lines.Add($"- 其余 {enemy.Moves.Count - 8} 个技能省略");
            }
        }

        return string.Join("\n", lines);
    }

    private static string BuildPowerSection(PowerEntry power)
    {
        var lines = new List<string>
        {
            $"状态效果：{power.NameEn} / {power.NameZh}",
            $"来源：{power.Source}"
        };

        if (!string.IsNullOrWhiteSpace(power.PowerType))
        {
            lines.Add($"类型：{power.PowerType}");
        }

        if (!string.IsNullOrWhiteSpace(power.StackType))
        {
            lines.Add($"堆叠类型：{KnowledgeTextFormatter.FormatPlainText(power.StackType)}");
        }

        if (!string.IsNullOrWhiteSpace(power.StackRuleZh))
        {
            lines.Add($"叠加规则(ZH)：{KnowledgeTextFormatter.FormatPlainText(power.StackRuleZh)}");
        }

        if (!string.IsNullOrWhiteSpace(power.StackRuleEn))
        {
            lines.Add($"叠加规则(EN)：{KnowledgeTextFormatter.FormatPlainText(power.StackRuleEn)}");
        }

        if (!string.IsNullOrWhiteSpace(power.ResolutionRuleZh))
        {
            lines.Add($"结算规则(ZH)：{KnowledgeTextFormatter.FormatPlainText(power.ResolutionRuleZh)}");
        }

        if (!string.IsNullOrWhiteSpace(power.ResolutionRuleEn))
        {
            lines.Add($"结算规则(EN)：{KnowledgeTextFormatter.FormatPlainText(power.ResolutionRuleEn)}");
        }

        if (!string.IsNullOrWhiteSpace(power.DescriptionZh))
        {
            lines.Add($"描述(ZH)：{KnowledgeTextFormatter.FormatPowerText(power, power.DescriptionZh)}");
        }

        if (!string.IsNullOrWhiteSpace(power.DescriptionEn))
        {
            lines.Add($"描述(EN)：{KnowledgeTextFormatter.FormatPowerText(power, power.DescriptionEn)}");
        }

        return string.Join("\n", lines);
    }

    private static string BuildEventSection(EventEntry gameEvent)
    {
        var lines = new List<string>
        {
            $"事件：{gameEvent.NameEn} / {gameEvent.NameZh}",
            $"来源：{gameEvent.Source}"
        };

        if (!string.IsNullOrWhiteSpace(gameEvent.DescriptionZh))
        {
            lines.Add($"说明(ZH)：{KnowledgeTextFormatter.FormatEventText(gameEvent, gameEvent.DescriptionZh)}");
        }

        if (!string.IsNullOrWhiteSpace(gameEvent.DescriptionEn))
        {
            lines.Add($"说明(EN)：{KnowledgeTextFormatter.FormatEventText(gameEvent, gameEvent.DescriptionEn)}");
        }

        return string.Join("\n", lines);
    }

    private static string BuildEnchantmentSection(EnchantmentEntry enchantment)
    {
        var lines = new List<string>
        {
            $"附魔：{enchantment.NameEn} / {enchantment.NameZh}",
            $"来源：{enchantment.Source}"
        };

        if (!string.IsNullOrWhiteSpace(enchantment.DescriptionZh))
        {
            lines.Add($"说明(ZH)：{KnowledgeTextFormatter.FormatEnchantmentText(enchantment, enchantment.DescriptionZh)}");
        }

        if (!string.IsNullOrWhiteSpace(enchantment.DescriptionEn))
        {
            lines.Add($"说明(EN)：{KnowledgeTextFormatter.FormatEnchantmentText(enchantment, enchantment.DescriptionEn)}");
        }

        return string.Join("\n", lines);
    }

    private static string BuildMechanicsSection(IReadOnlyList<MechanicRule> rules)
    {
        var builder = new StringBuilder();
        builder.AppendLine("机制规则：");
        foreach (var rule in rules)
        {
            builder.AppendLine($"- {rule.Title} ({rule.Source})");
            if (!string.IsNullOrWhiteSpace(rule.Summary))
            {
                builder.AppendLine($"  摘要：{rule.Summary}");
            }
        }

        return builder.ToString().Trim();
    }

    private static string BuildBuildSection(IReadOnlyList<BuildGuideEntry> builds)
    {
        var builder = new StringBuilder();
        builder.AppendLine("推荐构筑：");
        foreach (var build in builds)
        {
            builder.AppendLine($"- {build.NameEn} / {build.NameZh}");
            if (!string.IsNullOrWhiteSpace(build.SummaryZh))
            {
                builder.AppendLine($"  摘要(ZH)：{KnowledgeTextFormatter.FormatPlainText(build.SummaryZh)}");
            }
            if (!string.IsNullOrWhiteSpace(build.SummaryEn))
            {
                builder.AppendLine($"  摘要(EN)：{KnowledgeTextFormatter.FormatPlainText(build.SummaryEn)}");
            }

            if (!string.IsNullOrWhiteSpace(build.TipsZh))
            {
                builder.AppendLine($"  要点(ZH)：{KnowledgeTextFormatter.FormatPlainText(build.TipsZh)}");
            }
            if (!string.IsNullOrWhiteSpace(build.TipsEn))
            {
                builder.AppendLine($"  要点(EN)：{KnowledgeTextFormatter.FormatPlainText(build.TipsEn)}");
            }
        }

        return builder.ToString().Trim();
    }

    private static string ComposeSection(string title, params string[] values)
    {
        var parts = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToList();

        if (parts.Count == 0)
        {
            return string.Empty;
        }

        return title + "：\n" + string.Join("\n", parts);
    }

    private static bool LooksLikeCharacterQuestion(string normalized)
    {
        return normalized.Contains("角色", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("职业", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("玩法", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("playstyle", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("build", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("构筑", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeMechanicsQuestion(string normalized)
    {
        return normalized.Contains("机制", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("能量", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("格挡", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("抽牌", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("状态", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("debuff", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("buff", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("keyword", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Matches(string? value, string normalizedQuery)
    {
        return !string.IsNullOrWhiteSpace(value)
            && GuideKnowledgeBase.Normalize(value).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractNamedTarget(string query, IEnumerable<string> markers)
    {
        foreach (var marker in markers)
        {
            var index = query.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            var tail = query[(index + marker.Length)..].Trim();
            tail = tail.TrimStart('是', '：', ':', '？', '?', '的');
            if (!string.IsNullOrWhiteSpace(tail))
            {
                return tail.Trim();
            }
        }

        return null;
    }

    private static string BuildFallbackAnswer(RunAnalysis analysis)
    {
        var builder = new StringBuilder();
        builder.AppendLine("我没有在本地知识库里找到足够直接的条目。你可以换一种更具体的问法，例如：");
        builder.AppendLine("- 这张卡有什么用？");
        builder.AppendLine("- 当前角色有哪些推荐构筑？");
        builder.AppendLine("- 某个遗物适合什么套路？");

        if (!string.IsNullOrWhiteSpace(analysis.RecommendedBuildName))
        {
            builder.AppendLine();
            builder.AppendLine($"当前局面参考构筑：{analysis.RecommendedBuildName}");
        }

        return builder.ToString().Trim();
    }

    private static string Trim(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..Math.Max(0, maxLength - 3)].TrimEnd() + "...";
    }
}

public sealed record KnowledgeAnswer(bool HasAnswer, string Answer, IReadOnlyList<string> Sources)
{
    public static KnowledgeAnswer Empty { get; } = new(false, string.Empty, Array.Empty<string>());
}