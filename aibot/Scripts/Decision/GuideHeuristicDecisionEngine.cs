using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Rewards;
using aibot.Scripts.Knowledge;

namespace aibot.Scripts.Decision;

public sealed class GuideHeuristicDecisionEngine : IAiDecisionEngine
{
    private readonly GuideKnowledgeBase _knowledgeBase;

    public GuideHeuristicDecisionEngine(GuideKnowledgeBase knowledgeBase)
    {
        _knowledgeBase = knowledgeBase;
    }

    public Task<PotionDecision> ChoosePotionUseAsync(Player player, IReadOnlyList<PotionModel> usablePotions, IReadOnlyList<CardModel> playableCards, IReadOnlyList<Creature> enemies, RunAnalysis analysis, CancellationToken cancellationToken)
    {
        var currentHp = player.Creature.CurrentHp;
        var maxHp = player.Creature.MaxHp;
        var healthRatio = maxHp <= 0 ? 1f : currentHp / (float)maxHp;

        var best = usablePotions
            .Select(potion => new
            {
                Potion = potion,
                Score = ScorePotionUse(potion, playableCards, enemies, healthRatio, analysis)
            })
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();

        if (best is null || best.Score < 100)
        {
            return Task.FromResult(new PotionDecision(null, null, "Local heuristic kept potions for later.", new DecisionTrace("Consumable", "Local", "Hold consumables", "Local heuristic did not find a potion use with enough value in the current combat state.")));
        }

        var target = ChoosePotionTarget(best.Potion, player, enemies);
        if (RequiresPotionTarget(best.Potion) && target is null)
        {
            return Task.FromResult(new PotionDecision(null, null, $"Skipped {best.Potion.Title} because no valid target was available.", new DecisionTrace("Consumable", "Local", $"Skip {best.Potion.Title}", $"Local heuristic considered {best.Potion.Title} but found no valid target for {best.Potion.TargetType}.")));
        }

        return Task.FromResult(new PotionDecision(best.Potion, target, $"Local heuristic selected potion {best.Potion.Title}.", new DecisionTrace("Consumable", "Local", $"Use {best.Potion.Title}", $"Local heuristic chose {best.Potion.Title} with score {best.Score} based on HP {currentHp}/{maxHp}, {usablePotions.Count} usable potions, and {playableCards.Count} playable cards.")));
    }

    public Task<CombatDecision> ChooseCombatActionAsync(Player player, IReadOnlyList<CardModel> playableCards, IReadOnlyList<Creature> enemies, RunAnalysis analysis, CancellationToken cancellationToken)
    {
        var chosen = playableCards
            .OrderByDescending(card => ScoreCombatCard(card, analysis))
            .FirstOrDefault();

        if (chosen is null)
        {
            return Task.FromResult(new CombatDecision(null, null, true, "No playable cards available.", new DecisionTrace("Combat", "Local", "End turn", "No playable cards were available, so the local heuristic ended the turn.")));
        }

        Creature? target = null;
        if (chosen.TargetType == TargetType.AnyEnemy)
        {
            target = enemies.Where(e => e.IsAlive).OrderBy(e => e.CurrentHp).FirstOrDefault();
        }

        var targetText = target is null ? "no target required" : $"targeting {target.Name}";
        return Task.FromResult(new CombatDecision(chosen, target, false, $"Local heuristic selected {chosen.Title}.", new DecisionTrace("Combat", "Local", $"Play {chosen.Title}", $"Local heuristic chose {chosen.Title} because it had the best combat score for the current hand, with {targetText}.")));
    }

    public Task<CardRewardDecision> ChooseCardRewardAsync(IReadOnlyList<CardModel> options, RunAnalysis analysis, CancellationToken cancellationToken)
    {
        var chosen = options
            .OrderByDescending(card => ScoreRewardCard(card, analysis))
            .FirstOrDefault();

        return Task.FromResult(new CardRewardDecision(chosen, chosen is null ? "No card options." : $"Preferred card: {chosen.Title}", new DecisionTrace("Card Reward", "Local", chosen is null ? "Skip card choice" : $"Take {chosen.Title}", chosen is null ? "No selectable card rewards were available." : $"Local heuristic preferred {chosen.Title} because it best matched the current build plan {analysis.RecommendedBuildName}.")));
    }

