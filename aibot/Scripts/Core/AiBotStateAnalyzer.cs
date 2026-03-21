using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using aibot.Scripts.Decision;
using aibot.Scripts.Knowledge;

namespace aibot.Scripts.Core;

public sealed class AiBotStateAnalyzer
{
    private readonly GuideKnowledgeBase _knowledgeBase;

    public AiBotStateAnalyzer(GuideKnowledgeBase knowledgeBase)
    {
        _knowledgeBase = knowledgeBase;
    }

    public RunAnalysis Analyze(RunState runState)
    {
        var player = runState.Players.First();
        return Analyze(player, runState);
    }

    public RunAnalysis Analyze(Player player)
    {
        return Analyze(player, player.RunState as RunState);
    }

    private RunAnalysis Analyze(Player player, RunState? runState)
    {
        var deckCardNames = player.Deck.Cards.Select(card => card.Title).Distinct().ToList();
        var deckEntries = player.Deck.Cards
            .GroupBy(card => card.Title)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => group.Count() > 1 ? $"{group.Key} x{group.Count()}" : group.Key)
            .ToList();
        var relicNames = player.Relics.Select(relic => relic.Title.GetFormattedText()).Distinct().ToList();
        var potionNames = player.Potions.Select(potion => potion.Title.GetFormattedText()).Distinct().ToList();
        var characterId = ResolveCharacterId(player.Character);
        var characterName = player.Character.Title.GetFormattedText();
        var builds = _knowledgeBase.GetBuildsForCharacter(characterId).ToList();
        var selectedBuild = builds
            .OrderByDescending(build => ScoreBuild(deckCardNames, relicNames, build))
            .FirstOrDefault();

        var characterBrief = _knowledgeBase.BuildCharacterBrief(characterId);
        var buildSummary = BuildPreferredBuildSummary(selectedBuild, characterBrief);
        var deckSummary = _knowledgeBase.BuildDeckSummary(deckEntries, characterId);
        var relicSummary = _knowledgeBase.BuildRelicSummary(relicNames, characterId);
        var potionSummary = _knowledgeBase.BuildPotionSummary(potionNames, characterId: characterId);
        var knowledgeDigest = _knowledgeBase.BuildKnowledgeDigest(characterId, deckEntries, relicNames, potionNames);
        var runProgressSummary = BuildRunProgressSummary(player, runState);
        var playerStateSummary = BuildPlayerStateSummary(player);
        var combatSummary = BuildCombatSummary(player);
        var characterCombatMechanicSummary = BuildCharacterCombatMechanicSummary(player, characterId);
        var enemySummary = BuildEnemySummary(player);
        var recentHistorySummary = BuildRecentHistorySummary(runState);
        var strategicNeedsSummary = BuildStrategicNeedsSummary(player, runState);
        var deckStructureSummary = BuildDeckStructureSummary(player);
        var removalCandidateSummary = BuildRemovalCandidateSummary(player);

