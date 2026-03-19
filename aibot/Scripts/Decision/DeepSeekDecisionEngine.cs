using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using aibot.Scripts.Config;
using aibot.Scripts.Knowledge;

namespace aibot.Scripts.Decision;

public sealed class DeepSeekDecisionEngine : IAiDecisionEngine, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AiBotConfig _config;
    private readonly HttpClient _httpClient;
    private readonly GuideKnowledgeBase? _knowledgeBase;

    public DeepSeekDecisionEngine(AiBotConfig config, GuideKnowledgeBase? knowledgeBase = null)
    {
        _config = config;
        _knowledgeBase = knowledgeBase;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(config.Provider.BaseUrl.TrimEnd('/') + "/")
        };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.Provider.ApiKey);
    }

    public async Task<PotionDecision> ChoosePotionUseAsync(Player player, IReadOnlyList<PotionModel> usablePotions, IReadOnlyList<CardModel> playableCards, IReadOnlyList<Creature> enemies, RunAnalysis analysis, CancellationToken cancellationToken)
    {
        var available = usablePotions
            .Where(potion => potion.Usage is PotionUsage.CombatOnly or PotionUsage.AnyTime)
            .Where(potion => !potion.IsQueued && !potion.HasBeenRemovedFromState && potion.PassesCustomUsabilityCheck)
            .ToList();

        var decisionOptions = available
            .Select((potion, index) => new DecisionOption(index.ToString(), $"{potion.Title}", $"usage={potion.Usage}, target={potion.TargetType}"))
            .Append(new DecisionOption("skip", "Skip potion", "hold consumables for later"))
            .ToList();

        if (available.Count == 0)
        {
            return new PotionDecision(null, null, "DeepSeek saw no usable consumables.", new DecisionTrace("Consumable", "LLM/DeepSeek", "Hold consumables", "DeepSeek saw no legal consumable use in combat."));
        }

        var response = await ChooseOptionAsync(
            "Choose whether to use a combat consumable in Slay the Spire 2. Return JSON with fields key and reason.",
            BuildSharedContext(
                analysis,
                $"Combat state: Hp={player.Creature.CurrentHp}/{player.Creature.MaxHp}; Potions={string.Join(", ", available.Select(potion => potion.Title.GetFormattedText()))}; PlayableCards={string.Join(", ", playableCards.Take(8).Select(card => card.Title))}; Enemies={string.Join(", ", enemies.Where(enemy => enemy.IsAlive).Select(enemy => $"{enemy.Name}:{enemy.CurrentHp}"))}"),
            decisionOptions,
            cancellationToken);

        if (response.Key == "skip")
        {
            return new PotionDecision(null, null, "DeepSeek chose to hold consumables.", new DecisionTrace("Consumable", "LLM/DeepSeek", "Hold consumables", response.Reason));
        }

        var selected = int.TryParse(response.Key, out var indexValue) && indexValue >= 0 && indexValue < available.Count
            ? available[indexValue]
            : available[0];

        return new PotionDecision(selected, ChoosePotionTarget(selected, player, enemies), $"DeepSeek selected {selected.Title}.", new DecisionTrace("Consumable", "LLM/DeepSeek", $"Use {selected.Title}", response.Reason));
    }

    public async Task<CombatDecision> ChooseCombatActionAsync(Player player, IReadOnlyList<CardModel> playableCards, IReadOnlyList<Creature> enemies, RunAnalysis analysis, CancellationToken cancellationToken)
    {
        var options = playableCards
            .Select(card => new DecisionOption(card.Id.Entry, $"Play {card.Title}", $"type={card.Type}, target={card.TargetType}"))
            .ToList();

        if (options.Count == 0)
        {
            return new CombatDecision(null, null, true, "DeepSeek saw no playable cards.", new DecisionTrace("Combat", "LLM/DeepSeek", "End turn", "DeepSeek saw no legal combat action, so it ended the turn."));
        }

        var response = await ChooseOptionAsync(
            "Choose the best combat action for Slay the Spire 2. Return JSON with fields key and reason.",
            BuildSharedContext(
                analysis,
                $"Combat state: Hp={player.Creature.CurrentHp}/{player.Creature.MaxHp}; PlayableCards={string.Join(", ", playableCards.Take(10).Select(card => card.Title))}; Enemies={string.Join(", ", enemies.Where(enemy => enemy.IsAlive).Select(enemy => $"{enemy.Name}:{enemy.CurrentHp}"))}"),
            options,
            cancellationToken);

        var card = playableCards.FirstOrDefault(c => c.Id.Entry == response.Key) ?? playableCards[0];
        Creature? target = card.TargetType == TargetType.AnyEnemy
            ? enemies.Where(e => e.IsAlive).OrderBy(e => e.CurrentHp).FirstOrDefault()
            : null;

        return new CombatDecision(card, target, false, $"DeepSeek selected {card.Title}.", new DecisionTrace("Combat", "LLM/DeepSeek", $"Play {card.Title}", response.Reason));
    }

    public async Task<CardRewardDecision> ChooseCardRewardAsync(IReadOnlyList<CardModel> options, RunAnalysis analysis, CancellationToken cancellationToken)
    {
        var decisionOptions = options
            .Select(card => new DecisionOption(card.Id.Entry, card.Title, $"type={card.Type}, cost={card.EnergyCost.GetAmountToSpend()}"))
            .ToList();

        if (decisionOptions.Count == 0)
        {
            return new CardRewardDecision(null, "DeepSeek saw no card reward options.", new DecisionTrace("Card Reward", "LLM/DeepSeek", "No card reward", "DeepSeek saw no selectable card rewards."));
        }

        var response = await ChooseOptionAsync(
            "Choose the best card reward for the current STS2 run. Return JSON with fields key and reason.",
            BuildSharedContext(
                analysis,
                BuildCardRewardStateContext(options, analysis)),
            decisionOptions,
            cancellationToken);

        var card = options.FirstOrDefault(c => c.Id.Entry == response.Key) ?? options[0];
        return new CardRewardDecision(card, $"DeepSeek preferred {card.Title}.", new DecisionTrace("Card Reward", "LLM/DeepSeek", $"Take {card.Title}", response.Reason));
    }

    public async Task<RewardDecision> ChooseRewardAsync(IReadOnlyList<NRewardButton> options, bool hasOpenPotionSlots, RunAnalysis analysis, CancellationToken cancellationToken)
    {
        var available = options.Where(o => o.IsEnabled).ToList();
        if (!hasOpenPotionSlots)
        {
            available = available.Where(o => o.Reward?.GetType().Name != "PotionReward").ToList();
        }

        var decisionOptions = available
            .Select((button, index) => new DecisionOption(index.ToString(), button.Reward?.GetType().Name ?? "Unknown", "reward button"))
            .ToList();

        if (decisionOptions.Count == 0)
        {
            return new RewardDecision(null, "DeepSeek saw no rewards.", new DecisionTrace("Rewards", "LLM/DeepSeek", "No reward", "DeepSeek saw no valid reward choices."));
        }

        var response = await ChooseOptionAsync(
            "Choose the best reward to claim in STS2. Return JSON with fields key and reason.",
            BuildSharedContext(
                analysis,
                $"Reward state: HasPotionSlot={hasOpenPotionSlots}; Rewards={string.Join(", ", available.Select(button => button.Reward?.GetType().Name ?? "Unknown"))}"),
            decisionOptions,
            cancellationToken);

        var selected = int.TryParse(response.Key, out var indexValue) && indexValue >= 0 && indexValue < available.Count
            ? available[indexValue]
            : available[0];

        return new RewardDecision(selected, $"DeepSeek selected {selected.Reward?.GetType().Name ?? "reward"}.", new DecisionTrace("Rewards", "LLM/DeepSeek", $"Take {selected.Reward?.GetType().Name ?? "reward"}", response.Reason));
    }

    public async Task<ShopDecision> ChooseShopPurchaseAsync(IReadOnlyList<MerchantEntry> options, int currentGold, bool hasOpenPotionSlots, RunAnalysis analysis, CancellationToken cancellationToken)
    {
        var available = options
            .Where(option => option.IsStocked && option.EnoughGold)
            .Where(option => hasOpenPotionSlots || option is not MerchantPotionEntry)
            .ToList();

        var decisionOptions = available
            .Select((entry, index) => new DecisionOption(index.ToString(), DescribeShopEntry(entry), $"cost={entry.Cost}"))
            .Append(new DecisionOption("skip", "Leave shop", "save gold for later"))
            .ToList();

        if (available.Count == 0)
        {
            return new ShopDecision(null, "DeepSeek saw no affordable purchases.", new DecisionTrace("Shop", "LLM/DeepSeek", "Leave shop", "DeepSeek saw no affordable shop purchases."));
        }

        var response = await ChooseOptionAsync(
            "Choose the best shop purchase in Slay the Spire 2. Return JSON with fields key and reason.",
            BuildSharedContext(
                analysis,
                BuildShopStateContext(available, currentGold, hasOpenPotionSlots, analysis)),
            decisionOptions,
            cancellationToken);

        if (response.Key == "skip")
        {
            return new ShopDecision(null, "DeepSeek chose to save gold.", new DecisionTrace("Shop", "LLM/DeepSeek", "Leave shop", response.Reason));
        }

        var selected = int.TryParse(response.Key, out var indexValue) && indexValue >= 0 && indexValue < available.Count
            ? available[indexValue]
            : available[0];

        return new ShopDecision(selected, $"DeepSeek selected {DescribeShopEntry(selected)}.", new DecisionTrace("Shop", "LLM/DeepSeek", $"Buy {DescribeShopEntry(selected)}", response.Reason));
    }

    public async Task<RestDecision> ChooseRestSiteOptionAsync(Player player, IReadOnlyList<RestSiteOption> options, RunAnalysis analysis, CancellationToken cancellationToken)
    {
        var available = options.Where(option => option.IsEnabled).ToList();
        var decisionOptions = available
            .Select(option => new DecisionOption(option.OptionId, option.Title.GetFormattedText(), TrimText(option.Description.GetFormattedText(), 120)))
            .ToList();

        if (decisionOptions.Count == 0)
        {
            return new RestDecision(null, "DeepSeek saw no rest-site options.", new DecisionTrace("Rest Site", "LLM/DeepSeek", "No campfire action", "DeepSeek saw no enabled rest site options."));
        }

        var response = await ChooseOptionAsync(
            "Choose the best rest-site action in Slay the Spire 2. Weigh current HP, upgrade value, deck weakness, and long-term scaling. Return JSON with fields key and reason.",
            BuildSharedContext(
                analysis,
                BuildRestSiteStateContext(player, available)),
            decisionOptions,
            cancellationToken);

        var selected = available.FirstOrDefault(option => option.OptionId == response.Key) ?? available[0];
        return new RestDecision(selected, $"DeepSeek selected {selected.Title.GetFormattedText()}.", new DecisionTrace("Rest Site", "LLM/DeepSeek", $"Choose {selected.Title.GetFormattedText()}", response.Reason));
    }

    public async Task<MapDecision> ChooseMapPointAsync(IReadOnlyList<MapPoint> options, int currentHp, int maxHp, int gold, RunAnalysis analysis, CancellationToken cancellationToken)
    {
        var decisionOptions = options
            .Select(point => new DecisionOption(point.ToString(), point.PointType.ToString(), $"row={point.coord.row}, col={point.coord.col}"))
            .ToList();

        if (decisionOptions.Count == 0)
        {
            return new MapDecision(null, "DeepSeek saw no map options.", new DecisionTrace("Map", "LLM/DeepSeek", "No map move", "DeepSeek saw no travelable map nodes."));
        }

        var context = BuildSharedContext(
            analysis,
            BuildMapStateContext(options, currentHp, maxHp, gold));
        var response = await ChooseOptionAsync(
            "Choose the best next map point in STS2. Prefer a strong but safe path. Return JSON with fields key and reason.",
            context,
            decisionOptions,
            cancellationToken);

        var pointChoice = options.FirstOrDefault(point => point.ToString() == response.Key) ?? options[0];
        return new MapDecision(pointChoice, $"DeepSeek selected {pointChoice.PointType}.", new DecisionTrace("Map", "LLM/DeepSeek", $"Go to {pointChoice.PointType} ({pointChoice.coord.row},{pointChoice.coord.col})", response.Reason));
    }

    private async Task<LlmDecisionResponse> ChooseOptionAsync(string instruction, string context, IReadOnlyList<DecisionOption> options, CancellationToken cancellationToken)
    {
        var optionsText = string.Join("\n", options.Select(option => $"- key={option.Key}; label={option.Label}; hint={option.ReasonHint}"));
        var prompt = $"{instruction}\nContext:\n{context}\nOptions:\n{optionsText}\nOutput format:\n{{\"key\":\"exact option key\",\"reason\":\"short explanation in Chinese\"}}";

        if (_config.Logging.LogDecisionPrompt)
        {
            Log.Info($"[AiBot] DeepSeek prompt:\n{prompt}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                model = _config.Provider.Model,
                temperature = 0.2,
                messages = new object[]
                {
                    new { role = "system", content = "You are selecting safe, legal, high-quality actions for a Slay the Spire 2 autoplayer. You must reason like a strong human player using the provided character guide, build guide, card guide, relic guide, item notes, current state, deck, relics, potions, and options. Prefer long-term synergy, survival, scaling, tempo, and legal actions. Output only one JSON object." },
                    new { role = "user", content = prompt }
                }
            }), Encoding.UTF8, "application/json")
        };

        using var httpResponse = await _httpClient.SendAsync(request, cancellationToken);
        httpResponse.EnsureSuccessStatusCode();

        var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
        var parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(body, JsonOptions);
        var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("DeepSeek returned an empty completion.");
        }

        if (TryParseDecisionResponse(content, out var decisionResponse))
        {
            return decisionResponse;
        }

        var fallbackKey = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0].Trim();
        return new LlmDecisionResponse(fallbackKey, $"DeepSeek selected option {fallbackKey}, but did not provide a structured reason.");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static Creature? ChoosePotionTarget(PotionModel potion, Player player, IReadOnlyList<Creature> enemies)
    {
        return potion.TargetType switch
        {
            TargetType.AnyEnemy => enemies.Where(enemy => enemy.IsAlive).OrderBy(enemy => enemy.CurrentHp).FirstOrDefault(),
            TargetType.AnyAlly or TargetType.AnyPlayer or TargetType.Self => player.Creature,
            _ => null
        };
    }

    private static string DescribeShopEntry(MerchantEntry entry)
    {
        return entry switch
        {
            MerchantCardEntry cardEntry when cardEntry.CreationResult?.Card is not null => $"card {cardEntry.CreationResult.Card.Title}",
            MerchantRelicEntry relicEntry when relicEntry.Model is not null => $"relic {relicEntry.Model.Title}",
            MerchantPotionEntry potionEntry when potionEntry.Model is not null => $"potion {potionEntry.Model.Title}",
            MerchantCardRemovalEntry => "card removal",
            _ => "shop item"
        };
    }

    private string BuildCardRewardStateContext(IReadOnlyList<CardModel> options, RunAnalysis analysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Reward state:");
        sb.AppendLine($"Card reward options={options.Count}");

        foreach (var card in options.Take(6))
        {
            sb.AppendLine($"- {DescribeCardOption(card, analysis)}");
        }

        return sb.ToString().Trim();
    }

    private string BuildShopStateContext(IReadOnlyList<MerchantEntry> options, int currentGold, bool hasOpenPotionSlots, RunAnalysis analysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Shop state:");
        sb.AppendLine($"Gold={currentGold}; HasPotionSlot={hasOpenPotionSlots}; AffordableOptions={options.Count}");

        foreach (var entry in options.Take(10))
        {
            sb.AppendLine($"- {DescribeShopEntryDetailed(entry, analysis)}");
        }

        if (options.Any(option => option is MerchantCardRemovalEntry) && !string.IsNullOrWhiteSpace(analysis.RemovalCandidateSummary))
        {
            sb.AppendLine("Suggested removal targets:");
            sb.AppendLine(analysis.RemovalCandidateSummary);
        }

        return sb.ToString().Trim();
    }

    private static string BuildMapStateContext(IReadOnlyList<MapPoint> options, int currentHp, int maxHp, int gold)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Map state:");
        sb.AppendLine($"Hp={currentHp}/{maxHp}; Gold={gold}; MapOptions={options.Count}");

        foreach (var point in options.Take(8))
        {
            var next = point.Children
                .GroupBy(child => child.PointType)
                .OrderBy(group => group.Key.ToString())
                .Select(group => $"{group.Key}x{group.Count()}");

            var nextSummary = string.Join(", ", next);
            sb.AppendLine($"- {point.PointType} at ({point.coord.row},{point.coord.col}); next={(string.IsNullOrWhiteSpace(nextSummary) ? "none" : nextSummary)}");
        }

        return sb.ToString().Trim();
    }

    private static string BuildRestSiteStateContext(Player player, IReadOnlyList<RestSiteOption> options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Rest-site state:");
        sb.AppendLine($"Hp={player.Creature.CurrentHp}/{player.Creature.MaxHp}; Options={options.Count}");

        foreach (var option in options.Take(8))
        {
            sb.AppendLine($"- key={option.OptionId}; title={option.Title.GetFormattedText()}; text={TrimText(option.Description.GetFormattedText(), 140)}");
        }

        return sb.ToString().Trim();
    }

    private string DescribeCardOption(CardModel card, RunAnalysis analysis)
    {
        var guide = _knowledgeBase?.FindCard(card.Title, analysis.CharacterId);
        var keywords = JoinNames(card.Keywords.Select(keyword => keyword.ToString()), 4);
        var tags = JoinNames(card.Tags.Select(tag => tag.ToString()), 4);
        var cost = card.EnergyCost.CostsX ? "X" : card.EnergyCost.GetAmountToSpend().ToString();
        var description = TrimText(card.GetDescriptionForPile(PileType.Deck), 140);
        var guideNote = guide is null ? string.Empty : TrimText(guide.DescriptionEn, 110);

        return $"{card.Title}; type={card.Type}; rarity={card.Rarity}; cost={cost}; upgraded={card.IsUpgraded}; keywords={(string.IsNullOrWhiteSpace(keywords) ? "none" : keywords)}; tags={(string.IsNullOrWhiteSpace(tags) ? "none" : tags)}; text={description}{(string.IsNullOrWhiteSpace(guideNote) ? string.Empty : $"; guide={guideNote}")}";
    }

    private string DescribeShopEntryDetailed(MerchantEntry entry, RunAnalysis analysis)
    {
        return entry switch
        {
            MerchantCardEntry cardEntry when cardEntry.CreationResult?.Card is not null => $"card; gold={entry.Cost}; {DescribeCardOption(cardEntry.CreationResult.Card, analysis)}",
            MerchantRelicEntry relicEntry when relicEntry.Model is not null => DescribeRelicEntryDetailed(relicEntry, analysis),
            MerchantPotionEntry potionEntry when potionEntry.Model is not null => DescribePotionEntryDetailed(potionEntry),
            MerchantCardRemovalEntry => $"card removal; gold={entry.Cost}; use this only if trimming weak cards is worth more than saving gold",
            _ => $"shop item; gold={entry.Cost}"
        };
    }

    private string DescribeRelicEntryDetailed(MerchantRelicEntry entry, RunAnalysis analysis)
    {
        var model = entry.Model;
        if (model is null)
        {
            return $"relic; gold={entry.Cost}";
        }

        var guide = _knowledgeBase?.FindRelic(model.Title.GetFormattedText(), analysis.CharacterId);
        var relicText = TrimText(model.DynamicDescription.GetFormattedText(), 140);
        var guideNote = guide is null ? string.Empty : TrimText(guide.DescriptionEn, 110);
        return $"relic {model.Title.GetFormattedText()}; gold={entry.Cost}; text={relicText}{(string.IsNullOrWhiteSpace(guideNote) ? string.Empty : $"; guide={guideNote}")}";
    }

    private static string DescribePotionEntryDetailed(MerchantPotionEntry entry)
    {
        var model = entry.Model;
        if (model is null)
        {
            return $"potion; gold={entry.Cost}";
        }

        var text = TrimText(model.DynamicDescription.GetFormattedText(), 140);
        return $"potion {model.Title.GetFormattedText()}; gold={entry.Cost}; usage={model.Usage}; target={model.TargetType}; text={text}";
    }

    private static string JoinNames(IEnumerable<string> values, int maxCount)
    {
        return string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().Take(maxCount));
    }

    private static string TrimText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd() + "...";
    }

    private static string BuildSharedContext(RunAnalysis analysis, string stateContext)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Character: {analysis.CharacterName}");
        sb.AppendLine($"Recommended Build: {analysis.RecommendedBuildName}");

        if (!string.IsNullOrWhiteSpace(analysis.CharacterBrief))
        {
            sb.AppendLine("Character Brief:");
            sb.AppendLine(analysis.CharacterBrief);
        }

        if (!string.IsNullOrWhiteSpace(analysis.RecommendedBuildSummary))
        {
            sb.AppendLine("Build Notes:");
            sb.AppendLine(analysis.RecommendedBuildSummary);
        }

        if (!string.IsNullOrWhiteSpace(analysis.DeckSummary))
        {
            sb.AppendLine("Deck Summary:");
            sb.AppendLine(analysis.DeckSummary);
        }

        if (!string.IsNullOrWhiteSpace(analysis.RelicSummary))
        {
            sb.AppendLine("Relic Summary:");
            sb.AppendLine(analysis.RelicSummary);
        }

        if (!string.IsNullOrWhiteSpace(analysis.PotionSummary))
        {
            sb.AppendLine("Potion / Item Summary:");
            sb.AppendLine(analysis.PotionSummary);
        }

        if (!string.IsNullOrWhiteSpace(analysis.KnowledgeDigest))
        {
            sb.AppendLine("Knowledge Digest:");
            sb.AppendLine(analysis.KnowledgeDigest);
        }

        if (!string.IsNullOrWhiteSpace(analysis.RunProgressSummary))
        {
            sb.AppendLine("Run Progress:");
            sb.AppendLine(analysis.RunProgressSummary);
        }

        if (!string.IsNullOrWhiteSpace(analysis.PlayerStateSummary))
        {
            sb.AppendLine("Player State:");
            sb.AppendLine(analysis.PlayerStateSummary);
        }

        if (!string.IsNullOrWhiteSpace(analysis.CombatSummary))
        {
            sb.AppendLine("Combat Summary:");
            sb.AppendLine(analysis.CombatSummary);
        }

        if (!string.IsNullOrWhiteSpace(analysis.EnemySummary))
        {
            sb.AppendLine("Enemy Summary:");
            sb.AppendLine(analysis.EnemySummary);
        }

        if (!string.IsNullOrWhiteSpace(analysis.RecentHistorySummary))
        {
            sb.AppendLine("Recent Route History:");
            sb.AppendLine(analysis.RecentHistorySummary);
        }

        if (!string.IsNullOrWhiteSpace(analysis.StrategicNeedsSummary))
        {
            sb.AppendLine("Strategic Needs:");
            sb.AppendLine(analysis.StrategicNeedsSummary);
        }

        if (!string.IsNullOrWhiteSpace(analysis.DeckStructureSummary))
        {
            sb.AppendLine("Deck Structure:");
            sb.AppendLine(analysis.DeckStructureSummary);
        }

        if (!string.IsNullOrWhiteSpace(analysis.RemovalCandidateSummary))
        {
            sb.AppendLine("Removal Candidates:");
            sb.AppendLine(analysis.RemovalCandidateSummary);
        }

        sb.AppendLine("Current State:");
        sb.AppendLine(stateContext);
        return sb.ToString().Trim();
    }

    private sealed class ChatCompletionResponse
    {
        public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        public Message? Message { get; set; }
    }

    private sealed class Message
    {
        public string? Content { get; set; }
    }

    private static bool TryParseDecisionResponse(string content, out LlmDecisionResponse response)
    {
        response = new LlmDecisionResponse(string.Empty, string.Empty);

        try
        {
            var start = content.IndexOf('{');
            var end = content.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                var json = content[start..(end + 1)];
                var parsed = JsonSerializer.Deserialize<LlmDecisionResponse>(json, JsonOptions);
                if (parsed is not null && !string.IsNullOrWhiteSpace(parsed.Key))
                {
                    response = parsed with { Reason = string.IsNullOrWhiteSpace(parsed.Reason) ? $"DeepSeek selected option {parsed.Key}." : parsed.Reason.Trim() };
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private sealed record LlmDecisionResponse(string Key, string Reason);
}