    public Task<RewardDecision> ChooseRewardAsync(IReadOnlyList<NRewardButton> options, bool hasOpenPotionSlots, RunAnalysis analysis, CancellationToken cancellationToken)
    {
        var filtered = options
            .Where(button => button.IsEnabled)
            .Where(button => hasOpenPotionSlots || button.Reward is not PotionReward)
            .OrderByDescending(button => ScoreRewardButton(button, analysis))
            .ToList();

        var chosen = filtered.FirstOrDefault();
        return Task.FromResult(new RewardDecision(chosen, chosen is null ? "No enabled reward buttons." : $"Selected reward type {chosen.Reward?.GetType().Name ?? "unknown"}.", new DecisionTrace("Rewards", "Local", chosen is null ? "No reward selected" : $"Take {chosen.Reward?.GetType().Name ?? "reward"}", chosen is null ? "No enabled rewards were available." : $"Local heuristic prioritized {chosen.Reward?.GetType().Name ?? "reward"} based on the current reward ordering rules.")));
    }

    public Task<ShopDecision> ChooseShopPurchaseAsync(IReadOnlyList<MerchantEntry> options, int currentGold, bool hasOpenPotionSlots, RunAnalysis analysis, CancellationToken cancellationToken)
    {
        var best = options
            .Where(option => option.IsStocked && option.EnoughGold)
            .Where(option => hasOpenPotionSlots || option is not MerchantPotionEntry)
            .Select(option => new
            {
                Entry = option,
                Score = ScoreShopEntry(option, analysis, currentGold)
            })
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();

        if (best is null || best.Score < 115)
        {
            return Task.FromResult(new ShopDecision(null, "Local heuristic chose to save gold.", new DecisionTrace("Shop", "Local", "Leave shop", $"Local heuristic did not find a shop purchase above threshold with {currentGold} gold and potion slot open={hasOpenPotionSlots}.")));
        }

        return Task.FromResult(new ShopDecision(best.Entry, $"Local heuristic selected {DescribeShopEntry(best.Entry)}.", new DecisionTrace("Shop", "Local", $"Buy {DescribeShopEntry(best.Entry)}", $"Local heuristic chose {DescribeShopEntry(best.Entry)} with score {best.Score} while holding {currentGold} gold.")));
    }

    public Task<MapDecision> ChooseMapPointAsync(IReadOnlyList<MapPoint> options, int currentHp, int maxHp, int gold, RunAnalysis analysis, CancellationToken cancellationToken)
    {
        var chosen = options
            .OrderByDescending(point => ScoreMapPoint(point, currentHp, maxHp, gold))
            .ThenBy(point => point.coord.col)
            .FirstOrDefault();

        return Task.FromResult(new MapDecision(chosen, chosen is null ? "No map options." : $"Selected map point type {chosen.PointType}.", new DecisionTrace("Map", "Local", chosen is null ? "No map move" : $"Go to {chosen.PointType} ({chosen.coord.row},{chosen.coord.col})", chosen is null ? "No travelable map nodes were available." : $"Local heuristic selected {chosen.PointType} at ({chosen.coord.row},{chosen.coord.col}) using current HP {currentHp}/{maxHp} and gold {gold}.")));
    }

    private int ScoreCombatCard(CardModel card, RunAnalysis analysis)
    {
        var score = 0;
        switch (card.Type)
        {
            case CardType.Power:
                score += 80;
                break;
            case CardType.Attack:
                score += 55;
                break;
            case CardType.Skill:
                score += 35;
                break;
            default:
                score -= 50;
                break;
        }

        if (card.EnergyCost.GetAmountToSpend() == 0)
        {
            score += 20;
        }

        if (card.EnergyCost.CostsX)
        {
            score += 5;
        }

        score += ScoreKnowledgeMatch(card.Title, analysis);
        score += ScoreKnowledgeMatch(card.Id.Entry, analysis);
        return score;
    }

    private int ScoreRewardCard(CardModel card, RunAnalysis analysis)
    {
        var score = 0;
        switch (card.Type)
        {
            case CardType.Power:
                score += 30;
                break;
            case CardType.Attack:
                score += 18;
                break;
            case CardType.Skill:
                score += 12;
                break;
        }

        score += ScoreKnowledgeMatch(card.Title, analysis) * 2;
        score += ScoreKnowledgeMatch(card.Id.Entry, analysis) * 2;
        return score;
    }

    private int ScoreRewardButton(NRewardButton button, RunAnalysis analysis)
    {
        var reward = button.Reward;
        if (reward is null)
        {
            return -100;
        }

        var typeName = reward.GetType().Name;
        var score = typeName switch
        {
            nameof(CardReward) => 100,
            nameof(RelicReward) => 90,
            nameof(GoldReward) => 70,
            nameof(PotionReward) => 50,
            _ => 40
        };

        return score;
    }