        return new RunAnalysis(
            characterId,
            characterName,
            selectedBuild?.NameEn ?? "Generalist",
            buildSummary,
            deckCardNames,
            relicNames,
            potionNames,
            characterBrief,
            deckSummary,
            relicSummary,
            potionSummary,
            knowledgeDigest,
            runProgressSummary,
            playerStateSummary,
            combatSummary,
            characterCombatMechanicSummary,
            enemySummary,
            recentHistorySummary,
            strategicNeedsSummary,
            deckStructureSummary,
            removalCandidateSummary);
    }

    private int ResolveCharacterId(CharacterModel character)
    {
        var normalizedEntry = GuideKnowledgeBase.Normalize(character.Id.Entry);
        var normalizedTitle = GuideKnowledgeBase.Normalize(character.Title.GetFormattedText());
        var match = _knowledgeBase.Characters.FirstOrDefault(entry =>
            GuideKnowledgeBase.Normalize(entry.Slug) == normalizedEntry ||
            GuideKnowledgeBase.Normalize(entry.NameEn) == normalizedTitle ||
            GuideKnowledgeBase.Normalize(entry.NameZh) == normalizedTitle);

        return match?.Id ?? 0;
    }

    private int ScoreBuild(IReadOnlyList<string> deckCardNames, IReadOnlyList<string> relicNames, BuildGuideEntry build)
    {
        var score = 0;
        foreach (var cardName in deckCardNames)
        {
            if (_knowledgeBase.MentionsCard(GuideKnowledgeBase.Normalize(cardName), build))
            {
                score += 8;
            }
        }

        foreach (var relicName in relicNames)
        {
            if (_knowledgeBase.MentionsCard(GuideKnowledgeBase.Normalize(relicName), build))
            {
                score += 5;
            }
        }

        if (string.Equals(build.Tier, "S", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        return score;
    }

    private static string BuildPreferredBuildSummary(BuildGuideEntry? build, string characterBrief)
    {
        if (build is null)
        {
            return characterBrief;
        }

        var summary = string.Join(" ", new[]
        {
            KnowledgeTextFormatter.FormatPlainText(build.SummaryEn),
            KnowledgeTextFormatter.FormatPlainText(build.StrategyEn),
            KnowledgeTextFormatter.FormatPlainText(build.TipsEn)
        }.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();

        return string.IsNullOrWhiteSpace(summary) ? characterBrief : summary;
    }

    private static string BuildRunProgressSummary(Player player, RunState? runState)
    {
        if (runState is null)
        {
            return string.Empty;
        }

        var currentPoint = runState.CurrentMapPoint;
        var coord = runState.CurrentMapCoord;
        var roomType = runState.CurrentRoom?.RoomType.ToString() ?? "Unknown";
        return $"Ascension={runState.AscensionLevel}; Act={runState.CurrentActIndex + 1}; Floor={runState.ActFloor}; TotalFloor={runState.TotalFloor}; Room={roomType}; MapNode={(currentPoint is null ? "none" : currentPoint.PointType.ToString())}; Coord={(coord.HasValue ? $"({coord.Value.row},{coord.Value.col})" : "none")}; Gold={player.Gold}; VisitedNodes={runState.VisitedMapCoords.Count}";
    }

    private static string BuildPlayerStateSummary(Player player)
    {
        var creature = player.Creature;
        var powers = SummarizePowers(creature.Powers, 6);
        return $"Hp={creature.CurrentHp}/{creature.MaxHp}; Block={creature.Block}; Gold={player.Gold}; Potions={string.Join(", ", player.Potions.Select(potion => potion.Title.GetFormattedText()))}; OpenPotionSlot={player.HasOpenPotionSlots}; DeckSize={player.Deck.Cards.Count}; Relics={player.Relics.Count}; Powers={powers}";
    }

    private static string BuildCombatSummary(Player player)
    {
        var combat = player.PlayerCombatState;
        if (combat is null || !CombatManager.Instance.IsInProgress)
        {
            return string.Empty;
        }

        var hand = string.Join(", ", combat.Hand.Cards.Take(10).Select(card => card.Title));
        var draw = combat.DrawPile.Cards.Count;
        var discard = combat.DiscardPile.Cards.Count;
        var exhaust = combat.ExhaustPile.Cards.Count;
        var pets = combat.Pets.Count == 0 ? "none" : string.Join(", ", combat.Pets.Select(pet => $"{pet.Name} {pet.CurrentHp}/{pet.MaxHp}"));
        var orbs = combat.OrbQueue.Orbs.Count == 0
            ? "none"
            : string.Join(" | ", combat.OrbQueue.Orbs.Select((orb, index) =>
                $"{index + 1}:{orb.Title.GetFormattedText()} passive={orb.PassiveVal} evoke={orb.EvokeVal} text={TrimText(FormatOrbDescription(orb), 90)}"));
        return $"Energy={combat.Energy}/{combat.MaxEnergy}; Stars={combat.Stars}; Hand=[{hand}]; Draw={draw}; Discard={discard}; Exhaust={exhaust}; Pets={pets}; OrbSlots={combat.OrbQueue.Orbs.Count}/{combat.OrbQueue.Capacity}; Orbs={orbs}";
    }

    private static string FormatOrbDescription(OrbModel orb)
    {
        try
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
        }
        catch (Exception ex)
        {
            Log.Warn($"[AiBot] Failed to format orb description for {orb.Id.Entry}: {ex.Message}");
            try
            {
                return orb.Description.GetFormattedText();
            }
            catch
            {
                return $"Passive {orb.PassiveVal}; Evoke {orb.EvokeVal}";
            }
        }
    }

    private static string BuildEnemySummary(Player player)
    {
        var combatState = player.Creature.CombatState;
        if (combatState is null || !CombatManager.Instance.IsInProgress)
        {
            return string.Empty;
        }

        var enemies = combatState.HittableEnemies
            .Where(enemy => enemy.IsAlive)
            .Select(enemy =>
            {
                var intents = enemy.IsMonster && enemy.Monster?.NextMove is not null
                    ? string.Join(" | ", enemy.Monster.NextMove.Intents.Select(intent =>
                    {
                        var tip = intent.GetHoverTip(combatState.Allies, enemy);
                        var title = string.IsNullOrWhiteSpace(tip.Title) ? intent.GetType().Name : tip.Title;
                        return $"{title}: {TrimText(tip.Description, 90)}";
                    }))
                    : "none";
                var powers = SummarizePowers(enemy.Powers, 4);
                return $"{enemy.Name} Hp={enemy.CurrentHp}/{enemy.MaxHp} Block={enemy.Block} Intent=[{intents}] Powers={powers}";
            })
            .ToList();

        return string.Join("\n", enemies);
    }

    private static string BuildCharacterCombatMechanicSummary(Player player, int characterId)
    {
        var combat = player.PlayerCombatState;
        if (combat is null || !CombatManager.Instance.IsInProgress)
        {
            return string.Empty;
        }

        var characterSlug = GuideKnowledgeBase.Normalize(player.Character.Id.Entry);
        if (characterId == 2 || characterSlug.Contains("silent", StringComparison.Ordinal))
        {
            return BuildSilentCombatMechanicSummary(player);
        }

        if (characterId == 1 || characterSlug.Contains("ironclad", StringComparison.Ordinal))
        {
            return BuildIroncladCombatMechanicSummary(player);
        }

        if (characterId == 5 || characterSlug.Contains("necrobinder", StringComparison.Ordinal) || characterSlug.Contains("necro", StringComparison.Ordinal))
        {
            return BuildNecrobinderCombatMechanicSummary(player);
        }

        if (characterId == 4 || characterSlug.Contains("regent", StringComparison.Ordinal))
        {
            return BuildRegentCombatMechanicSummary(player);
        }

        return string.Empty;
    }

    private static string BuildSilentCombatMechanicSummary(Player player)
    {
        var combat = player.PlayerCombatState!;
        var handCards = combat.Hand.Cards.ToList();
        var allCombatCards = EnumerateCombatCards(combat).ToList();
        var shivsInHand = handCards.Count(IsShivCard);
        var shivsInCombat = allCombatCards.Count(IsShivCard);
        var zeroCostAttacksInHand = handCards.Count(card => card.Type == CardType.Attack && !card.EnergyCost.CostsX && card.EnergyCost.GetAmountToSpend() == 0);
        var retainInHand = handCards.Count(card => card.Keywords.Contains(CardKeyword.Retain));
        var accuracy = GetPowerAmount(player.Creature, "accuracy", "精准");
        var afterImage = GetPowerAmount(player.Creature, "after image", "afterimage", "残影");
        var envenom = GetPowerAmount(player.Creature, "envenom", "淬毒");
        var poisonAmounts = player.Creature.CombatState?.HittableEnemies
            .Where(enemy => enemy.IsAlive)
            .Select(enemy => GetPowerAmount(enemy, "poison", "中毒", "毒"))
            .Where(amount => amount > 0)
            .ToList() ?? new List<int>();

        var totalPoison = poisonAmounts.Sum();
        var maxPoison = poisonAmounts.DefaultIfEmpty(0).Max();

        return $"Silent mechanics: ShivsInHand={shivsInHand}; TotalShivsInCombat={shivsInCombat}; ZeroCostAttacksInHand={zeroCostAttacksInHand}; RetainInHand={retainInHand}; Accuracy={accuracy}; AfterImage={afterImage}; Envenom={envenom}; PoisonOnEnemies={totalPoison} total (max {maxPoison}). Tactical note: if Accuracy, After Image, or Envenom is active, many-card turns and cheap multi-hit attacks gain value fast. If poison is already stacked, defense, draw, and safe stall lines are often strong when they preserve lethal next turns.";
    }

    private static string BuildIroncladCombatMechanicSummary(Player player)
    {
        var combat = player.PlayerCombatState!;
        var handCards = combat.Hand.Cards.ToList();
        var exhaustCardsInHand = handCards.Count(card => card.Keywords.Contains(CardKeyword.Exhaust));
        var statusOrCurseInHand = handCards.Count(card => card.Type is CardType.Status or CardType.Curse);
        var selfDamageCardsInHand = handCards.Count(IsSelfDamageCard);
        var feelNoPain = GetPowerAmount(player.Creature, "feel no pain", "无痛");
        var darkEmbrace = GetPowerAmount(player.Creature, "dark embrace", "黑暗拥抱");
        var rupture = GetPowerAmount(player.Creature, "rupture", "裂伤");
        var strength = GetPowerAmount(player.Creature, "strength", "力量");

        return $"Ironclad mechanics: ExhaustCardsInHand={exhaustCardsInHand}; ExhaustPile={combat.ExhaustPile.Cards.Count}; StatusOrCurseInHand={statusOrCurseInHand}; SelfDamageCardsInHand={selfDamageCardsInHand}; FeelNoPain={feelNoPain}; DarkEmbrace={darkEmbrace}; Rupture={rupture}; Strength={strength}. Tactical note: when Feel No Pain or Dark Embrace is active, exhausting cards can create immediate block or draw instead of being pure cost. When Rupture or other self-damage payoffs are active, HP-loss cards may be worthwhile if the turn swings tempo or scales future damage enough.";
    }

    private static string BuildNecrobinderCombatMechanicSummary(Player player)
    {
        var combat = player.PlayerCombatState!;
        var allCombatCards = EnumerateCombatCards(combat).ToList();
        var soulCardsInHand = combat.Hand.Cards.Count(IsSoulCard);
        var soulCardsInCombat = allCombatCards.Count(IsSoulCard);
        var summons = combat.Pets.ToList();
        var osty = summons.FirstOrDefault(pet => GuideKnowledgeBase.Normalize(pet.Name).Contains("osty", StringComparison.Ordinal));
        var summonSummary = summons.Count == 0
            ? "none"
            : string.Join(", ", summons.Select(pet => $"{pet.Name} {pet.CurrentHp}/{pet.MaxHp}"));

        return $"Necrobinder mechanics: SoulCardsInHand={soulCardsInHand}; TotalSoulCardsInCombat={soulCardsInCombat}; Summons={summons.Count}; Osty={(osty is null ? "not present" : $"{osty.CurrentHp}/{osty.MaxHp}")}; ActiveAllies={summonSummary}. Tactical note: soul cards and summon payoffs should be judged as engine pieces, not isolated card text. If key summons are alive, protect or leverage them; if soul density is high, setup, cycling, and synergy cards can be stronger than raw immediate damage.";
    }

    private static string BuildRegentCombatMechanicSummary(Player player)
    {
        var combat = player.PlayerCombatState!;
        var handCards = combat.Hand.Cards.ToList();
        var starCardsInHand = handCards.Count(card => card.HasStarCostX || card.CurrentStarCost >= 0);
        var affordableStarCards = handCards.Count(card =>
            card.HasStarCostX ? combat.Stars > 0 : card.CurrentStarCost >= 0 && card.CurrentStarCost <= combat.Stars);
        var starXCardsInHand = handCards.Count(card => card.HasStarCostX);
        var allies = combat.Pets.ToList();
        var allySummary = allies.Count == 0
            ? "none"
            : string.Join(", ", allies.Select(pet => $"{pet.Name} {pet.CurrentHp}/{pet.MaxHp}"));

        return $"Regent mechanics: Stars={combat.Stars}; StarCardsInHand={starCardsInHand}; AffordableStarCardsInHand={affordableStarCards}; StarXCardsInHand={starXCardsInHand}; Allies={allies.Count}; ActiveAllies={allySummary}. Tactical note: stars are a separate combat resource, so evaluate whether spending them now creates a major swing or whether a stronger star payoff is likely soon. Ally/support cards become better when they protect existing companions or convert stars into board control.";
    }

    private static IEnumerable<CardModel> EnumerateCombatCards(PlayerCombatState combat)
    {
        foreach (var card in combat.Hand.Cards)
        {
            yield return card;
        }

        foreach (var card in combat.DrawPile.Cards)
        {
            yield return card;
        }

        foreach (var card in combat.DiscardPile.Cards)
        {
            yield return card;
        }

        foreach (var card in combat.ExhaustPile.Cards)
        {
            yield return card;
        }
    }

    private static bool IsShivCard(CardModel card)
    {
        var normalized = NormalizeCardIdentity(card);
        return normalized.Contains("shiv", StringComparison.Ordinal) || normalized.Contains("飞刀", StringComparison.Ordinal);
    }

    private static bool IsSoulCard(CardModel card)
    {
        var normalized = NormalizeCardIdentity(card);
        return normalized.Contains("soul", StringComparison.Ordinal) || normalized.Contains("灵魂", StringComparison.Ordinal);
    }

    private static bool IsSelfDamageCard(CardModel card)
    {
        var description = GuideKnowledgeBase.Normalize(card.GetDescriptionForPile(PileType.Deck));
        return (description.Contains("lose", StringComparison.Ordinal) && description.Contains("hp", StringComparison.Ordinal)) ||
               (description.Contains("失去", StringComparison.Ordinal) && description.Contains("生命", StringComparison.Ordinal));
    }

    private static int GetPowerAmount(Creature creature, params string[] nameTokens)
    {
        return creature.Powers
            .Where(power => nameTokens.Any(token => GuideKnowledgeBase.Normalize(power.Title.GetFormattedText()).Contains(GuideKnowledgeBase.Normalize(token), StringComparison.Ordinal)))
            .Select(power => Math.Max(power.DisplayAmount, 0))
            .DefaultIfEmpty(0)
            .Max();
    }

    private static string NormalizeCardIdentity(CardModel card)
    {
        return GuideKnowledgeBase.Normalize($"{card.Id.Entry} {card.Title}");
    }

    private static string BuildRecentHistorySummary(RunState? runState)
    {
        if (runState?.MapPointHistory is null)
        {
            return string.Empty;
        }

        var recent = runState.MapPointHistory
            .SelectMany(entries => entries)
            .TakeLast(5)
            .Select(entry =>
            {
                var rooms = entry.Rooms.Count == 0
                    ? entry.MapPointType.ToString()
                    : string.Join(", ", entry.Rooms.Select(room => $"{room.RoomType}(turns={room.TurnsTaken})"));
                return $"{entry.MapPointType}: {rooms}";
            })
            .ToList();

        return string.Join("\n", recent);
    }

    private static string BuildStrategicNeedsSummary(Player player, RunState? runState)
    {
        var cards = player.Deck.Cards.ToList();
        if (cards.Count == 0)
        {
            return string.Empty;
        }

        var size = cards.Count;
        var attacks = cards.Count(card => card.Type == CardType.Attack);
        var skills = cards.Count(card => card.Type == CardType.Skill);
        var powers = cards.Count(card => card.Type == CardType.Power);
        var upgraded = cards.Count(card => card.IsUpgraded);
        var removableWeak = cards.Count(card => card.IsRemovable && (card.IsBasicStrikeOrDefend || card.Type is CardType.Curse or CardType.Status));
        var blockCards = cards.Count(card => card.GainsBlock || card.Tags.Contains(CardTag.Defend));
        var zeroCost = cards.Count(card => !card.EnergyCost.CostsX && card.EnergyCost.GetAmountToSpend() == 0);
        var highCost = cards.Count(card => card.EnergyCost.CostsX || card.EnergyCost.GetAmountToSpend() >= 2);
        var healthRatio = player.Creature.MaxHp <= 0 ? 1f : player.Creature.CurrentHp / (float)player.Creature.MaxHp;
        var needs = new List<string>();

        if (healthRatio < 0.45f)
        {
            needs.Add($"Survival is urgent: hp is only {player.Creature.CurrentHp}/{player.Creature.MaxHp}, so favor healing, safer routes, and immediate defense.");
        }
        else if (healthRatio < 0.65f)
        {
            needs.Add($"Survival matters: hp is {player.Creature.CurrentHp}/{player.Creature.MaxHp}, so balance greed with safety.");
        }

        if (blockCards < Math.Max(3, size / 4))
        {
            needs.Add("Deck likely needs more reliable defense or mitigation because block density is low.");
        }

        if (attacks < Math.Max(4, size / 5))
        {
            needs.Add("Deck may need better frontloaded damage to finish fights before taking too much chip damage.");
        }

        if (powers == 0 && size >= 14)
        {
            needs.Add("Deck appears light on scaling; strong long-fight payoffs or snowball relics would help.");
        }

        if (highCost > zeroCost + 3)
        {
            needs.Add("Energy curve looks clunky; prioritize cheaper cards, energy support, or draw/filtering.");
        }

        if (removableWeak >= 4)
        {
            needs.Add("Card removal is valuable because the deck still has several weak removable cards.");
        }

        if (upgraded < Math.Max(2, size / 5))
        {
            needs.Add("Upgrades still have high value because a relatively small share of the deck is upgraded.");
        }

        if (skills > attacks + 4)
        {
            needs.Add("Deck is skill-heavy; be careful not to become too slow or too passive without enough damage output.");
        }

        if (runState is not null)
        {
            needs.Add($"Run context: act={runState.CurrentActIndex + 1}, floor={runState.TotalFloor}, currentRoom={runState.CurrentRoom?.RoomType}.");
        }

        return string.Join("\n", needs.Distinct().Take(8));
    }

    private static string SummarizePowers(IReadOnlyList<PowerModel> powers, int maxCount)
    {
        if (powers.Count == 0)
        {
            return "none";
        }

        return string.Join(", ", powers.Take(maxCount).Select(power => $"{power.Title.GetFormattedText()}({power.DisplayAmount})"));
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

    private static string BuildDeckStructureSummary(Player player)
    {
        var cards = player.Deck.Cards.ToList();
        if (cards.Count == 0)
        {
            return string.Empty;
        }

        var attackCount = cards.Count(card => card.Type == CardType.Attack);
        var skillCount = cards.Count(card => card.Type == CardType.Skill);
        var powerCount = cards.Count(card => card.Type == CardType.Power);
        var statusCount = cards.Count(card => card.Type == CardType.Status);
        var curseCount = cards.Count(card => card.Type == CardType.Curse);
        var upgradedCount = cards.Count(card => card.IsUpgraded);
        var basicCount = cards.Count(card => card.Rarity == CardRarity.Basic);
        var strikeCount = cards.Count(card => card.Tags.Contains(CardTag.Strike));
        var defendCount = cards.Count(card => card.Tags.Contains(CardTag.Defend));
        var zeroCostCount = cards.Count(card => !card.EnergyCost.CostsX && card.EnergyCost.GetAmountToSpend() == 0);
        var highCostCount = cards.Count(card => card.EnergyCost.CostsX || card.EnergyCost.GetAmountToSpend() >= 2);
        var retainCount = cards.Count(card => card.Keywords.Contains(CardKeyword.Retain));
        var exhaustCount = cards.Count(card => card.Keywords.Contains(CardKeyword.Exhaust));
        var etherealCount = cards.Count(card => card.Keywords.Contains(CardKeyword.Ethereal));
        var unplayableCount = cards.Count(card => card.Keywords.Contains(CardKeyword.Unplayable));
        var blockCount = cards.Count(card => card.GainsBlock);
        var avgCost = cards.Where(card => !card.EnergyCost.CostsX).DefaultIfEmpty().Average(card => card is null ? 0 : card.EnergyCost.GetAmountToSpend());

        return $"DeckComposition: size={cards.Count}; attack={attackCount}; skill={skillCount}; power={powerCount}; status={statusCount}; curse={curseCount}; upgraded={upgradedCount}; basic={basicCount}; strikes={strikeCount}; defends={defendCount}; zeroCost={zeroCostCount}; highCost={highCostCount}; blockCards={blockCount}; retain={retainCount}; exhaust={exhaustCount}; ethereal={etherealCount}; unplayable={unplayableCount}; avgCost={avgCost:F2}";
    }

    private static string BuildRemovalCandidateSummary(Player player)
    {
        var candidates = player.Deck.Cards
            .Where(card => card.IsRemovable)
            .Select(card => new
            {
                Card = card,
                Score = ScoreRemovalCandidate(card),
                Reason = ExplainRemovalCandidate(card)
            })
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Card.EnergyCost.CostsX ? 99 : entry.Card.EnergyCost.GetAmountToSpend())
            .ThenBy(entry => entry.Card.Title)
            .Take(5)
            .ToList();

        if (candidates.Count == 0)
        {
            return string.Empty;
        }

        return string.Join("\n", candidates.Select(entry =>
            $"- {entry.Card.Title}: removeScore={entry.Score}; rarity={entry.Card.Rarity}; type={entry.Card.Type}; cost={(entry.Card.EnergyCost.CostsX ? "X" : entry.Card.EnergyCost.GetAmountToSpend())}; upgraded={entry.Card.IsUpgraded}; reason={entry.Reason}; text={TrimText(entry.Card.GetDescriptionForPile(PileType.Deck), 110)}"));
    }

    private static int ScoreRemovalCandidate(CardModel card)
    {
        if (!card.IsRemovable)
        {
            return int.MinValue;
        }

        var score = 0;
        switch (card.Type)
        {
            case CardType.Curse:
                score += 200;
                break;
            case CardType.Status:
                score += 170;
                break;
            case CardType.Skill when card.Tags.Contains(CardTag.Defend) && card.Rarity == CardRarity.Basic:
                score += 95;
                break;
            case CardType.Attack when card.Tags.Contains(CardTag.Strike) && card.Rarity == CardRarity.Basic:
                score += 100;
                break;
        }

        if (card.Keywords.Contains(CardKeyword.Unplayable))
        {
            score += 120;
        }

        if (card.Keywords.Contains(CardKeyword.Ethereal))
        {
            score += 20;
        }

        if (!card.IsUpgraded)
        {
            score += 10;
        }

        if (card.EnergyCost.CostsX)
        {
            score += 15;
        }
        else if (card.EnergyCost.GetAmountToSpend() >= 2)
        {
            score += 25;
        }

        if (card.Rarity == CardRarity.Basic)
        {
            score += 20;
        }

        if (card.Rarity == CardRarity.Rare)
        {
            score -= 40;
        }

        if (card.Type == CardType.Power)
        {
            score -= 20;
        }

        return score;
    }

    private static string ExplainRemovalCandidate(CardModel card)
    {
        if (card.Type == CardType.Curse)
        {
            return "curse card usually lowers deck quality";
        }

        if (card.Type == CardType.Status)
        {
            return "status card usually adds low-value draws";
        }

        if (card.Keywords.Contains(CardKeyword.Unplayable))
        {
            return "unplayable card clogs draws";
        }

        if (card.IsBasicStrikeOrDefend)
        {
            return "basic strike/defend tends to be the weakest long-term card";
        }

        if (card.EnergyCost.CostsX || card.EnergyCost.GetAmountToSpend() >= 2)
        {
            return "expensive card may be clunky if payoff is low";
        }

        return "candidate for trimming if stronger synergy cards are available";
    }
}
