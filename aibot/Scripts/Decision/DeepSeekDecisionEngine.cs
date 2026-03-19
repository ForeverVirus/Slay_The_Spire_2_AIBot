using System.Net.Http.Headers;
using System.Threading;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
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
    private int _completedRequestCount;

    public DeepSeekDecisionEngine(AiBotConfig config, GuideKnowledgeBase? knowledgeBase = null)
    {
        _config = config;
        _knowledgeBase = knowledgeBase;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(config.Provider.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(Math.Max(5, config.DecisionTimeoutSeconds))
        };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.Provider.ApiKey);
    }

    public async Task<PotionDecision> ChoosePotionUseAsync(Player player, IReadOnlyList<PotionModel> usablePotions, IReadOnlyList<CardModel> playableCards, IReadOnlyList<Creature> enemies, RunAnalysis analysis, CancellationToken cancellationToken)
    {
        var available = usablePotions
            .Where(potion => potion.Usage is PotionUsage.CombatOnly or PotionUsage.AnyTime)
            .Where(potion => !potion.IsQueued && !potion.HasBeenRemovedFromState && potion.PassesCustomUsabilityCheck)
            .ToList();

        var actionOptions = BuildPotionActionOptions(player, available, enemies);
        var decisionOptions = actionOptions
            .Select((option, index) => new DecisionOption(index.ToString(), option.Label, option.Hint))
            .Append(new DecisionOption("skip", "Skip potion", "hold consumables for later"))
            .ToList();

        if (actionOptions.Count == 0)
        {
            return new PotionDecision(null, null, "DeepSeek saw no usable consumables.", new DecisionTrace("Consumable", "LLM/DeepSeek", "Hold consumables", "DeepSeek saw no legal consumable use in combat."));
        }

        var response = await ChooseOptionAsync(
            "Choose whether to use a combat consumable in Slay the Spire 2. When a potion needs a target, pick the best target. "
            + "Potion economy: (1) Use potions to prevent significant HP loss or to secure a kill on a dangerous enemy. "
            + "(2) Save strong potions for elite and boss fights unless you will die or lose excessive HP without using them now. "
            + "(3) Block/Weak potions are excellent when facing large incoming damage this turn. "
            + "(4) Use potions if potion slots are full and a new potion reward is coming. "
            + "(5) If the fight is easy and HP is healthy, hold potions for harder fights ahead. "
            + "Return JSON with fields key and reason.",
            BuildSharedContext(
                analysis,
                BuildPotionStateContext(player, playableCards, enemies, actionOptions)),
            decisionOptions,
            cancellationToken);

        if (response.Key == "skip")
        {
            return new PotionDecision(null, null, "DeepSeek chose to hold consumables.", new DecisionTrace("Consumable", "LLM/DeepSeek", "Hold consumables", response.Reason));
        }

        var selected = int.TryParse(response.Key, out var indexValue) && indexValue >= 0 && indexValue < actionOptions.Count
            ? actionOptions[indexValue]
            : actionOptions[0];

        return new PotionDecision(selected.Potion, selected.Target, $"DeepSeek selected {selected.Summary}.", new DecisionTrace("Consumable", "LLM/DeepSeek", $"Use {selected.Summary}", response.Reason));
    }

    public async Task<CombatDecision> ChooseCombatActionAsync(Player player, IReadOnlyList<CardModel> playableCards, IReadOnlyList<Creature> enemies, RunAnalysis analysis, CancellationToken cancellationToken)
    {
        var actionOptions = BuildCombatActionOptions(player, playableCards, enemies, analysis);
        var options = actionOptions
            .Select((option, index) => new DecisionOption(index.ToString(), option.Label, option.Hint))
            .Append(new DecisionOption("end_turn", "End turn", "ENDS YOUR TURN IMMEDIATELY. All remaining energy is LOST (energy never carries over). All non-Retain hand cards are DISCARDED. All block gained this turn will be REMOVED at the start of next turn. Only choose this when you truly have zero useful plays left."))
            .ToList();

        if (actionOptions.Count == 0)
        {
            return new CombatDecision(null, null, true, "DeepSeek saw no playable cards.", new DecisionTrace("Combat", "LLM/DeepSeek", "End turn", "DeepSeek saw no legal combat action, so it ended the turn."));
        }

        var response = await ChooseOptionAsync(
            "Choose the best combat action for Slay the Spire 2. You are choosing ONE action at a time (one card play or end turn). "
            + "CRITICAL TURN MECHANICS (never forget): "
            + "• Energy does NOT carry over — if you have 2 energy left and end the turn, those 2 energy are WASTED. Next turn you get a fresh full energy bar (e.g., 3/3), NOT the leftover. "
            + "• Hand cards do NOT carry over — when you end the turn, ALL cards remaining in your hand (except those with the Retain keyword) are DISCARDED to the discard pile. They are NOT available next turn. "
            + "• Block does NOT carry over — all block you gained this turn is REMOVED at the start of your NEXT turn (unless a relic like Calipers or a power like Barricade explicitly preserves it). Block only protects you from damage THIS turn. "
            + "• Deck cycling: when the draw pile is empty, the entire discard pile is reshuffled into a new draw pile, then you draw from it. "
            + "CONSEQUENCE: You must spend ALL available energy every turn on useful plays. Ending the turn with unspent energy and playable cards is almost always a mistake. Even a mediocre card play (a basic Strike or Defend) is far better than wasting energy. "
            + "Play-order heuristic: (1) Check if you can kill an enemy this turn — if yes, prioritize lethal. "
            + "(2) Play Power cards early — they compound value every remaining turn. "
            + "(3) Play draw/cycle cards before committing to damage/block — see more options first. "
            + "(4) If enemies are attacking, use block cards to cover incoming damage BEFORE spending remaining energy on offense. "
            + "(5) If enemies are NOT attacking, skip block entirely — focus on offense, scaling, or setup. "
            + "(6) Multi-hit attacks scale with Strength — prefer them when Strength is high. "
            + "(7) After all high-value plays, spend any remaining energy on whatever is available (even basic Strikes/Defends) rather than ending the turn with unspent energy. "
            + "HP is a finite run-wide resource. Blocking incoming damage is almost always worth the energy. "
            + "Return JSON with fields key and reason.",
            BuildSharedContext(
                analysis,
                BuildCombatActionStateContext(player, actionOptions, enemies, analysis)),
            options,
            cancellationToken);

        if (response.Key == "end_turn")
        {
            return new CombatDecision(null, null, true, "DeepSeek chose to end the turn.", new DecisionTrace("Combat", "LLM/DeepSeek", "End turn", response.Reason));
        }

        var selected = int.TryParse(response.Key, out var indexValue) && indexValue >= 0 && indexValue < actionOptions.Count
            ? actionOptions[indexValue]
            : actionOptions[0];

        return new CombatDecision(selected.Card, selected.Target, false, $"DeepSeek selected {selected.Summary}.", new DecisionTrace("Combat", "LLM/DeepSeek", $"Play {selected.Summary}", response.Reason));
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
            "Choose the best card reward for the current Slay the Spire 2 run. Deck-building principles: "
            + "(1) Prioritize cards that fit the recommended build archetype — synergy compounds over many fights. "
            + "(2) Early acts: value frontloaded damage and reliable defense to survive immediate threats. "
            + "(3) Later acts: value scaling, Powers, and combo pieces that win long boss fights. "
            + "(4) Avoid deck bloat — a lean deck draws key cards more often. Skip mediocre cards if no option improves the deck. "
            + "(5) Rare cards are strong but only if they synergize; do not take a rare just for rarity. "
            + "(6) Block cards, draw/cycle cards, and energy-efficient cards are universally valuable. "
            + "Return JSON with fields key and reason.",
            BuildSharedContext(
                analysis,
                BuildCardRewardStateContext(options, analysis)),
            decisionOptions,
            cancellationToken);

        var card = options.FirstOrDefault(c => c.Id.Entry == response.Key) ?? options[0];
        return new CardRewardDecision(card, $"DeepSeek preferred {card.Title}.", new DecisionTrace("Card Reward", "LLM/DeepSeek", $"Take {card.Title}", response.Reason));
    }

    public async Task<CardSelectionDecision> ChooseCardSelectionAsync(AiCardSelectionContext request, IReadOnlyList<CardModel> options, RunAnalysis analysis, CancellationToken cancellationToken)
    {
        var available = options.ToList();
        var decisionOptions = available
            .Select((card, index) => new DecisionOption(index.ToString(), card.Title, $"type={card.Type}, cost={card.EnergyCost.GetAmountToSpend()}, upgraded={card.IsUpgraded}"))
            .ToList();

        if (request.MinSelect == 0 || request.Cancelable)
        {
            decisionOptions.Add(new DecisionOption("skip", "Stop selecting", "finish this optional card selection now"));
        }

        if (available.Count == 0)
        {
            return new CardSelectionDecision(null, true, "DeepSeek saw no selectable cards.", new DecisionTrace("Card Selection", "LLM/DeepSeek", "No card selection", $"DeepSeek saw no selectable cards for {DescribeSelectionKind(request.Kind)}."));
        }

        var response = await ChooseOptionAsync(
            "Choose the best card for this Slay the Spire 2 selection screen. "
            + "Selection-specific guidance: For REMOVAL — target curses, statuses, basic Strikes, then basic Defends (deck thinning raises draw quality). "
            + "For UPGRADE — upgrade cards you play every fight: key Powers, high-value Attacks, and block cards benefit most. "
            + "For DISCARD — discard situational or dead cards (expensive cards you cannot play, unsynergistic draws). "
            + "For TRANSFORM — transform weak basic cards for a chance at better replacements. "
            + "Only stop selecting early when every remaining option is neutral or harmful. "
            + "Return JSON with fields key and reason.",
            BuildSharedContext(
                analysis,
                BuildCardSelectionStateContext(request, available, analysis)),
            decisionOptions,
            cancellationToken);

        if (response.Key == "skip")
        {
            return new CardSelectionDecision(null, true, "DeepSeek chose to stop selecting.", new DecisionTrace("Card Selection", "LLM/DeepSeek", "Stop selecting", response.Reason));
        }

        var selected = int.TryParse(response.Key, out var indexValue) && indexValue >= 0 && indexValue < available.Count
            ? available[indexValue]
            : available[0];

        return new CardSelectionDecision(selected, false, $"DeepSeek selected {selected.Title}.", new DecisionTrace("Card Selection", "LLM/DeepSeek", $"Select {selected.Title}", response.Reason));
    }

    public async Task<CardRewardChoiceDecision> ChooseCardRewardChoiceAsync(AiCardSelectionContext request, IReadOnlyList<CardModel> options, IReadOnlyList<DecisionOption> alternatives, RunAnalysis analysis, CancellationToken cancellationToken)
    {
        var decisionOptions = options
            .Select((card, index) => new DecisionOption($"card:{index}", card.Title, $"type={card.Type}, cost={card.EnergyCost.GetAmountToSpend()}"))
            .Concat(alternatives.Select(option => new DecisionOption($"alt:{option.Key}", option.Label, option.ReasonHint)))
            .ToList();

        if (decisionOptions.Count == 0)
        {
            return new CardRewardChoiceDecision(null, null, "DeepSeek saw no reward choices.", new DecisionTrace("Card Reward", "LLM/DeepSeek", "No reward choice", "DeepSeek saw no card rewards or reward alternatives."));
        }

        var response = await ChooseOptionAsync(
            "Choose the best card reward action in Slay the Spire 2. You may take a card, skip, or reroll if offered. "
            + "Skip if no offered card improves the deck — deck bloat is a real threat. A tight 15-25 card deck draws key cards more reliably than a bloated 30+ card deck. "
            + "Reroll if offered and all current options are mediocre but better options may exist. "
            + "Take a card only if it clearly strengthens the build archetype, fills a strategic gap (defense, scaling, damage), or is a rare/powerful card with strong synergy. "
            + "Return JSON with fields key and reason.",
            BuildSharedContext(
                analysis,
                BuildCardRewardChoiceStateContext(request, options, alternatives, analysis)),
            decisionOptions,
            cancellationToken);

        if (response.Key.StartsWith("alt:", StringComparison.OrdinalIgnoreCase))
        {
            var alternativeKey = response.Key[4..];
            var selectedAlternative = alternatives.FirstOrDefault(option => string.Equals(option.Key, alternativeKey, StringComparison.OrdinalIgnoreCase)) ?? alternatives.First();
            return new CardRewardChoiceDecision(null, selectedAlternative.Key, $"DeepSeek selected reward alternative {selectedAlternative.Label}.", new DecisionTrace("Card Reward", "LLM/DeepSeek", $"Choose {selectedAlternative.Label}", response.Reason));
        }

        var selected = TryResolveIndexedCard(response.Key, "card:", options) ?? options[0];
        return new CardRewardChoiceDecision(selected, null, $"DeepSeek selected {selected.Title}.", new DecisionTrace("Card Reward", "LLM/DeepSeek", $"Take {selected.Title}", response.Reason));
    }

    public async Task<BundleChoiceDecision> ChooseBundleAsync(AiCardSelectionContext request, IReadOnlyList<CardBundleOption> bundles, RunAnalysis analysis, CancellationToken cancellationToken)
    {
        var decisionOptions = bundles
            .Select(bundle => new DecisionOption(bundle.Index.ToString(), $"Bundle {bundle.Index + 1}", string.Join(", ", bundle.Cards.Take(3).Select(card => card.Title.ToString()))))
            .ToList();

        if (decisionOptions.Count == 0)
        {
            return new BundleChoiceDecision(-1, "DeepSeek saw no bundle choices.", new DecisionTrace("Bundle Choice", "LLM/DeepSeek", "No bundle choice", "DeepSeek saw no selectable card bundles."));
        }

        var response = await ChooseOptionAsync(
            "Choose the strongest card bundle for the current Slay the Spire 2 run. "
            + "Evaluate bundles holistically: favor the bundle whose cards collectively strengthen the build archetype, improve synergy, and fill strategic gaps. "
            + "A bundle with one great card and two mediocre cards may be worse than a bundle with three good cards. "
            + "Return JSON with fields key and reason.",
            BuildSharedContext(
                analysis,
                BuildBundleChoiceStateContext(request, bundles, analysis)),
            decisionOptions,
            cancellationToken);

        var selectedIndex = int.TryParse(response.Key, out var indexValue) && bundles.Any(bundle => bundle.Index == indexValue)
            ? indexValue
            : bundles[0].Index;

        return new BundleChoiceDecision(selectedIndex, $"DeepSeek selected bundle {selectedIndex}.", new DecisionTrace("Bundle Choice", "LLM/DeepSeek", $"Choose bundle {selectedIndex}", response.Reason));
    }

    public async Task<CrystalSphereActionDecision> ChooseCrystalSphereActionAsync(CrystalSphereMinigame minigame, RunAnalysis analysis, CancellationToken cancellationToken)
    {
        var actions = BuildCrystalSphereActionCandidates(minigame)
            .OrderByDescending(action => action.Score)
            .Take(16)
            .ToList();

        if (actions.Count == 0)
        {
            return new CrystalSphereActionDecision(0, 0, true, "DeepSeek saw no Crystal Sphere actions.", new DecisionTrace("Crystal Sphere", "LLM/DeepSeek", "No divination action", "DeepSeek saw no hidden Crystal Sphere cells."));
        }

        var response = await ChooseOptionAsync(
            "Choose the best Crystal Sphere divination action in Slay the Spire 2. Big divination reveals a 3x3 area and small divination reveals one cell. Prefer actions that reveal the most useful information and maximize expected reward value across remaining searches. Return JSON with fields key and reason.",
            BuildSharedContext(
                analysis,
                BuildCrystalSphereStateContext(minigame, actions)),
            actions.Select((action, index) => new DecisionOption(index.ToString(), action.Label, action.Hint)).ToList(),
            cancellationToken);

        var selected = int.TryParse(response.Key, out var indexValue) && indexValue >= 0 && indexValue < actions.Count
            ? actions[indexValue]
            : actions[0];

        var toolLabel = selected.UseBigDivination ? "big" : "small";
        return new CrystalSphereActionDecision(selected.X, selected.Y, selected.UseBigDivination, $"DeepSeek selected {toolLabel} divination at ({selected.X},{selected.Y}).", new DecisionTrace("Crystal Sphere", "LLM/DeepSeek", $"Use {toolLabel} divination at ({selected.X},{selected.Y})", response.Reason));
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
            "Choose the best reward to claim in Slay the Spire 2. Relics are permanent and usually highest priority. Gold is flexible. Potions are only valuable if slots are open and a tough fight is ahead. Card rewards should be evaluated for build fit. Return JSON with fields key and reason.",
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
            "Choose the best shop purchase in Slay the Spire 2. "
            + "Shop economics: (1) Card removal (removing basic Strikes/Defends) is often the best purchase — it thins the deck and improves every future draw. "
            + "(2) Relics are permanent upgrades and usually the best value if affordable and synergistic. "
            + "(3) Cards from the shop can fill critical gaps (missing defense, scaling, or combo piece). "
            + "(4) Potions are low priority unless a tough elite or boss fight is imminent. "
            + "(5) Save 100+ gold buffer for future shops if no purchase is compelling. "
            + "(6) Leave the shop if nothing significantly improves the run — gold in reserve has value. "
            + "Return JSON with fields key and reason.",
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
            "Choose the best rest-site action in Slay the Spire 2. "
            + "Decision framework: (1) REST (heal) if HP is below ~55% of max — survival is paramount; dead runs score zero. "
            + "(2) UPGRADE if HP is above ~55% — upgrading a key card (core Power, primary damage, best block card) improves every future fight. "
            + "(3) If a boss fight is the next node, prefer resting to enter at high HP unless the deck already handles the boss well. "
            + "(4) Other options (Dig for relic, Lift for Strength, etc.) can be strong — evaluate them against heal/upgrade value. "
            + "(5) Upgrade priority: unupgraded Powers > key archetype cards > block cards > basic Strikes/Defends. "
            + "Return JSON with fields key and reason.",
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
            "Choose the best next map point in Slay the Spire 2. "
            + "Routing strategy: (1) If HP is below 50%, avoid Elites and prefer rest sites, shops, or easy combats. "
            + "(2) If HP is healthy (above 70%), Elites offer valuable relics and are worth the risk. "
            + "(3) Plan 2-3 nodes ahead: a path with elite→rest is better than elite→elite if HP is limited. "
            + "(4) Unknown/question mark nodes can be events (potentially beneficial) — moderate risk, moderate reward. "
            + "(5) Shops are valuable when gold is above 100 or when card removal is needed. "
            + "(6) Before the boss: ensure a rest site or safe path to enter the boss fight at high HP. "
            + "Return JSON with fields key and reason.",
            context,
            decisionOptions,
            cancellationToken);

        var pointChoice = options.FirstOrDefault(point => point.ToString() == response.Key) ?? options[0];
        return new MapDecision(pointChoice, $"DeepSeek selected {pointChoice.PointType}.", new DecisionTrace("Map", "LLM/DeepSeek", $"Go to {pointChoice.PointType} ({pointChoice.coord.row},{pointChoice.coord.col})", response.Reason));
    }

    public async Task<EventDecision> ChooseEventOptionAsync(EventModel eventModel, IReadOnlyList<EventOption> options, RunAnalysis analysis, CancellationToken cancellationToken)
    {
        var available = options.Where(option => !option.IsLocked).ToList();
        var decisionOptions = available
            .Select((option, index) => new DecisionOption(index.ToString(), GetEventOptionLabel(option), BuildEventOptionHint(option)))
            .ToList();

        if (decisionOptions.Count == 0)
        {
            return new EventDecision(null, "DeepSeek saw no event options.", new DecisionTrace("Event", "LLM/DeepSeek", "No event choice", "DeepSeek saw no selectable event options."));
        }

        var response = await ChooseOptionAsync(
            "Choose the best event option in Slay the Spire 2. "
            + "Event evaluation: (1) Avoid options that kill you (lethal=true) or cost more HP than you can afford. "
            + "(2) Free relic, free card removal, or free upgrade options are almost always worth taking. "
            + "(3) Small HP costs (5-10) for good rewards are usually acceptable if HP is above 50%. "
            + "(4) Curse penalties are significant — a curse clogs the deck for potentially many fights. "
            + "(5) Gold costs are fine if the reward is strong and current gold is sufficient. "
            + "(6) When in doubt, choose the option with the least irreversible downside. "
            + "Return JSON with fields key and reason.",
            BuildSharedContext(
                analysis,
                BuildEventStateContext(eventModel, available, analysis)),
            decisionOptions,
            cancellationToken);

        var selected = int.TryParse(response.Key, out var indexValue) && indexValue >= 0 && indexValue < available.Count
            ? available[indexValue]
            : available[0];

        return new EventDecision(selected, $"DeepSeek selected {GetEventOptionLabel(selected)}.", new DecisionTrace("Event", "LLM/DeepSeek", $"Choose {GetEventOptionLabel(selected)}", response.Reason));
    }

    public async Task<RelicChoiceDecision> ChooseRelicAsync(IReadOnlyList<RelicModel> options, string source, bool allowSkip, RunAnalysis analysis, CancellationToken cancellationToken)
    {
        var available = options.ToList();
        var decisionOptions = available
            .Select((relic, index) => new DecisionOption(index.ToString(), relic.Title.GetFormattedText(), $"rarity={relic.Rarity}; text={TrimText(relic.DynamicDescription.GetFormattedText(), 120)}"))
            .ToList();

        if (allowSkip)
        {
            decisionOptions.Add(new DecisionOption("skip", "Skip relic", "decline all relics on this screen"));
        }

        if (available.Count == 0)
        {
            return new RelicChoiceDecision(null, allowSkip, $"DeepSeek saw no relic choices in {source}.", new DecisionTrace("Relic Choice", "LLM/DeepSeek", allowSkip ? "Skip relic" : "No relic choice", $"DeepSeek saw no selectable relics in {source}."));
        }

        var response = await ChooseOptionAsync(
            allowSkip
                ? "Choose the best relic for the current Slay the Spire 2 run, or skip if all offered relics are strategically worse than declining. "
                  + "Relic evaluation: (1) Relics that synergize with the current build archetype are best (e.g. Strength relics for Ironclad Strength build, Shiv relics for Silent Shiv build). "
                  + "(2) Energy relics (+1 energy) are universally powerful. "
                  + "(3) Passive defense relics (Block generation, damage reduction) help survival across the entire run. "
                  + "(4) Boss relics with downsides: evaluate whether the downside is manageable for this specific deck. "
                  + "(5) Skip only when all relics actively hurt the build or have downsides that outweigh benefits. "
                  + "Return JSON with fields key and reason."
                : "Choose the best relic for the current Slay the Spire 2 run. "
                  + "Relic evaluation: (1) Relics that synergize with the current build archetype are best. "
                  + "(2) Energy relics are universally powerful. (3) Passive defense relics help survival across the run. "
                  + "(4) Boss relics with downsides: evaluate whether the downside is manageable for this deck. "
                  + "Return JSON with fields key and reason.",
            BuildSharedContext(
                analysis,
                BuildRelicChoiceStateContext(available, source, analysis)),
            decisionOptions,
            cancellationToken);

        if (allowSkip && response.Key == "skip")
        {
            return new RelicChoiceDecision(null, true, $"DeepSeek skipped relic selection in {source}.", new DecisionTrace("Relic Choice", "LLM/DeepSeek", "Skip relic", response.Reason));
        }

        var selected = int.TryParse(response.Key, out var indexValue) && indexValue >= 0 && indexValue < available.Count
            ? available[indexValue]
            : available[0];

        return new RelicChoiceDecision(selected, false, $"DeepSeek selected {selected.Title.GetFormattedText()}.", new DecisionTrace("Relic Choice", "LLM/DeepSeek", $"Choose {selected.Title.GetFormattedText()}", response.Reason));
    }

    private async Task<LlmDecisionResponse> ChooseOptionAsync(string instruction, string context, IReadOnlyList<DecisionOption> options, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
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
                            new { role = "system", content = BuildSystemPrompt() },
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

                Interlocked.Exchange(ref _completedRequestCount, 1);

                if (TryParseDecisionResponse(content, out var decisionResponse))
                {
                    return decisionResponse;
                }

                var fallbackKey = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0].Trim();
                return new LlmDecisionResponse(fallbackKey, $"DeepSeek selected option {fallbackKey}, but did not provide a structured reason.");
            }
            catch (Exception ex) when (ShouldRetryFirstRequest(ex, attempt, cancellationToken))
            {
                lastError = ex;
                Log.Warn($"[AiBot] DeepSeek first request failed on attempt {attempt + 1}; retrying once before local fallback. {ex.GetType().Name}: {ex.Message}");
                await Task.Delay(600, cancellationToken);
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }
        }

        throw lastError ?? new InvalidOperationException("DeepSeek decision request failed.");
    }

    private bool ShouldRetryFirstRequest(Exception ex, int attempt, CancellationToken cancellationToken)
    {
        if (attempt > 0)
        {
            return false;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return Interlocked.CompareExchange(ref _completedRequestCount, 0, 0) == 0;
    }

    private static string BuildSystemPrompt()
    {
        return "You are an expert Slay the Spire 2 autoplayer AI. You reason like a top-level human player who consistently wins high-ascension runs. "
            + "FUNDAMENTAL TURN RULES (these override everything else): "
            + "Rule A: Energy resets every turn — unspent energy is permanently lost. If you have playable cards and energy remaining, you MUST play them before ending the turn. "
            + "Rule B: Hand cards are discarded every turn — when you end the turn, all non-Retain hand cards go to the discard pile. You do NOT keep them for next turn. "
            + "Rule C: Block resets every turn — all block is removed at the start of your next turn (unless Barricade/Calipers). Block only protects you during the current enemy turn. "
            + "Rule D: Deck recycling — when the draw pile is empty, the discard pile is shuffled into a new draw pile. Cards you play/discard this turn eventually cycle back. "
            + "You deeply understand the 5 playable characters and their archetypes: "
            + "Ironclad (75 HP, Strength scaling, Exhaust synergy, self-heal via Burning Blood); "
            + "Silent (70 HP, Poison stacking, Shiv burst, card manipulation, Ring of the Snake); "
            + "Defect (75 HP, Orb management — Lightning for damage, Frost for Block, Dark for burst, Plasma for energy — Focus scaling); "
            + "Regent (68 HP, Star resource system — star generators + star spenders, Sovereign Blade forge, ally summoning); "
            + "Necrobinder (65 HP, lowest HP in game, Doom execution mechanic, Soul cards, Osty companion). "
            + "Key strategic principles you follow: "
            + "(1) HP is the most precious resource — every point of avoidable damage hurts the run. "
            + "(2) Deck quality beats deck size — lean 15-25 card decks draw key cards reliably. Card removal is often more valuable than adding a mediocre card. "
            + "(3) Scaling wins long fights (boss fights): Powers, Strength/Dexterity stacking, Focus, Poison, Doom. Frontloaded damage wins short fights (hallways, Act 1). "
            + "(4) Build around one archetype — synergistic cards compound; scattered picks dilute the deck. "
            + "(5) NEVER end the turn with unspent energy and playable cards — even a basic Strike/Defend is better than wasting energy. Energy wasted = value permanently lost. "
            + "(6) Block incoming damage first, then spend remaining energy on offense. Exception: if offense kills the enemy, that prevents all future damage. "
            + "(7) Route wisely: fight elites when healthy for relics, rest when below 55% HP, upgrade key cards at campfires when healthy, shop for removal when deck has weak cards. "
            + "Output exactly one JSON object with fields 'key' and 'reason'. The 'key' must be an exact option key from the provided list. The 'reason' should be a concise strategic explanation in Chinese.";
    }

    public void Dispose()
    {
        _httpClient.Dispose();
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

    private string BuildCardSelectionStateContext(AiCardSelectionContext request, IReadOnlyList<CardModel> options, RunAnalysis analysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Card selection state:");
        sb.AppendLine($"Kind={DescribeSelectionKind(request.Kind)}; Prompt={request.PromptText}; Min={request.MinSelect}; Max={request.MaxSelect}; Cancelable={request.Cancelable}; Zone={request.Zone}; Options={options.Count}");

        if (!string.IsNullOrWhiteSpace(request.ExtraInfo))
        {
            sb.AppendLine($"Extra={request.ExtraInfo}");
        }

        foreach (var card in options.Take(10))
        {
            sb.AppendLine($"- {DescribeCardOption(card, analysis)}");
        }

        return sb.ToString().Trim();
    }

    private string BuildCardRewardChoiceStateContext(AiCardSelectionContext request, IReadOnlyList<CardModel> options, IReadOnlyList<DecisionOption> alternatives, RunAnalysis analysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Card reward choice state:");
        sb.AppendLine($"Prompt={request.PromptText}; CardOptions={options.Count}; Alternatives={alternatives.Count}");

        foreach (var card in options.Take(8))
        {
            sb.AppendLine($"- card: {DescribeCardOption(card, analysis)}");
        }

        foreach (var alternative in alternatives)
        {
            sb.AppendLine($"- alternative key={alternative.Key}; label={alternative.Label}; hint={alternative.ReasonHint}");
        }

        return sb.ToString().Trim();
    }

    private string BuildBundleChoiceStateContext(AiCardSelectionContext request, IReadOnlyList<CardBundleOption> bundles, RunAnalysis analysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Bundle choice state:");
        sb.AppendLine($"Prompt={request.PromptText}; Bundles={bundles.Count}");

        foreach (var bundle in bundles.Take(6))
        {
            var cards = string.Join(" | ", bundle.Cards.Take(4).Select(card => DescribeCardOption(card, analysis)));
            sb.AppendLine($"- bundle={bundle.Index}; cards={cards}");
        }

        return sb.ToString().Trim();
    }

    private string BuildCrystalSphereStateContext(CrystalSphereMinigame minigame, IReadOnlyList<CrystalSphereActionOption> actions)
    {
        var sb = new StringBuilder();
        var hidden = 0;
        var revealedGood = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var revealedBad = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var x = 0; x < minigame.GridSize.X; x++)
        {
            for (var y = 0; y < minigame.GridSize.Y; y++)
            {
                var cell = minigame.cells[x, y];
                if (cell.IsHidden)
                {
                    hidden++;
                    continue;
                }

                if (cell.Item is null)
                {
                    continue;
                }

                var targetSet = cell.Item.IsGood ? revealedGood : revealedBad;
                targetSet.Add(cell.Item.GetType().Name);
            }
        }

        sb.AppendLine("Crystal Sphere state:");
        sb.AppendLine($"DivinationsLeft={minigame.DivinationCount}; CurrentTool={minigame.CrystalSphereTool}; HiddenCells={hidden}; RevealedGood={string.Join(", ", revealedGood.DefaultIfEmpty("none"))}; RevealedBad={string.Join(", ", revealedBad.DefaultIfEmpty("none"))}");

        foreach (var action in actions)
        {
            sb.AppendLine($"- {action.Label}; hint={action.Hint}");
        }

        return sb.ToString().Trim();
    }

    private string BuildPotionStateContext(Player player, IReadOnlyList<CardModel> playableCards, IReadOnlyList<Creature> enemies, IReadOnlyList<PotionActionOption> actionOptions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Potion state:");
        var hpPercent = player.Creature.MaxHp > 0 ? player.Creature.CurrentHp * 100 / player.Creature.MaxHp : 100;
        var allies = player.Creature.CombatState?.Allies;
        var incomingDamage = EstimateIncomingDamage(enemies, allies);
        var aliveEnemies = enemies.Where(enemy => enemy.IsAlive).ToList();
        var totalEnemyHp = aliveEnemies.Sum(enemy => enemy.CurrentHp);
        var fightDifficulty = totalEnemyHp > 120 || aliveEnemies.Any(e => e.MaxHp >= 100) ? "HARD (elite/boss-level)" : aliveEnemies.Count >= 3 ? "MODERATE (multi-enemy)" : "NORMAL (hallway)";
        sb.AppendLine($"PlayerHp={player.Creature.CurrentHp}/{player.Creature.MaxHp} ({hpPercent}%); IncomingDamage={incomingDamage}; EnemyCount={aliveEnemies.Count}; TotalEnemyHp={totalEnemyHp}; Difficulty={fightDifficulty}; PotionActions={actionOptions.Count}");
        sb.AppendLine($"Playable card snapshot={string.Join(", ", playableCards.Take(8).Select(card => card.Title.ToString()))}");

        foreach (var option in actionOptions.Take(12))
        {
            sb.AppendLine($"- {option.Summary}; hint={option.Hint}");
        }

        if (hpPercent < 40)
        {
            sb.AppendLine("LOW HP: Consider using potions now to survive this fight rather than saving for later.");
        }

        return sb.ToString().Trim();
    }

    private string BuildCombatActionStateContext(Player player, IReadOnlyList<CombatActionOption> actionOptions, IReadOnlyList<Creature> enemies, RunAnalysis analysis)
    {
        var sb = new StringBuilder();
        var combat = player.PlayerCombatState;
        sb.AppendLine("Combat action state:");
        var currentBlock = player.Creature.Block;
        var allies = player.Creature.CombatState?.Allies;
        var incomingDamage = EstimateIncomingDamage(enemies, allies);
        var turnNumber = player.Creature.CombatState?.RoundNumber ?? 1;
        var hpPercent = player.Creature.MaxHp > 0 ? player.Creature.CurrentHp * 100 / player.Creature.MaxHp : 100;

        // Calculate orb passive effects that trigger at end of turn
        var orbPassive = CalculateOrbPassiveEffects(combat);
        var effectiveBlock = currentBlock + orbPassive.FrostBlock;
        var blockDeficit = Math.Max(0, incomingDamage - effectiveBlock);

        sb.AppendLine($"Turn={turnNumber}; PlayerHp={player.Creature.CurrentHp}/{player.Creature.MaxHp} ({hpPercent}%); Block={currentBlock}; Energy={combat?.Energy ?? 0}/{combat?.MaxEnergy ?? 0}; PlayableActions={actionOptions.Count}");
        sb.AppendLine($"IncomingDamage={incomingDamage}; CurrentBlock={currentBlock}; UnblockedDamage={Math.Max(0, incomingDamage - currentBlock)}");

        // Show orb passive contributions if any
        if (orbPassive.FrostBlock > 0 || orbPassive.LightningDamage > 0 || orbPassive.DarkGrowth > 0 || orbPassive.GlassDamage > 0)
        {
            sb.AppendLine("END-OF-TURN ORB PASSIVES (automatic, no energy cost):");
            if (orbPassive.FrostBlock > 0)
            {
                sb.AppendLine($"- Frost orbs will grant +{orbPassive.FrostBlock} block at end of turn (from {orbPassive.FrostCount} Frost orb(s)). Effective total block = {effectiveBlock}. You only need {blockDeficit} more block from cards to fully cover incoming damage.");
            }
            if (orbPassive.LightningDamage > 0)
            {
                sb.AppendLine($"- Lightning orbs will deal {orbPassive.LightningDamage} damage to random enemies at end of turn (from {orbPassive.LightningCount} Lightning orb(s)). Factor this into lethal calculations.");
            }
            if (orbPassive.DarkGrowth > 0)
            {
                sb.AppendLine($"- Dark orbs will grow evoke value by +{orbPassive.DarkGrowth} at end of turn (from {orbPassive.DarkCount} Dark orb(s)).");
            }
            if (orbPassive.GlassDamage > 0)
            {
                sb.AppendLine($"- Glass orbs will deal {orbPassive.GlassDamage} damage to ALL enemies at end of turn (from {orbPassive.GlassCount} Glass orb(s)). Factor this into lethal calculations.");
            }
        }

        sb.AppendLine($"StrategicNeeds={TrimText(analysis.StrategicNeedsSummary, 220)}");

        // Enemy roster with per-enemy details
        var aliveEnemies = enemies.Where(enemy => enemy.IsAlive).ToList();
        if (aliveEnemies.Count > 0)
        {
            sb.AppendLine("Enemy roster:");
            foreach (var enemy in aliveEnemies.Take(6))
            {
                var enemyDamage = EstimateEnemyDamage(enemy, allies);
                var intentLabel = DescribeEnemyIntent(enemy);
                var enemyPowers = DescribeCreaturePowers(enemy, 4);
                sb.AppendLine($"- {enemy.Name}#{enemy.CombatId}: hp={enemy.CurrentHp}/{enemy.MaxHp}; block={enemy.Block}; intent={intentLabel}{(enemyDamage > 0 ? $"; damage={enemyDamage}" : "")}{(string.IsNullOrWhiteSpace(enemyPowers) ? "" : $"; powers={enemyPowers}")}");
            }
        }

        // Lethal opportunity detection (include Lightning + Glass passive damage)
        var lightningPassive = orbPassive.LightningDamage;
        var glassPassive = orbPassive.GlassDamage;
        var lethalTargets = aliveEnemies
            .Where(enemy => enemy.CurrentHp <= EstimateAvailableDamage(actionOptions, enemy) + glassPassive + (aliveEnemies.Count == 1 ? lightningPassive : lightningPassive / Math.Max(aliveEnemies.Count, 1)))
            .Select(enemy => enemy.Name)
            .ToList();
        if (lethalTargets.Count > 0)
        {
            sb.AppendLine($"LETHAL OPPORTUNITY: You can likely kill {string.Join(", ", lethalTargets)} this turn (including end-of-turn orb passives). Prioritize finishing them to remove their threat permanently.");
        }

        if (incomingDamage > 0 && effectiveBlock < incomingDamage)
        {
            sb.AppendLine($"WARNING: You will take {incomingDamage - effectiveBlock} damage after Frost passive block. You need {blockDeficit} more block from cards to fully cover incoming damage.");
        }
        else if (incomingDamage > 0 && effectiveBlock >= incomingDamage && currentBlock < incomingDamage)
        {
            sb.AppendLine($"FROST COVERAGE: Your {orbPassive.FrostCount} Frost orb(s) will passively grant +{orbPassive.FrostBlock} block at end of turn, which combined with your current {currentBlock} block covers all {incomingDamage} incoming damage. You do NOT need to play additional block cards for defense — spend energy on offense, scaling, or draw instead.");
        }

        if (combat is not null)
        {
            sb.AppendLine($"OrbSlots={combat.OrbQueue.Orbs.Count}/{combat.OrbQueue.Capacity}");
            if (combat.OrbQueue.Orbs.Count > 0)
            {
                sb.AppendLine("Current orbs:");
                foreach (var orb in combat.OrbQueue.Orbs.Take(6).Select((orb, index) => new { orb, index }))
                {
                    sb.AppendLine($"- slot {orb.index + 1}: {orb.orb.Title.GetFormattedText()}; passive={orb.orb.PassiveVal}; evoke={orb.orb.EvokeVal}; text={TrimText(FormatOrbDescription(orb.orb), 120)}");
                }
            }

            if (combat.OrbQueue.Capacity > 0)
            {
                sb.AppendLine(combat.OrbQueue.Orbs.Count >= combat.OrbQueue.Capacity
                    ? "Orb rule: orb slots are full, so channeling a new orb will immediately evoke the leftmost orb before inserting the new orb."
                    : "Orb rule: channeling a new orb adds it to the orb bar; when the bar is full, the leftmost orb is evoked to make room.");
            }
        }

        if (!string.IsNullOrWhiteSpace(analysis.CharacterCombatMechanicSummary))
        {
            sb.AppendLine($"CharacterMechanics={TrimText(analysis.CharacterCombatMechanicSummary, 500)}");
        }

        var uniqueCards = actionOptions
            .Select(option => option.Card)
            .DistinctBy(card => card.Id.Entry)
            .Take(8)
            .ToList();

        if (uniqueCards.Count > 0)
        {
            sb.AppendLine("Playable hand cards:");
            foreach (var card in uniqueCards)
            {
                sb.AppendLine($"- {DescribeCardOption(card, analysis)}");
            }
        }

        if (combat?.OrbQueue.Capacity > 0)
        {
            sb.AppendLine("Tactical reminder: for orb cards, evaluate both the card text AND the orb consequences. Channeling into full slots evokes the leftmost orb immediately. Frost/Lightning/Dark orbs also trigger passively at end of turn (already calculated above).");
        }
        if (incomingDamage > 0 && blockDeficit > 0)
        {
            sb.AppendLine($"Block priority: enemies deal {incomingDamage} total. After current block ({currentBlock}) + Frost passive (+{orbPassive.FrostBlock}), you still need {blockDeficit} more block from cards. Prioritize block cards for this deficit before spending energy on offense.");
        }
        else if (incomingDamage > 0 && blockDeficit <= 0)
        {
            sb.AppendLine($"Block is SUFFICIENT: current block ({currentBlock}) + Frost passive (+{orbPassive.FrostBlock}) >= incoming damage ({incomingDamage}). Spend all energy on offense, scaling, or draw — do NOT waste energy on extra block cards.");
        }
        else if (aliveEnemies.Count > 0)
        {
            sb.AppendLine("Enemies are NOT attacking this turn. Focus all energy on offense, scaling, setup, or draw. Do not waste energy on block when enemies are buffing/sleeping.");
        }

        var currentEnergy = combat?.Energy ?? 0;
        if (currentEnergy > 0 && actionOptions.Count > 0)
        {
            sb.AppendLine($"ENERGY WARNING: You have {currentEnergy} energy and {actionOptions.Count} playable card(s). Energy does NOT carry over — every unspent point is permanently wasted. Play cards until you run out of energy or playable options. Even a basic Strike or Defend is better than ending the turn with unspent energy.");
        }

        foreach (var option in actionOptions.Take(14))
        {
            sb.AppendLine($"- {option.Summary}; hint={option.Hint}");
        }

        return sb.ToString().Trim();
    }

    private string BuildShopStateContext(IReadOnlyList<MerchantEntry> options, int currentGold, bool hasOpenPotionSlots, RunAnalysis analysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Shop state:");
        sb.AppendLine($"Gold={currentGold}; HasPotionSlot={hasOpenPotionSlots}; AffordableOptions={options.Count}");

        if (currentGold < 80)
        {
            sb.AppendLine("LOW GOLD: Be very selective. Prioritize card removal if affordable, then leave gold for future shops.");
        }

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
        var hpPercent = maxHp > 0 ? currentHp * 100 / maxHp : 100;
        sb.AppendLine($"Hp={currentHp}/{maxHp} ({hpPercent}%); Gold={gold}; MapOptions={options.Count}");

        if (hpPercent < 50)
        {
            sb.AppendLine("CAUTION: HP is below 50%. Prioritize rest sites and avoid elites unless a clear path to healing exists.");
        }

        foreach (var point in options.Take(8))
        {
            var next = point.Children
                .GroupBy(child => child.PointType)
                .OrderBy(group => group.Key.ToString())
                .Select(group => $"{group.Key}x{group.Count()}");

            var nextSummary = string.Join(", ", next);
            var hasRestAhead = point.Children.Any(child => child.PointType.ToString().Contains("Rest"));
            var riskLevel = point.PointType.ToString().Contains("Elite") ? " [HIGH RISK/REWARD]" : "";
            sb.AppendLine($"- {point.PointType} at ({point.coord.row},{point.coord.col}){riskLevel}; next={(string.IsNullOrWhiteSpace(nextSummary) ? "none" : nextSummary)}{(hasRestAhead ? " [rest ahead]" : "")}");
        }

        return sb.ToString().Trim();
    }

    private static string BuildRestSiteStateContext(Player player, IReadOnlyList<RestSiteOption> options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Rest-site state:");
        var hpPercent = player.Creature.MaxHp > 0 ? player.Creature.CurrentHp * 100 / player.Creature.MaxHp : 100;
        sb.AppendLine($"Hp={player.Creature.CurrentHp}/{player.Creature.MaxHp} ({hpPercent}%); Options={options.Count}");

        if (hpPercent <= 55)
        {
            sb.AppendLine("HP is at or below 55% — resting is strongly recommended unless a key upgrade is critical for the next boss.");
        }

        foreach (var option in options.Take(8))
        {
            sb.AppendLine($"- key={option.OptionId}; title={option.Title.GetFormattedText()}; text={TrimText(SafeFormat(() => option.Description.GetFormattedText(), option.OptionId, $"rest-site option {option.OptionId}"), 140)}");
        }

        return sb.ToString().Trim();
    }

    private string BuildEventStateContext(EventModel eventModel, IReadOnlyList<EventOption> options, RunAnalysis analysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Event state:");
        sb.AppendLine($"Event={eventModel.Title.GetFormattedText()}; Id={eventModel.Id.Entry}; Options={options.Count}");
        sb.AppendLine($"EventDescription={TrimText(SafeFormat(() => eventModel.Description?.GetFormattedText() ?? string.Empty, eventModel.Id.Entry, $"event {eventModel.Id.Entry}"), 220)}");

        foreach (var option in options.Take(8))
        {
            var title = GetEventOptionLabel(option);
            var description = TrimText(SafeFormat(() => option.Description.GetFormattedText(), option.TextKey, $"event option {option.TextKey}"), 160);
            var relicInfo = option.Relic is null ? string.Empty : $"; relic={DescribeRelicDetailed(option.Relic, analysis)}";
            var killInfo = option.WillKillPlayer is null ? string.Empty : "; lethal=true";
            sb.AppendLine($"- key={option.TextKey}; title={title}; proceed={option.IsProceed}; text={description}{relicInfo}{killInfo}");
        }

        return sb.ToString().Trim();
    }

    private string BuildRelicChoiceStateContext(IReadOnlyList<RelicModel> options, string source, RunAnalysis analysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Relic choice state:");
        sb.AppendLine($"Source={source}; Options={options.Count}");

        foreach (var relic in options.Take(8))
        {
            sb.AppendLine($"- {DescribeRelicDetailed(relic, analysis)}");
        }

        return sb.ToString().Trim();
    }

    private string DescribeCardOption(CardModel card, RunAnalysis analysis)
    {
        var guide = _knowledgeBase?.FindCard(card.Title, analysis.CharacterId);
        var keywords = JoinNames(card.Keywords.Select(keyword => keyword.ToString()), 4);
        var tags = JoinNames(card.Tags.Select(tag => tag.ToString()), 4);
        var cost = card.EnergyCost.CostsX ? "X" : card.EnergyCost.GetAmountToSpend().ToString();
        var starCost = card.HasStarCostX ? "X" : card.CurrentStarCost >= 0 ? card.CurrentStarCost.ToString() : "none";
        var description = TrimText(card.GetDescriptionForPile(PileType.Deck), 140);
        var guideNote = guide is null ? string.Empty : TrimText(guide.DescriptionEn, 110);

        return $"{card.Title}; type={card.Type}; rarity={card.Rarity}; cost={cost}; starCost={starCost}; upgraded={card.IsUpgraded}; keywords={(string.IsNullOrWhiteSpace(keywords) ? "none" : keywords)}; tags={(string.IsNullOrWhiteSpace(tags) ? "none" : tags)}; text={description}{(string.IsNullOrWhiteSpace(guideNote) ? string.Empty : $"; guide={guideNote}")}";
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

        return $"relic; gold={entry.Cost}; {DescribeRelicDetailed(model, analysis)}";
    }

    private string DescribeRelicDetailed(RelicModel model, RunAnalysis analysis)
    {
        var guide = _knowledgeBase?.FindRelic(model.Title.GetFormattedText(), analysis.CharacterId);
        var relicText = TrimText(SafeFormat(() => model.DynamicDescription.GetFormattedText(), model.Title.GetFormattedText(), $"relic {model.Id.Entry}"), 140);
        var guideNote = guide is null ? string.Empty : TrimText(guide.DescriptionEn, 110);
        return $"relic {model.Title.GetFormattedText()}; rarity={model.Rarity}; text={relicText}{(string.IsNullOrWhiteSpace(guideNote) ? string.Empty : $"; guide={guideNote}")}";
    }

    private static string DescribePotionEntryDetailed(MerchantPotionEntry entry)
    {
        var model = entry.Model;
        if (model is null)
        {
            return $"potion; gold={entry.Cost}";
        }

        var text = TrimText(SafeFormat(() => model.DynamicDescription.GetFormattedText(), model.Title.GetFormattedText(), $"potion {model.Id.Entry}"), 140);
        return $"potion {model.Title.GetFormattedText()}; gold={entry.Cost}; usage={model.Usage}; target={model.TargetType}; text={text}";
    }

    private static string JoinNames(IEnumerable<string> values, int maxCount)
    {
        return string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().Take(maxCount));
    }

    private static CardModel? TryResolveIndexedCard(string key, string prefix, IReadOnlyList<CardModel> options)
    {
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rawIndex = key[prefix.Length..];
        return int.TryParse(rawIndex, out var indexValue) && indexValue >= 0 && indexValue < options.Count
            ? options[indexValue]
            : null;
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

    private static string GetEventOptionLabel(EventOption option)
    {
        var title = option.Title.GetFormattedText();
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        var description = SafeFormat(() => option.Description.GetFormattedText(), option.TextKey, $"event option {option.TextKey}");
        return string.IsNullOrWhiteSpace(description) ? option.TextKey : TrimText(description, 80);
    }

    private static string BuildEventOptionHint(EventOption option)
    {
        var pieces = new List<string>
        {
            $"proceed={option.IsProceed}"
        };

        var description = TrimText(SafeFormat(() => option.Description.GetFormattedText(), option.TextKey, $"event option {option.TextKey}"), 120);
        if (!string.IsNullOrWhiteSpace(description))
        {
            pieces.Add($"text={description}");
        }

        if (option.Relic is not null)
        {
            pieces.Add($"relic={option.Relic.Title.GetFormattedText()}({option.Relic.Rarity})");
        }

        if (option.WillKillPlayer is not null)
        {
            pieces.Add("lethal=true");
        }

        return string.Join("; ", pieces);
    }

    private static string DescribeSelectionKind(AiCardSelectionKind kind)
    {
        return kind switch
        {
            AiCardSelectionKind.DeckRemove => "deck removal",
            AiCardSelectionKind.DeckUpgrade => "deck upgrade",
            AiCardSelectionKind.DeckTransform => "deck transform",
            AiCardSelectionKind.DeckEnchant => "deck enchant",
            AiCardSelectionKind.HandDiscard => "hand discard",
            AiCardSelectionKind.HandUpgrade => "hand upgrade",
            AiCardSelectionKind.BundleChoice => "bundle choice",
            _ => kind.ToString()
        };
    }

    private static string BuildSharedContext(RunAnalysis analysis, string stateContext)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Character: {analysis.CharacterName}");
        sb.AppendLine($"Recommended Build: {analysis.RecommendedBuildName}");
        sb.AppendLine("Core Rules (MUST FOLLOW):");
        sb.AppendLine("- ENERGY DOES NOT CARRY OVER: at end of turn, all remaining energy is LOST. Next turn gives a fresh full energy bar. If you have 2 energy and playable cards, you MUST play them — ending the turn wastes that energy permanently.");
        sb.AppendLine("- HAND CARDS DO NOT CARRY OVER: at end of turn, all hand cards without the Retain keyword are DISCARDED to the discard pile. You will NOT have them next turn.");
        sb.AppendLine("- BLOCK DOES NOT CARRY OVER: at the start of YOUR next turn, all block is removed to 0 (unless Barricade or Calipers is in play). Block only absorbs damage during the current enemy attack phase.");
        sb.AppendLine("- DECK RECYCLING: when the draw pile is empty, the entire discard pile is shuffled into a new draw pile. Then cards are drawn from it.");
        sb.AppendLine("- CONSEQUENCE: Always spend all energy on useful plays. A basic Strike dealing 6 damage is infinitely better than wasting 1 energy. A basic Defend giving 5 block is infinitely better than floating 1 energy. NEVER end the turn with energy remaining if there are playable cards.");
        sb.AppendLine("- HP is a finite run-wide resource: preventing damage with block is almost always worth the energy, even if some block overflows. Block overflow is acceptable; HP loss is not.");
        sb.AppendLine("- Deck quality > deck size: a tight 15-25 card deck draws key cards consistently. Avoid adding mediocre cards.");
        sb.AppendLine("- Scaling wins long fights: Powers, Strength/Dexterity stacking, Focus, Poison, Doom — these grow every turn and dominate boss fights.");
        sb.AppendLine("- Frontloaded damage wins short fights: in Act 1 and hallway fights, immediate damage and block matter more than long-term scaling.");

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

        if (!string.IsNullOrWhiteSpace(analysis.CharacterCombatMechanicSummary))
        {
            sb.AppendLine("Character Combat Notes:");
            sb.AppendLine(analysis.CharacterCombatMechanicSummary);
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

    private static string FormatOrbDescription(OrbModel orb)
    {
        return SafeFormat(
            () =>
            {
                if (orb.HasSmartDescription && orb.IsMutable)
                {
                    var smartDescription = orb.SmartDescription;
                    smartDescription.Add("energyPrefix", orb.Owner.Character.CardPool.Title);
                    smartDescription.Add("Passive", orb.PassiveVal);
                    smartDescription.Add("Evoke", orb.EvokeVal);
                    return smartDescription.GetFormattedText();
                }

                return orb.Description.GetFormattedText();
            },
            $"Passive {orb.PassiveVal}; Evoke {orb.EvokeVal}",
            $"orb {orb.Id.Entry}");
    }

    private static string SafeFormat(Func<string> formatter, string fallback, string context)
    {
        try
        {
            return formatter();
        }
        catch (Exception ex)
        {
            Log.Warn($"[AiBot] Failed to format localized text for {context}: {ex.Message}");
            return fallback;
        }
    }

    private sealed record OrbPassiveEffects(int FrostBlock, int FrostCount, int LightningDamage, int LightningCount, int DarkGrowth, int DarkCount, int GlassDamage, int GlassCount);

    private static OrbPassiveEffects CalculateOrbPassiveEffects(PlayerCombatState? combat)
    {
        if (combat is null || combat.OrbQueue.Orbs.Count == 0)
        {
            return new OrbPassiveEffects(0, 0, 0, 0, 0, 0, 0, 0);
        }

        var frostBlock = 0;
        var frostCount = 0;
        var lightningDamage = 0;
        var lightningCount = 0;
        var darkGrowth = 0;
        var darkCount = 0;
        var glassDamage = 0;
        var glassCount = 0;

        foreach (var orb in combat.OrbQueue.Orbs)
        {
            try
            {
                if (orb is FrostOrb)
                {
                    frostBlock += (int)orb.PassiveVal;
                    frostCount++;
                }
                else if (orb is LightningOrb)
                {
                    lightningDamage += (int)orb.PassiveVal;
                    lightningCount++;
                }
                else if (orb is DarkOrb)
                {
                    darkGrowth += (int)orb.PassiveVal;
                    darkCount++;
                }
                else if (orb is GlassOrb)
                {
                    var passiveVal = (int)orb.PassiveVal;
                    if (passiveVal > 0)
                    {
                        glassDamage += passiveVal;
                        glassCount++;
                    }
                }
            }
            catch
            {
                // Orb value calculation can fail with certain modifiers; skip safely.
            }
        }

        return new OrbPassiveEffects(frostBlock, frostCount, lightningDamage, lightningCount, darkGrowth, darkCount, glassDamage, glassCount);
    }

    private static int EstimateIncomingDamage(IReadOnlyList<Creature> enemies, IReadOnlyList<Creature>? allies)
    {
        if (allies is null || allies.Count == 0)
        {
            return 0;
        }

        var total = 0;
        foreach (var enemy in enemies.Where(enemy => enemy.IsAlive && enemy.IsMonster && enemy.Monster?.NextMove is not null))
        {
            foreach (var intent in enemy.Monster!.NextMove!.Intents)
            {
                if (intent is AttackIntent attackIntent)
                {
                    try
                    {
                        total += attackIntent.GetTotalDamage(allies, enemy);
                    }
                    catch
                    {
                        // Damage calculation can fail if hooks or modifiers throw; skip safely.
                    }
                }
            }
        }

        return total;
    }

    private static int EstimateEnemyDamage(Creature enemy, IReadOnlyList<Creature>? allies)
    {
        if (allies is null || allies.Count == 0 || !enemy.IsAlive || !enemy.IsMonster || enemy.Monster?.NextMove is null)
        {
            return 0;
        }

        var total = 0;
        foreach (var intent in enemy.Monster!.NextMove!.Intents)
        {
            if (intent is AttackIntent attackIntent)
            {
                try
                {
                    total += attackIntent.GetTotalDamage(allies, enemy);
                }
                catch
                {
                }
            }
        }

        return total;
    }

    private static string DescribeEnemyIntent(Creature enemy)
    {
        if (!enemy.IsMonster || enemy.Monster?.NextMove is null)
        {
            return "unknown";
        }

        var intents = enemy.Monster.NextMove.Intents;
        if (intents.Count == 0)
        {
            return "unknown";
        }

        var labels = new List<string>();
        foreach (var intent in intents.Take(3))
        {
            var name = intent.GetType().Name.Replace("Intent", "");
            if (intent is AttackIntent attackIntent)
            {
                var repeats = attackIntent.Repeats;
                labels.Add(repeats > 1 ? $"Attack(x{repeats})" : "Attack");
            }
            else
            {
                labels.Add(name);
            }
        }

        return string.Join("+", labels);
    }

    private static string DescribeCreaturePowers(Creature creature, int maxCount)
    {
        if (creature.Powers.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", creature.Powers.Take(maxCount).Select(power =>
        {
            var amount = power.Amount != 0 ? $"({power.Amount})" : "";
            return $"{power.Title.GetFormattedText()}{amount}";
        }));
    }

    private static int EstimateAvailableDamage(IReadOnlyList<CombatActionOption> actionOptions, Creature target)
    {
        var totalDamage = 0;
        foreach (var option in actionOptions)
        {
            if (option.Target == target || option.Target is null)
            {
                var card = option.Card;
                if (card.Type == CardType.Attack)
                {
                    // Use base damage as rough estimate; actual damage includes modifiers
                    try
                    {
                        if (card.DynamicVars.ContainsKey("Damage"))
                        {
                            totalDamage += card.DynamicVars.Damage.IntValue;
                        }
                    }
                    catch
                    {
                        // Damage lookup can fail on some card types; skip safely.
                    }
                }
            }
        }

        return totalDamage;
    }

    private static List<CrystalSphereActionOption> BuildCrystalSphereActionCandidates(CrystalSphereMinigame minigame)
    {
        var actions = new List<CrystalSphereActionOption>();
        for (var x = 0; x < minigame.GridSize.X; x++)
        {
            for (var y = 0; y < minigame.GridSize.Y; y++)
            {
                if (!minigame.cells[x, y].IsHidden)
                {
                    continue;
                }

                var centerBias = 12 - Math.Abs(x - minigame.GridSize.X / 2) - Math.Abs(y - minigame.GridSize.Y / 2);
                actions.Add(CreateCrystalSphereActionOption(minigame, x, y, true, centerBias));
                actions.Add(CreateCrystalSphereActionOption(minigame, x, y, false, centerBias));
            }
        }

        return actions;
    }

    private static CrystalSphereActionOption CreateCrystalSphereActionOption(CrystalSphereMinigame minigame, int x, int y, bool useBigDivination, int centerBias)
    {
        var coverage = GetCrystalSphereCoverage(minigame, x, y, useBigDivination);
        var toolLabel = useBigDivination ? "Big" : "Small";
        return new CrystalSphereActionOption(
            x,
            y,
            useBigDivination,
            $"{toolLabel} divination at ({x},{y})",
            $"coverage={coverage}; centerBias={centerBias}; currentTool={minigame.CrystalSphereTool}",
            coverage * (useBigDivination ? 20 : 9) + centerBias);
    }

    private static int GetCrystalSphereCoverage(CrystalSphereMinigame minigame, int x, int y, bool useBigDivination)
    {
        var coverage = 0;
        var radius = useBigDivination ? 1 : 0;
        for (var offsetX = -radius; offsetX <= radius; offsetX++)
        {
            for (var offsetY = -radius; offsetY <= radius; offsetY++)
            {
                var targetX = x + offsetX;
                var targetY = y + offsetY;
                if (targetX < 0 || targetY < 0 || targetX >= minigame.GridSize.X || targetY >= minigame.GridSize.Y)
                {
                    continue;
                }

                if (minigame.cells[targetX, targetY].IsHidden)
                {
                    coverage++;
                }
            }
        }

        return coverage;
    }

    private static List<CombatActionOption> BuildCombatActionOptions(Player player, IReadOnlyList<CardModel> playableCards, IReadOnlyList<Creature> enemies, RunAnalysis analysis)
    {
        var options = new List<CombatActionOption>();
        foreach (var card in playableCards)
        {
            var targets = GetCardTargetCandidates(card.TargetType, player, enemies);
            if (RequiresExplicitCardTarget(card.TargetType))
            {
                foreach (var target in targets.Where(target => target is not null))
                {
                    options.Add(new CombatActionOption(
                        card,
                        target,
                        $"Play {card.Title} -> {DescribeCreature(target!)}",
                        $"type={card.Type}; cost={DescribeEnergyCost(card)}; targetType={card.TargetType}; card={TrimText(card.GetDescriptionForPile(PileType.Hand), 120)}; target={BuildTargetHint(target!, analysis)}",
                        $"play {card.Title} targeting {DescribeCreature(target!)}"));
                }

                continue;
            }

            options.Add(new CombatActionOption(
                card,
                null,
                $"Play {card.Title}",
                $"type={card.Type}; cost={DescribeEnergyCost(card)}; targetType={card.TargetType}; card={TrimText(card.GetDescriptionForPile(PileType.Hand), 120)}",
                $"play {card.Title}"));
        }

        return options;
    }

    private static List<PotionActionOption> BuildPotionActionOptions(Player player, IReadOnlyList<PotionModel> usablePotions, IReadOnlyList<Creature> enemies)
    {
        var options = new List<PotionActionOption>();
        foreach (var potion in usablePotions)
        {
            var targets = GetTargetCandidates(potion.TargetType, player, enemies);
            if (RequiresExplicitTarget(potion.TargetType))
            {
                foreach (var target in targets.Where(target => target is not null))
                {
                    options.Add(new PotionActionOption(
                        potion,
                        target,
                        $"Use {potion.Title} -> {DescribeCreature(target!)}",
                        $"usage={potion.Usage}; targetType={potion.TargetType}; potion={TrimText(potion.DynamicDescription.GetFormattedText(), 120)}; target={BuildTargetHint(target!)}",
                        $"use {potion.Title} on {DescribeCreature(target!)}"));
                }

                continue;
            }

            options.Add(new PotionActionOption(
                potion,
                targets.FirstOrDefault(),
                $"Use {potion.Title}",
                $"usage={potion.Usage}; targetType={potion.TargetType}; potion={TrimText(potion.DynamicDescription.GetFormattedText(), 120)}",
                $"use {potion.Title}"));
        }

        return options;
    }

    private static List<Creature?> GetCardTargetCandidates(TargetType targetType, Player player, IReadOnlyList<Creature> enemies)
    {
        return targetType switch
        {
            TargetType.AnyEnemy => enemies.Where(enemy => enemy.IsAlive).Cast<Creature?>().ToList(),
            TargetType.AnyAlly => GetAllyTargets(player).Cast<Creature?>().ToList(),
            _ => new List<Creature?> { null }
        };
    }

    private static List<Creature?> GetTargetCandidates(TargetType targetType, Player player, IReadOnlyList<Creature> enemies)
    {
        return targetType switch
        {
            TargetType.AnyEnemy => enemies.Where(enemy => enemy.IsAlive).Cast<Creature?>().ToList(),
            TargetType.AnyPlayer => new List<Creature?> { player.Creature },
            TargetType.AnyAlly => GetAllyTargets(player).Cast<Creature?>().ToList(),
            TargetType.Self => new List<Creature?> { player.Creature },
            _ => new List<Creature?> { null }
        };
    }

    private static List<Creature> GetAllyTargets(Player player)
    {
        var allies = new List<Creature> { player.Creature };
        if (player.PlayerCombatState is not null)
        {
            allies.AddRange(player.PlayerCombatState.Pets.Where(pet => pet.IsAlive));
        }

        return allies
            .Where(ally => ally.IsAlive)
            .Distinct()
            .ToList();
    }

    private static bool RequiresExplicitCardTarget(TargetType targetType)
    {
        return targetType is TargetType.AnyEnemy or TargetType.AnyAlly;
    }

    private static bool RequiresExplicitTarget(TargetType targetType)
    {
        return targetType is TargetType.AnyEnemy or TargetType.AnyAlly or TargetType.AnyPlayer or TargetType.Self;
    }

    private static string DescribeCreature(Creature creature)
    {
        var combatId = creature.CombatId?.ToString() ?? "?";
        return $"{creature.Name}#{combatId} ({creature.CurrentHp}/{creature.MaxHp} hp, {creature.Block} block)";
    }

    private static string BuildTargetHint(Creature creature, RunAnalysis? analysis = null)
    {
        var intent = creature.IsMonster && creature.Monster?.NextMove is not null
            ? string.Join(" | ", creature.Monster.NextMove.Intents.Select(intent => intent.GetType().Name).Take(3))
            : "none";
        var guideHint = analysis is null ? string.Empty : $"; strategic={TrimText(analysis.EnemySummary, 120)}";
        return $"hp={creature.CurrentHp}/{creature.MaxHp}; block={creature.Block}; intent={intent}{guideHint}";
    }

    private static string DescribeEnergyCost(CardModel card)
    {
        return card.EnergyCost.CostsX ? "X" : card.EnergyCost.GetAmountToSpend().ToString();
    }

    private sealed class ChatCompletionResponse
    {
        public List<Choice>? Choices { get; set; }
    }

    private sealed record CrystalSphereActionOption(int X, int Y, bool UseBigDivination, string Label, string Hint, int Score);

    private sealed record CombatActionOption(CardModel Card, Creature? Target, string Label, string Hint, string Summary);

    private sealed record PotionActionOption(PotionModel Potion, Creature? Target, string Label, string Hint, string Summary);

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