    private int ScorePotionUse(PotionModel potion, IReadOnlyList<CardModel> playableCards, IReadOnlyList<Creature> enemies, float healthRatio, RunAnalysis analysis)
    {
        if (potion.IsQueued || potion.HasBeenRemovedFromState || !potion.PassesCustomUsabilityCheck)
        {
            return int.MinValue;
        }

        if (potion.Usage is PotionUsage.Automatic or PotionUsage.None)
        {
            return int.MinValue;
        }

        var normalized = GuideKnowledgeBase.Normalize($"{potion.Title}");
        var enemyCount = enemies.Count(enemy => enemy.IsAlive);
        var lowestEnemyHp = enemies.Where(enemy => enemy.IsAlive).Select(enemy => enemy.CurrentHp).DefaultIfEmpty(int.MaxValue).Min();
        var score = potion.Usage == PotionUsage.AnyTime ? 65 : 90;

        if (ContainsAny(normalized, "energy", "swift", "speed", "strength", "dexterity", "focus", "regen", "ghost", "gigantification", "bronze", "courage", "serum", "tonic", "fortifier"))
        {
            score += 45;
        }

        if (ContainsAny(normalized, "attack", "skill", "power", "colorless", "cunning", "memories", "bottled", "chaos"))
        {
            score += playableCards.Count <= 1 ? 50 : 25;
        }

        if (ContainsAny(normalized, "fire", "poison", "weak", "vulnerable", "shackling", "doom", "explosive", "ashwater", "rock"))
        {
            score += 40;
            if (lowestEnemyHp <= 25)
            {
                score += 30;
            }

            if (enemyCount >= 2)
            {
                score += 15;
            }
        }

        if (ContainsAny(normalized, "blood", "fruit", "entropic"))
        {
            score += healthRatio < 0.55f ? 55 : -25;
        }

        score += ScoreKnowledgeMatch($"{potion.Title}", analysis);
        return score;
    }

    private int ScoreShopEntry(MerchantEntry entry, RunAnalysis analysis, int currentGold)
    {
        var score = entry switch
        {
            MerchantRelicEntry relicEntry when relicEntry.Model is not null => 180 + ScoreKnowledgeMatch($"{relicEntry.Model.Title}", analysis) * 2 + (relicEntry.Model.Rarity switch
            {
                RelicRarity.Shop => 35,
                RelicRarity.Rare => 25,
                RelicRarity.Uncommon => 15,
                RelicRarity.Ancient => 20,
                _ => 5
            }),
            MerchantCardEntry cardEntry when cardEntry.CreationResult?.Card is not null => 110 + ScoreRewardCard(cardEntry.CreationResult.Card, analysis) * 2 + (cardEntry.IsOnSale ? 35 : 0),
            MerchantPotionEntry potionEntry when potionEntry.Model is not null => 85 + potionEntry.Model.Rarity switch
            {
                PotionRarity.Rare => 20,
                PotionRarity.Uncommon => 10,
                _ => 0
            },
            MerchantCardRemovalEntry removalEntry when !removalEntry.Used => analysis.DeckCardNames.Count >= 14 ? 150 : 95,
            _ => 0
        };

        score += Math.Max(0, 40 - entry.Cost / 5);
        if (entry.Cost >= currentGold)
        {
            score -= 20;
        }

        return score;
    }

    private int ScoreMapPoint(MapPoint point, int currentHp, int maxHp, int gold)
    {
        var healthRatio = maxHp <= 0 ? 1f : currentHp / (float)maxHp;
        return point.PointType switch
        {
            MapPointType.RestSite when healthRatio < 0.45f => 150,
            MapPointType.Shop when gold >= 180 => 130,
            MapPointType.Treasure => 120,
            MapPointType.Monster => 100,
            MapPointType.Elite when healthRatio > 0.70f => 115,
            MapPointType.Elite => 70,
            MapPointType.Unknown => 95,
            MapPointType.RestSite => 80,
            MapPointType.Shop => 75,
            MapPointType.Boss => 200,
            _ => 50
        };
    }

    private int ScoreKnowledgeMatch(string cardName, RunAnalysis analysis)
    {
        var normalized = GuideKnowledgeBase.Normalize(cardName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return 0;
        }

        if (analysis.DeckCardNames.Any(name => GuideKnowledgeBase.Normalize(name) == normalized))
        {
            return 12;
        }

        foreach (var build in _knowledgeBase.GetBuildsForCharacter(analysis.CharacterId))
        {
            if (_knowledgeBase.MentionsCard(normalized, build))
            {
                return build.NameEn == analysis.RecommendedBuildName ? 25 : 10;
            }
        }

        return 0;
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

    private static bool RequiresPotionTarget(PotionModel potion)
    {
        return potion.TargetType is TargetType.AnyEnemy or TargetType.AnyAlly or TargetType.AnyPlayer or TargetType.Self or TargetType.TargetedNoCreature;
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

    private static bool ContainsAny(string source, params string[] fragments)
    {
        return fragments.Any(fragment => source.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }
}
