using System.Text;
using System.Text.Json;
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

    public string RootDirectory { get; }

    public string OverviewMarkdown { get; private set; } = string.Empty;

    public string KnowledgeMarkdown { get; private set; } = string.Empty;

    public IReadOnlyList<CharacterGuideEntry> Characters { get; private set; } = Array.Empty<CharacterGuideEntry>();

    public IReadOnlyList<BuildGuideEntry> Builds { get; private set; } = Array.Empty<BuildGuideEntry>();

    public IReadOnlyList<CardGuideEntry> Cards { get; private set; } = Array.Empty<CardGuideEntry>();

    public IReadOnlyList<RelicGuideEntry> Relics { get; private set; } = Array.Empty<RelicGuideEntry>();

    public GuideKnowledgeBase(string modDirectory)
    {
        RootDirectory = ResolveKnowledgeBaseDirectory(modDirectory);
    }

    public void Load()
    {
        Directory.CreateDirectory(RootDirectory);
        OverviewMarkdown = ReadTextIfExists("00_OVERVIEW.md");
        KnowledgeMarkdown = ReadTextIfExists("sts2_knowledge_base.md");
        Characters = LoadJson<List<CharacterGuideEntry>>("characters_full.json") ?? new List<CharacterGuideEntry>();
        Builds = LoadJson<List<BuildGuideEntry>>("builds_full.json") ?? new List<BuildGuideEntry>();
        Cards = LoadJson<List<CardGuideEntry>>("cards_full.json") ?? new List<CardGuideEntry>();
        Relics = LoadJson<List<RelicGuideEntry>>("relics_full.json") ?? new List<RelicGuideEntry>();

        Log.Info($"[AiBot] Knowledge base loaded. Characters={Characters.Count}, Builds={Builds.Count}, Cards={Cards.Count}, Relics={Relics.Count}");
    }

    public IEnumerable<BuildGuideEntry> GetBuildsForCharacter(int characterId)
    {
        return Builds.Where(b => b.CharacterId == characterId);
    }

    public CardGuideEntry? FindCard(string cardName, int? characterId = null)
    {
        var normalized = Normalize(RemoveCountSuffix(cardName));
        return Cards.FirstOrDefault(card =>
            (characterId is null || card.CharacterId == characterId.Value) &&
            (Normalize(card.Slug) == normalized || Normalize(card.NameEn) == normalized || Normalize(card.NameZh) == normalized));
    }

    public RelicGuideEntry? FindRelic(string relicName, int? characterId = null)
    {
        var normalized = Normalize(relicName);
        return Relics.FirstOrDefault(relic =>
            (characterId is null || relic.CharacterId is null || relic.CharacterId == characterId.Value) &&
            (Normalize(relic.Slug) == normalized || Normalize(relic.NameEn) == normalized || Normalize(relic.NameZh) == normalized));
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
                    : $"- {entry}: {guide.CardType ?? "Unknown"}; {TrimSnippet(guide.DescriptionEn, 120)}";
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
                    : $"- {name}: {TrimSnippet(guide.DescriptionEn, 120)}";
            })
            .ToList();

        return string.Join("\n", lines);
    }

    public string BuildPotionSummary(IEnumerable<string> potionNames, int maxEntries = 6)
    {
        var list = potionNames.Take(maxEntries).ToList();
        if (list.Count == 0)
        {
            return string.Empty;
        }

        var snippets = ExtractMarkdownSnippets(list, 4, 140);
        var lines = list.Select(name => $"- {name}").ToList();
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
        if (!string.IsNullOrWhiteSpace(brief))
        {
            sb.AppendLine("Character Guide:");
            sb.AppendLine(brief);
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
            var potionSummary = BuildPotionSummary(potionList, 4);
            if (!string.IsNullOrWhiteSpace(potionSummary))
            {
                sb.AppendLine();
                sb.AppendLine("Potion / Item Notes:");
                sb.AppendLine(potionSummary);
            }
        }

        var snippets = ExtractMarkdownSnippets(deckList.Concat(relicList).Concat(potionList), 6, 180);
        if (snippets.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Knowledge Base Notes:");
            foreach (var snippet in snippets)
            {
                sb.AppendLine($"- {snippet}");
            }
        }

        return TrimSnippet(sb.ToString().Trim(), 4000);
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

    private List<string> ExtractMarkdownSnippets(IEnumerable<string> terms, int maxSnippets, int snippetLength)
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

        var paragraphs = string.Join("\n\n", new[] { OverviewMarkdown, KnowledgeMarkdown })
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

    private string ReadTextIfExists(string fileName)
    {
        var path = Path.Combine(RootDirectory, fileName);
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    private T? LoadJson<T>(string fileName)
    {
        var path = Path.Combine(RootDirectory, fileName);
        if (!File.Exists(path))
        {
            Log.Warn($"[AiBot] Missing knowledge file: {path}");
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex)
        {
            Log.Error($"[AiBot] Failed to load knowledge file '{path}': {ex}");
            return default;
        }
    }
}
