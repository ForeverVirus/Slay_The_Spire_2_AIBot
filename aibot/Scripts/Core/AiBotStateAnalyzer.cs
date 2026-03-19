using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
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
        var potionSummary = _knowledgeBase.BuildPotionSummary(potionNames);
        var knowledgeDigest = _knowledgeBase.BuildKnowledgeDigest(characterId, deckEntries, relicNames, potionNames);
        var runProgressSummary = BuildRunProgressSummary(player, runState);
        var playerStateSummary = BuildPlayerStateSummary(player);
        var combatSummary = BuildCombatSummary(player);
        var enemySummary = BuildEnemySummary(player);
        var recentHistorySummary = BuildRecentHistorySummary(runState);

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
            enemySummary,
            recentHistorySummary);
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
            build.SummaryEn,
            build.StrategyEn,
            build.TipsEn
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
        var orbs = combat.OrbQueue.Orbs.Count == 0 ? "none" : string.Join(", ", combat.OrbQueue.Orbs.Select(orb => orb.Title.GetFormattedText()));
        return $"Energy={combat.Energy}/{combat.MaxEnergy}; Stars={combat.Stars}; Hand=[{hand}]; Draw={draw}; Discard={discard}; Exhaust={exhaust}; Pets={pets}; Orbs={orbs}";
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
}
