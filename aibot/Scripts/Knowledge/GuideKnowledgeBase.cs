using System.Text;
using System.Text.Json;
using aibot.Scripts.Config;
using MegaCrit.Sts2.Core.Logging;

namespace aibot.Scripts.Knowledge;

public sealed class GuideKnowledgeBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly KnowledgeValidator _validator = new();
    private readonly bool _enableCustomKnowledge;
    private readonly string _customDirectoryName;
    private readonly int _maxCustomFileSize;

    public string RootDirectory { get; }

    public string OverviewMarkdown { get; private set; } = string.Empty;

    public string KnowledgeMarkdown { get; private set; } = string.Empty;

    public string CoreMechanicsSummary { get; private set; } = string.Empty;

    public IReadOnlyDictionary<int, string> CharacterGuideMarkdownById { get; private set; } = new Dictionary<int, string>();

    public IReadOnlyList<CharacterGuideEntry> Characters { get; private set; } = Array.Empty<CharacterGuideEntry>();

    public IReadOnlyList<BuildGuideEntry> Builds { get; private set; } = Array.Empty<BuildGuideEntry>();

    public IReadOnlyList<CardGuideEntry> Cards { get; private set; } = Array.Empty<CardGuideEntry>();

    public IReadOnlyList<RelicGuideEntry> Relics { get; private set; } = Array.Empty<RelicGuideEntry>();

    public IReadOnlyList<PotionEntry> Potions { get; private set; } = Array.Empty<PotionEntry>();

    public IReadOnlyList<PowerEntry> Powers { get; private set; } = Array.Empty<PowerEntry>();

    public IReadOnlyList<EnemyEntry> Enemies { get; private set; } = Array.Empty<EnemyEntry>();

    public IReadOnlyList<EventEntry> Events { get; private set; } = Array.Empty<EventEntry>();

    public IReadOnlyList<EnchantmentEntry> Enchantments { get; private set; } = Array.Empty<EnchantmentEntry>();

    public IReadOnlyList<MechanicRule> Mechanics { get; private set; } = Array.Empty<MechanicRule>();

    public GuideKnowledgeBase(string modDirectory, AiBotConfig? config = null)
    {
        RootDirectory = ResolveKnowledgeBaseDirectory(modDirectory);
        _enableCustomKnowledge = config?.Knowledge.EnableCustom ?? true;
        _customDirectoryName = string.IsNullOrWhiteSpace(config?.Knowledge.CustomDir) ? "custom" : config!.Knowledge.CustomDir;
        _maxCustomFileSize = Math.Max(4096, config?.Knowledge.MaxCustomFileSize ?? 262144);
    }

    public void Load()
    {
        Directory.CreateDirectory(RootDirectory);
        OverviewMarkdown = ReadTextFromAliases("overview.md", "00_OVERVIEW.md");
        KnowledgeMarkdown = ReadTextFromAliases("general_strategy.md", "sts2_knowledge_base.md");
        Characters = LoadLayeredList<CharacterGuideEntry>("characters.json", "characters_full.json", entry => entry.Id.ToString());
        Builds = LoadLayeredList<BuildGuideEntry>("builds.json", "builds_full.json", entry => entry.Id.ToString());
        Cards = LoadLayeredList<CardGuideEntry>("cards.json", "cards_full.json", entry => Normalize(entry.Slug));
        Relics = LoadLayeredList<RelicGuideEntry>("relics.json", "relics_full.json", entry => Normalize(entry.Slug));
        Potions = LoadLayeredList<PotionEntry>("potions.json", "potions.json", entry => Normalize(entry.Slug));
        Powers = LoadLayeredList<PowerEntry>("powers.json", "powers.json", entry => Normalize(entry.Slug));
        Enemies = LoadLayeredList<EnemyEntry>("enemies.json", "enemies.json", entry => Normalize(entry.Slug));
        Events = LoadLayeredList<EventEntry>("events.json", "events.json", entry => Normalize(entry.Slug));
        Enchantments = LoadLayeredList<EnchantmentEntry>("enchantments.json", "enchantments.json", entry => Normalize(entry.Slug));
        Mechanics = LoadLayeredList<MechanicRule>("game_mechanics.json", "game_mechanics.json", entry => Normalize(entry.Id));
        CharacterGuideMarkdownById = LoadCharacterGuideMarkdown();
        CoreMechanicsSummary = BuildCoreMechanicsSummary();

        Log.Info($"[AiBot] Knowledge base loaded. Characters={Characters.Count}, CharacterGuides={CharacterGuideMarkdownById.Count}, Builds={Builds.Count}, Cards={Cards.Count}, Relics={Relics.Count}, Potions={Potions.Count}, Powers={Powers.Count}, Enemies={Enemies.Count}, Events={Events.Count}, Enchantments={Enchantments.Count}, Mechanics={Mechanics.Count}");
    }

    public IEnumerable<BuildGuideEntry> GetBuildsForCharacter(int characterId)
    {
        return Builds.Where(b => b.CharacterId == characterId);
    }

    public IReadOnlyList<BuildGuideEntry> FindBuilds(string? query, int characterId, int maxResults = 5)
    {
        var builds = Builds.Where(build => build.CharacterId == characterId);
        if (string.IsNullOrWhiteSpace(query))
        {
            return builds.Take(maxResults).ToList();
        }

        var normalized = Normalize(query);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return builds.Take(maxResults).ToList();
        }

        return builds
            .Select(build => new
            {
                Build = build,
                Score = ScoreSelectors(normalized, build, new Func<BuildGuideEntry, string?>[]
                {
                    entry => entry.NameEn,
                    entry => entry.NameZh,
                    entry => entry.Slug,
                    entry => entry.SummaryEn,
                    entry => entry.SummaryZh,
                    entry => entry.StrategyEn,
                    entry => entry.StrategyZh,
                    entry => entry.TipsEn,
                    entry => entry.TipsZh
                })
            })
            .Where(entry => entry.Score > 0)
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Build.NameEn, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .Select(entry => entry.Build)
            .ToList();
    }

    public CardGuideEntry? FindCard(string cardName, int? characterId = null)
    {
        return FindFilteredEntry(
            Cards,
            cardName,
            card => characterId is null || card.CharacterId == characterId.Value,
            card => card.Slug,
            card => card.NameEn,
            card => card.NameZh,
            card => card.DescriptionZh,
            card => card.DescriptionEn);
    }

    public RelicGuideEntry? FindRelic(string relicName, int? characterId = null)
    {
        return FindFilteredEntry(
            Relics,
            relicName,
            relic => characterId is null || relic.CharacterId is null || relic.CharacterId == characterId.Value,
            relic => relic.Slug,
            relic => relic.NameEn,
            relic => relic.NameZh,
            relic => relic.DescriptionZh,
            relic => relic.DescriptionEn);
    }

    public PotionEntry? FindPotion(string potionName)
    {
        return FindNamedEntry(
            Potions,
            potionName,
            potion => potion.Slug,
            potion => potion.NameEn,
            potion => potion.NameZh,
            potion => potion.DescriptionZh,
            potion => potion.DescriptionEn,
            potion => potion.Usage);
    }

    public PowerEntry? FindPower(string powerName)
    {
        return FindNamedEntry(
            Powers,
            powerName,
            power => power.Slug,
            power => power.NameEn,
            power => power.NameZh,
            power => power.DescriptionZh,
            power => power.DescriptionEn,
            power => power.PowerType);
    }

    public EnemyEntry? FindEnemy(string enemyName)
    {
        return FindNamedEntry(
            Enemies,
            enemyName,
            enemy => enemy.Slug,
            enemy => enemy.NameEn,
            enemy => enemy.NameZh,
            enemy => enemy.DescriptionZh,
            enemy => enemy.DescriptionEn,
            enemy => string.Join(' ', enemy.Moves.Select(move => move.Id)),
            enemy => string.Join(' ', enemy.Moves.Select(move => move.TitleEn)),
            enemy => string.Join(' ', enemy.Moves.Select(move => move.TitleZh)),
            enemy => string.Join(' ', enemy.Moves.Select(move => move.BanterZh)),
            enemy => string.Join(' ', enemy.Moves.Select(move => move.BanterEn)));
    }

    public EventEntry? FindEvent(string eventName)
    {
        return FindNamedEntry(
            Events,
            eventName,
            entry => entry.Slug,
            entry => entry.NameEn,
            entry => entry.NameZh,
            entry => entry.DescriptionZh,
            entry => entry.DescriptionEn);
    }

    public EnchantmentEntry? FindEnchantment(string enchantmentName)
    {
        return FindNamedEntry(
            Enchantments,
            enchantmentName,
            entry => entry.Slug,
            entry => entry.NameEn,
            entry => entry.NameZh,
            entry => entry.DescriptionZh,
            entry => entry.DescriptionEn);
    }

    public IReadOnlyList<MechanicRule> SearchMechanicRules(string query, int maxResults = 5)
    {
        var normalizedTerms = TokenizeSearchTerms(query);
        if (normalizedTerms.Count == 0)
        {
            return Mechanics.Take(maxResults).ToList();
        }

        return Mechanics
            .Select(rule => new
            {
                Rule = rule,
                Score = ScoreTextMatch(normalizedTerms, rule.Id, rule.Title, rule.Summary)
            })
            .Where(entry => entry.Score > 0)
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Rule.Title, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .Select(entry => entry.Rule)
            .ToList();
    }

    public IReadOnlyList<string> SearchMarkdownSnippets(IEnumerable<string> terms, int maxSnippets, int snippetLength, int? characterId = null)
    {
        return ExtractMarkdownSnippets(terms, maxSnippets, snippetLength, characterId);
    }

    public string BuildDeckSummary(IEnumerable<string> deckEntries, int characterId, int maxEntries = 12)
    {
        var lines = deckEntries
            .Take(maxEntries)
            .Select(entry =>
            {
                var guide = FindCard(entry, characterId);
                return guide is null
                    ? $"- {entry}"
                    : $"- {entry}: {guide.CardType ?? "Unknown"}; {TrimSnippet(SelectBestSummaryText(guide.DescriptionZh, guide.DescriptionEn), 120)}";
            })
            .ToList();

        return string.Join("\n", lines);
    }

    public string BuildRelicSummary(IEnumerable<string> relicNames, int characterId, int maxEntries = 8)
    {
        var lines = relicNames
            .Take(maxEntries)
            .Select(name =>
            {
                var guide = FindRelic(name, characterId);
                return guide is null
                    ? $"- {name}"
                    : $"- {name}: {TrimSnippet(SelectBestSummaryText(guide.DescriptionZh, guide.DescriptionEn), 120)}";
            })
            .ToList();

        return string.Join("\n", lines);
    }

    public string BuildPotionSummary(IEnumerable<string> potionNames, int maxEntries = 6, int? characterId = null)
    {
        var list = potionNames.Take(maxEntries).ToList();
        if (list.Count == 0)
        {
            return string.Empty;
        }

        var snippets = ExtractMarkdownSnippets(list, 4, 140, characterId);
        var lines = list.Select(name =>
        {
            var potion = FindPotion(name);
            return potion is null
                ? $"- {name}"
                : $"- {name}: {(string.IsNullOrWhiteSpace(potion.Rarity) ? "Potion" : potion.Rarity)}; {TrimSnippet(SelectBestSummaryText(potion.DescriptionZh, potion.DescriptionEn, potion.Usage), 120)}";
        }).ToList();
        if (snippets.Count > 0)
        {
            lines.Add("Relevant notes:");
            lines.AddRange(snippets.Select(snippet => $"- {snippet}"));
        }

        return string.Join("\n", lines);
    }

    public string BuildKnowledgeDigest(int characterId, IEnumerable<string> deckEntries, IEnumerable<string> relicNames, IEnumerable<string> potionNames, int maxCards = 8, int maxRelics = 5)
    {
        var sb = new StringBuilder();
        var brief = BuildCharacterBrief(characterId);
        var characterGuideSummary = BuildCharacterGuideSummary(characterId);
        if (!string.IsNullOrWhiteSpace(brief))
        {
            sb.AppendLine("Character Guide:");
            sb.AppendLine(brief);
        }

        if (!string.IsNullOrWhiteSpace(characterGuideSummary))
        {
            sb.AppendLine();
            sb.AppendLine("Character Deep Guide:");
            sb.AppendLine(characterGuideSummary);
        }

        if (!string.IsNullOrWhiteSpace(CoreMechanicsSummary))
        {
            sb.AppendLine();
            sb.AppendLine("Core Mechanics:");
            sb.AppendLine(CoreMechanicsSummary);
        }

        var deckList = deckEntries.Take(maxCards).ToList();
        if (deckList.Count > 0)
        {
            var deckSummary = BuildDeckSummary(deckList, characterId, maxCards);
            if (!string.IsNullOrWhiteSpace(deckSummary))
            {
                sb.AppendLine();
                sb.AppendLine("Deck Guide:");
                sb.AppendLine(deckSummary);
            }
        }

        var relicList = relicNames.Take(maxRelics).ToList();
        if (relicList.Count > 0)
        {
            var relicSummary = BuildRelicSummary(relicList, characterId, maxRelics);
            if (!string.IsNullOrWhiteSpace(relicSummary))
            {
                sb.AppendLine();
                sb.AppendLine("Relic Guide:");
                sb.AppendLine(relicSummary);
            }
        }

        var potionList = potionNames.Take(4).ToList();
        if (potionList.Count > 0)
        {
            var potionSummary = BuildPotionSummary(potionList, 4, characterId);
            if (!string.IsNullOrWhiteSpace(potionSummary))
            {
                sb.AppendLine();
                sb.AppendLine("Potion / Item Notes:");
                sb.AppendLine(potionSummary);
            }
        }

        var snippets = ExtractMarkdownSnippets(deckList.Concat(relicList).Concat(potionList), 6, 180, characterId);
        if (snippets.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Knowledge Base Notes:");
            foreach (var snippet in snippets)
            {
                sb.AppendLine($"- {snippet}");
            }
        }

        return TrimSnippet(sb.ToString().Trim(), 5000);
    }

    public string BuildCharacterBrief(int characterId)
    {
        var sb = new StringBuilder();
        var character = Characters.FirstOrDefault(c => c.Id == characterId);
        if (character is not null)
        {
            sb.AppendLine($"Character: {character.NameEn} / {character.NameZh}");
            if (!string.IsNullOrWhiteSpace(character.PlaystyleEn))
            {
                sb.AppendLine($"Playstyle: {character.PlaystyleEn}");
            }
        }

        foreach (var build in GetBuildsForCharacter(characterId).Take(3))
        {
            sb.AppendLine($"Build: {build.NameEn} / {build.NameZh}");
            if (!string.IsNullOrWhiteSpace(build.SummaryEn))
            {
                sb.AppendLine($"Summary: {build.SummaryEn}");
            }
            if (!string.IsNullOrWhiteSpace(build.TipsEn))
            {
                sb.AppendLine($"Tips: {build.TipsEn}");
            }
        }

        return sb.ToString().Trim();
    }

    public string BuildCharacterGuideSummary(int characterId)
    {
        if (!CharacterGuideMarkdownById.TryGetValue(characterId, out var markdown) || string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var lines = new List<string>();
        AddFirstHeading(lines, markdown);
        AddParagraphAfterMarker(lines, markdown, "### 描述");
        AddParagraphAfterMarker(lines, markdown, "### 游戏风格");
        AddRecommendedBuildSummaries(lines, markdown, 2);
        AddHighlightedCardNames(lines, markdown, "### 稀有卡牌 (Rare)", "Rare cards to value", 4);
        AddHighlightedCardNames(lines, markdown, "### 罕见卡牌 (Uncommon)", "Uncommon enablers", 4);

        var deduped = lines
            .Select(line => TrimSnippet(CleanMarkdownLine(line), 220))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct()
            .Take(14)
            .ToList();

        return string.Join("\n", deduped.Select(line => $"- {line}"));
    }

    public bool MentionsCard(string normalizedCardName, BuildGuideEntry build)
    {
        var haystack = string.Join('\n', new[]
        {
            build.NameEn,
            build.NameZh,
            build.SummaryEn,
            build.SummaryZh,
            build.StrategyEn,
            build.StrategyZh,
            build.TipsEn,
            build.TipsZh
        }.Where(s => !string.IsNullOrWhiteSpace(s)));

        return Normalize(haystack).Contains(normalizedCardName, StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Trim()
            .ToLowerInvariant()
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .Replace("'", string.Empty)
            .Replace("\"", string.Empty);
    }

    public static IReadOnlyList<string> TokenizeSearchTerms(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<string>();
        }

        return query
            .Split(new[] { ' ', '　', ',', '，', '。', '.', '?', '？', '!', '！', ':', '：', '/', '\\', '|', '\t', '\r', '\n', '(', ')', '（', '）' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.Trim())
            .Where(token => token.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<string> ExtractMarkdownSnippets(IEnumerable<string> terms, int maxSnippets, int snippetLength, int? characterId = null)
    {
        var normalizedTerms = terms
            .Select(RemoveCountSuffix)
            .Select(Normalize)
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct()
            .ToList();

        if (normalizedTerms.Count == 0)
        {
            return new List<string>();
        }

        var markdownSources = new List<string> { OverviewMarkdown, KnowledgeMarkdown };
        if (characterId is not null && CharacterGuideMarkdownById.TryGetValue(characterId.Value, out var characterMarkdown) && !string.IsNullOrWhiteSpace(characterMarkdown))
        {
            markdownSources.Add(characterMarkdown);
        }

        var paragraphs = string.Join("\n\n", markdownSources)
            .Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return paragraphs
            .Where(paragraph =>
            {
                var normalized = Normalize(paragraph);
                return normalizedTerms.Any(term => normalized.Contains(term, StringComparison.OrdinalIgnoreCase));
            })
            .Select(paragraph => TrimSnippet(paragraph.Replace('\r', ' ').Replace('\n', ' '), snippetLength))
            .Distinct()
            .Take(maxSnippets)
            .ToList();
    }

    private static TEntry? FindNamedEntry<TEntry>(IReadOnlyList<TEntry> entries, string query, params Func<TEntry, string?>[] selectors)
        where TEntry : class
    {
        return FindFilteredEntry(entries, query, static _ => true, selectors);
    }

    private static TEntry? FindFilteredEntry<TEntry>(IEnumerable<TEntry> entries, string query, Func<TEntry, bool> filter, params Func<TEntry, string?>[] selectors)
        where TEntry : class
    {
        var normalized = Normalize(RemoveCountSuffix(query));
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return entries
            .Where(filter)
            .Select(entry => new
            {
                Entry = entry,
                Score = ScoreSelectors(normalized, entry, selectors)
            })
            .Where(entry => entry.Score > 0)
            .OrderByDescending(entry => entry.Score)
            .Select(entry => entry.Entry)
            .FirstOrDefault();
    }

    private static string? SelectBestSummaryText(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static int ScoreSelectors<TEntry>(string normalizedQuery, TEntry entry, IEnumerable<Func<TEntry, string?>> selectors)
    {
        var score = 0;
        foreach (var selector in selectors)
        {
            var value = selector(entry);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var normalizedValue = Normalize(value);
            if (normalizedValue == normalizedQuery)
            {
                score = Math.Max(score, 100);
            }
            else if (normalizedValue.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                score = Math.Max(score, 70);
            }
            else if (normalizedQuery.Contains(normalizedValue, StringComparison.OrdinalIgnoreCase))
            {
                score = Math.Max(score, 40);
            }
        }

        return score;
    }

    private static int ScoreTextMatch(IReadOnlyList<string> normalizedTerms, params string?[] values)
    {
        var score = 0;
        foreach (var rawValue in values)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                continue;
            }

            var normalizedValue = Normalize(rawValue);
            foreach (var term in normalizedTerms)
            {
                if (normalizedValue == Normalize(term))
                {
                    score += 5;
                }
                else if (normalizedValue.Contains(Normalize(term), StringComparison.OrdinalIgnoreCase))
                {
                    score += 2;
                }
            }
        }

        return score;
    }

    private string BuildCoreMechanicsSummary()
    {
        var lines = new List<string>();

        AddFirstMatchingLine(lines, OverviewMarkdown,
            "能量 (Energy)",
            "格挡 (Block)",
            "抽牌 (Draw)");

        AddFirstMatchingLine(lines, KnowledgeMarkdown,
            "X费用",
            "牌组精简",
            "能量曲线",
            "防御手段");

        AddSectionBullets(lines, KnowledgeMarkdown, "### 回合流程", 8);
        AddSectionBullets(lines, KnowledgeMarkdown, "### 关键词解释", 8, new[]
        {
            "力量",
            "敏捷",
            "易伤",
            "虚弱",
            "毒素",
            "格挡",
            "消耗",
            "抽牌"
        });

        var deduped = lines
            .Select(line => TrimSnippet(line.Replace("**", string.Empty).Replace("|", " ").Replace("- ", string.Empty).Trim(), 220))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct()
            .Take(12)
            .ToList();

        return string.Join("\n", deduped.Select(line => $"- {line}"));
    }

    private Dictionary<int, string> LoadCharacterGuideMarkdown()
    {
        var guides = new Dictionary<int, string>();
        foreach (var character in Characters)
        {
            var fileNames = ResolveCharacterGuideFileNames(character);
            if (fileNames.Count == 0)
            {
                continue;
            }

            var markdown = ReadTextFromAliases(fileNames.ToArray());
            if (!string.IsNullOrWhiteSpace(markdown))
            {
                guides[character.Id] = markdown;
            }
        }

        return guides;
    }

    private static IReadOnlyList<string> ResolveCharacterGuideFileNames(CharacterGuideEntry character)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(character.Slug))
        {
            candidates.Add(character.Slug + ".md");
            candidates.Add(character.Slug + "_complete_guide.md");
        }

        var englishName = character.NameEn?.Trim();
        if (!string.IsNullOrWhiteSpace(englishName))
        {
            if (englishName.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
            {
                englishName = englishName[4..];
            }

            var baseName = englishName.ToLowerInvariant().Replace(' ', '_');
            candidates.Add(baseName + ".md");
            candidates.Add(baseName + "_complete_guide.md");
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddFirstMatchingLine(List<string> target, string text, params string[] markers)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var marker in markers)
        {
            var match = lines.FirstOrDefault(line => line.Contains(marker, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match))
            {
                target.Add(match);
            }
        }
    }

    private static void AddRecommendedBuildSummaries(List<string> target, string markdown, int maxBuilds)
    {
        if (string.IsNullOrWhiteSpace(markdown) || maxBuilds <= 0)
        {
            return;
        }

        var lines = markdown.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
        var start = Array.FindIndex(lines, line => line.Trim().Equals("## 🎯 推荐卡组构建", StringComparison.OrdinalIgnoreCase));
        if (start < 0)
        {
            return;
        }

        var buildsAdded = 0;
        for (var i = start + 1; i < lines.Length && buildsAdded < maxBuilds; i++)
        {
            var trimmed = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            if (!trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                continue;
            }

            var buildName = trimmed[4..].Trim();
            var intro = string.Empty;
            var core = string.Empty;

            for (var j = i + 1; j < lines.Length; j++)
            {
                var detail = lines[j].Trim();
                if (string.IsNullOrWhiteSpace(detail))
                {
                    continue;
                }

                if (detail.StartsWith("### ", StringComparison.Ordinal) || detail.StartsWith("## ", StringComparison.Ordinal))
                {
                    i = j - 1;
                    break;
                }

                if (detail.StartsWith("**简介:**", StringComparison.Ordinal))
                {
                    intro = ExtractLabeledValue(detail, "**简介:**");
                    continue;
                }

                if (detail.StartsWith("**核心机制:**", StringComparison.Ordinal))
                {
                    core = ExtractLabeledValue(detail, "**核心机制:**");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(intro) && !detail.StartsWith("**", StringComparison.Ordinal) && detail != "---")
                {
                    intro = detail;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(core) && !detail.StartsWith("**", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(intro))
                {
                    core = detail;
                }
            }

            target.Add($"Recommended build: {buildName}");
            if (!string.IsNullOrWhiteSpace(intro))
            {
                target.Add($"Plan: {intro}");
            }

            if (!string.IsNullOrWhiteSpace(core))
            {
                target.Add($"Mechanics: {core}");
            }

            buildsAdded++;
        }
    }

    private static void AddHighlightedCardNames(List<string> target, string markdown, string sectionHeader, string label, int maxCards)
    {
        if (string.IsNullOrWhiteSpace(markdown) || maxCards <= 0)
        {
            return;
        }

        var lines = markdown.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
        var start = Array.FindIndex(lines, line => line.Trim().Equals(sectionHeader, StringComparison.OrdinalIgnoreCase));
        if (start < 0)
        {
            return;
        }

        var cards = new List<string>();
        for (var i = start + 1; i < lines.Length && cards.Count < maxCards; i++)
        {
            var trimmed = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (trimmed.StartsWith("### ", StringComparison.Ordinal) || trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            if (trimmed.StartsWith("#### ", StringComparison.Ordinal))
            {
                cards.Add(trimmed[5..].Trim());
            }
        }

        if (cards.Count > 0)
        {
            target.Add($"{label}: {string.Join(", ", cards)}");
        }
    }

    private static string ExtractLabeledValue(string line, string label)
    {
        if (!line.StartsWith(label, StringComparison.Ordinal))
        {
            return line.Trim();
        }

        return line[label.Length..].Trim();
    }

    private static void AddSectionBullets(List<string> target, string markdown, string sectionHeader, int maxLines, IReadOnlyList<string>? preferredTerms = null)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return;
        }

        var lines = markdown.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
        var start = Array.FindIndex(lines, line => line.Trim().Equals(sectionHeader, StringComparison.OrdinalIgnoreCase));
        if (start < 0)
        {
            return;
        }

        for (var i = start + 1; i < lines.Length && target.Count < 24; i++)
        {
            var trimmed = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (trimmed.StartsWith("## ", StringComparison.Ordinal) || trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                break;
            }

            if (preferredTerms is not null && preferredTerms.Count > 0)
            {
                if (!preferredTerms.Any(term => trimmed.Contains(term, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
            }

            if (trimmed.StartsWith("-") || char.IsDigit(trimmed[0]) || trimmed.StartsWith("|"))
            {
                target.Add(trimmed);
                if (target.Count >= maxLines)
                {
                    return;
                }
            }
        }
    }

    private static void AddFirstHeading(List<string> target, string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return;
        }

        var heading = markdown.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.StartsWith("# ", StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(heading))
        {
            target.Add(heading);
        }
    }

    private static void AddParagraphAfterMarker(List<string> target, string markdown, string marker)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return;
        }

        var lines = markdown.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
        var start = Array.FindIndex(lines, line => line.Trim().Equals(marker, StringComparison.OrdinalIgnoreCase));
        if (start < 0)
        {
            return;
        }

        for (var i = start + 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (trimmed.StartsWith("## ", StringComparison.Ordinal) || trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                break;
            }

            if (trimmed.StartsWith(">", StringComparison.Ordinal) || trimmed == "---")
            {
                continue;
            }

            target.Add(trimmed);
            return;
        }
    }

    private static string CleanMarkdownLine(string value)
    {
        return value
            .Replace("#", string.Empty)
            .Replace("**", string.Empty)
            .Replace("|", " ")
            .Replace("`", string.Empty)
            .Trim();
    }

    private static string TrimSnippet(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = value.Trim();
        if (cleaned.Length <= maxLength)
        {
            return cleaned;
        }

        return cleaned[..maxLength].TrimEnd() + "...";
    }

    private static string RemoveCountSuffix(string value)
    {
        var marker = value.LastIndexOf(" x", StringComparison.OrdinalIgnoreCase);
        if (marker < 0 || marker >= value.Length - 2)
        {
            return value;
        }

        return int.TryParse(value[(marker + 2)..], out _)
            ? value[..marker]
            : value;
    }

    private string ResolveKnowledgeBaseDirectory(string modDirectory)
    {
        var bundled = Path.Combine(modDirectory, "KnowledgeBase");
        if (Directory.Exists(bundled))
        {
            return bundled;
        }

        var workspaceFallback = Path.GetFullPath(Path.Combine(modDirectory, "..", "..", "sts2_guides"));
        return workspaceFallback;
    }

    private string ReadTextFromAliases(params string[] fileNames)
    {
        foreach (var fileName in fileNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var markdown = ReadTextFromLayers(fileName);
            if (!string.IsNullOrWhiteSpace(markdown))
            {
                return markdown;
            }
        }

        return string.Empty;
    }

    private string ReadTextFromLayers(string fileName)
    {
        foreach (var path in EnumerateKnowledgePaths(fileName))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var validation = _validator.ValidateMarkdownFile(path, IsCustomPath(path), _maxCustomFileSize);
            if (!validation.IsAccepted)
            {
                Log.Warn($"[AiBot.Knowledge] Skipping markdown file {path}: {validation.Reason}");
                continue;
            }

            return File.ReadAllText(path);
        }

        return string.Empty;
    }

    private List<TEntry> LoadLayeredList<TEntry>(string preferredName, string legacyName, Func<TEntry, string> keySelector)
        where TEntry : class
    {
        var merged = new Dictionary<string, TEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in EnumerateJsonLayerCandidates(preferredName, legacyName))
        {
            foreach (var path in EnumerateKnowledgePaths(candidate))
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var validation = _validator.ValidateJsonFile(path, IsCustomPath(path), _maxCustomFileSize);
                if (!validation.IsAccepted)
                {
                    Log.Warn($"[AiBot.Knowledge] Skipping json file {path}: {validation.Reason}");
                    continue;
                }

                try
                {
                    var list = JsonSerializer.Deserialize<List<TEntry>>(File.ReadAllText(path), JsonOptions) ?? new List<TEntry>();
                    foreach (var entry in list)
                    {
                        var key = keySelector(entry);
                        if (string.IsNullOrWhiteSpace(key))
                        {
                            continue;
                        }

                        StampSource(entry, IsCustomPath(path) ? "custom" : "core");
                        merged[key] = entry;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[AiBot] Failed to load knowledge file '{path}': {ex}");
                }
            }
        }

        return merged.Values.ToList();
    }

    private IEnumerable<string> EnumerateJsonLayerCandidates(string preferredName, string legacyName)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { preferredName, legacyName })
        {
            if (seen.Add(name))
            {
                yield return name;
            }
        }
    }

    private IEnumerable<string> EnumerateKnowledgePaths(string fileName)
    {
        if (Path.IsPathRooted(fileName))
        {
            yield return fileName;
            yield break;
        }

        var customPath = Path.Combine(RootDirectory, _customDirectoryName, fileName);
        if (_enableCustomKnowledge)
        {
            yield return customPath;
        }

        yield return Path.Combine(RootDirectory, "core", fileName);
        yield return Path.Combine(RootDirectory, fileName);
        yield return Path.Combine(RootDirectory, "guides", fileName);
        yield return Path.Combine(RootDirectory, "core", "guides", fileName);
    }

    private bool IsCustomPath(string path)
    {
        return path.Contains(Path.DirectorySeparatorChar + _customDirectoryName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || path.Contains(Path.AltDirectorySeparatorChar + _customDirectoryName + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void StampSource<TEntry>(TEntry entry, string source)
        where TEntry : class
    {
        var property = typeof(TEntry).GetProperty("Source");
        if (property?.CanWrite == true && property.PropertyType == typeof(string))
        {
            property.SetValue(entry, source);
        }
    }

}
