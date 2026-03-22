using System.Text;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using aibot.Scripts.Config;
using aibot.Scripts.Core;
using aibot.Scripts.Decision;
using aibot.Scripts.Localization;

namespace aibot.Scripts.Agent;

public static class CombatAdvisor
{
    public static bool IsTurnExecutionRequest(string input)
    {
        var normalized = Normalize(input);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var asksForHelp = ContainsAny(normalized, "帮我", "替我", "代我", "forme");
        var mentionsTurn = ContainsAny(normalized, "回合", "这回合", "这一回合", "这个回合", "thisturn", "turn");
        var mentionsPlay = ContainsAny(normalized, "出牌", "打", "操作", "play", "handle", "take");

        return (asksForHelp && mentionsTurn && mentionsPlay)
            || ContainsAny(normalized,
                "帮我打这一回合",
                "帮我打这一个回合",
                "帮我打这个回合",
                "帮我打一回合",
                "帮我把这回合打完",
                "帮我把这一回合打完",
                "帮我把这个回合打完",
                "替我打这一回合",
                "替我出牌",
                "代我出牌",
                "playthisturn",
                "playthisturnforme",
                "handlethisturn",
                "takethisturn");
    }

    public static bool IsCombatAdviceQuestion(string input)
    {
        var normalized = Normalize(input);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var asksPlayQuestion = ContainsAny(normalized,
            "出什么",
            "打什么",
            "怎么出牌",
            "怎么打",
            "该出什么",
            "应该出什么",
            "whatshouldiplay",
            "bestplay",
            "whatshouldido");
        var asksTiming = ContainsAny(normalized,
            "现在",
            "接下来",
            "下一步",
            "下一张",
            "这回合",
            "这一回合",
            "当前",
            "now",
            "next",
            "nextplay",
            "nextstep",
            "thisturn");

        return (asksPlayQuestion && asksTiming)
            || ContainsAny(normalized,
                "我该出什么",
                "我应该出什么",
                "我该打什么",
                "现在应该出什么",
                "现在我应该出什么",
                "接下来我应该出什么",
                "接下来我该出什么",
                "下一步我应该出什么",
                "下一步该出什么",
                "这回合应该出什么",
                "这一回合应该出什么",
                "现在打什么",
                "接下来打什么",
                "这回合打什么",
                "现在怎么出牌",
                "接下来怎么出牌",
                "我现在该出什么",
                "我接下来该出什么",
                "我现在该做什么",
                "whatshouldiplaynow",
                "whatshouldiplaynext",
                "whatsnextplay",
                "whatshouldidonow",
                "whatshouldidonext",
                "whatisthebestplay",
                "bestplaynow");
    }

    public static async Task<CombatDecision?> GetCombatDecisionAsync(AiBotRuntime runtime, CancellationToken cancellationToken)
    {
        var player = GetPlayer();
        if (!CombatActionGuard.CanTakeLocalTurnActions(player) || runtime.DecisionEngine is null)
        {
            return null;
        }

        var localPlayer = player!;
        var combatState = localPlayer.Creature?.CombatState;
        if (combatState is null)
        {
            return null;
        }

        var hand = PileType.Hand.GetPile(localPlayer).Cards.ToList();
        var playable = hand.Where(CanPlay).ToList();
        var enemies = combatState.HittableEnemies?.Where(enemy => enemy.IsAlive).ToList() ?? new List<Creature>();
        var analysis = runtime.GetCurrentAnalysis();
        return await runtime.DecisionEngine.ChooseCombatActionAsync(localPlayer, playable, enemies, analysis, cancellationToken);
    }

    public static async Task<string> ExecuteCombatDecisionAsync(AiBotRuntime runtime, CombatDecision decision, CancellationToken cancellationToken)
    {
        if (decision.EndTurn)
        {
            var skill = AgentCore.Instance.Registry.FindSkillByName("end_turn");
            if (skill is null)
            {
                return AiBotText.Pick(runtime.Config, "未找到结束回合技能。", "Could not find the end-turn skill.");
            }

            var result = await skill.ExecuteAsync(new Agent.Skills.AgentSkillParameters(), cancellationToken);
            return FormatExecutionResult(runtime.Config, result, decision);
        }

        if (decision.Card is null)
        {
            return AiBotText.Pick(runtime.Config, "当前没有可执行的出牌决策。", "No playable combat action is available right now.");
        }

        var playSkill = AgentCore.Instance.Registry.FindSkillByName("play_card");
        if (playSkill is null)
        {
            return AiBotText.Pick(runtime.Config, "未找到出牌技能。", "Could not find the play-card skill.");
        }

        var parameters = new Agent.Skills.AgentSkillParameters(
            CardName: decision.Card.Title,
            TargetName: decision.Target?.Name);
        var execution = await playSkill.ExecuteAsync(parameters, cancellationToken);
        return FormatExecutionResult(runtime.Config, execution, decision);
    }

    public static async Task<string> PlayWholeTurnAsync(AiBotRuntime runtime, CancellationToken cancellationToken)
    {
        var summaries = new List<string>();
        var seenStates = new HashSet<string>(StringComparer.Ordinal);

        for (var step = 0; step < 20; step++)
        {
            var stateSignature = BuildBoardStateSignature();
            if (!string.IsNullOrEmpty(stateSignature) && !seenStates.Add(stateSignature))
            {
                summaries.Add(AiBotText.Pick(runtime.Config,
                    "检测到局面没有继续变化，我先停止继续代打，避免重复执行相同操作。",
                    "I stopped because the board state was no longer changing, to avoid repeating the same action."));
                break;
            }

            var decision = await GetCombatDecisionAsync(runtime, cancellationToken);
            if (decision is null)
            {
                return summaries.Count > 0
                    ? string.Join("\n", summaries)
                    : AiBotText.Pick(runtime.Config, "当前不在可代打的战斗阶段。", "The run is not in a playable combat state right now.");
            }

            summaries.Add(await ExecuteCombatDecisionAsync(runtime, decision, cancellationToken));
            if (decision.EndTurn)
            {
                break;
            }
        }

        return summaries.Count > 0
            ? string.Join("\n", summaries)
            : AiBotText.Pick(runtime.Config, "这一回合没有可自动执行的动作。", "There was no automatic action to take this turn.");
    }

    public static string FormatRecommendation(AiBotConfig config, CombatDecision decision)
    {
        var rationale = BuildDisplayedReason(decision);
        if (decision.EndTurn)
        {
            return AiBotText.Pick(config,
                $"建议这回合直接结束。\n理由：{rationale}",
                $"I recommend ending the turn.\nReason: {rationale}");
        }

        if (decision.Card is null)
        {
            return AiBotText.Pick(config,
                "当前没有明确的可出牌建议。",
                "There is no clear playable recommendation right now.");
        }

        var targetText = decision.Target is null
            ? string.Empty
            : AiBotText.Pick(config, $"，目标是 {decision.Target.Name}", $", targeting {decision.Target.Name}");

        return AiBotText.Pick(config,
            $"建议现在打出 {decision.Card.Title}{targetText}。\n理由：{rationale}",
            $"I recommend playing {decision.Card.Title}{targetText}.\nReason: {rationale}");
    }

    private static string BuildDisplayedReason(CombatDecision decision)
    {
        var detailed = decision.Trace?.Details?.Trim();
        if (!string.IsNullOrWhiteSpace(detailed))
        {
            return detailed;
        }

        return string.IsNullOrWhiteSpace(decision.Reason)
            ? "No detailed rationale was returned."
            : decision.Reason.Trim();
    }

    private static string FormatExecutionResult(AiBotConfig config, Agent.Skills.SkillExecutionResult result, CombatDecision? decision = null)
    {
        if (!result.Success)
        {
            return AiBotText.Pick(config,
                "这次自动执行没有成功，请再试一次，或者给我一个更具体的指令。",
                "The automatic action did not succeed. Please try again or give a more specific command.");
        }

        if (decision?.EndTurn == true)
        {
            return AiBotText.Pick(config, "我已经帮你结束这一回合。", "I ended the turn for you.");
        }

        if (decision?.Card is not null)
        {
            var targetText = decision.Target is null
                ? string.Empty
                : AiBotText.Pick(config, $"，目标是 {decision.Target.Name}", $", targeting {decision.Target.Name}");
            return AiBotText.Pick(config,
                $"我已经按当前判断替你打出 {decision.Card.Title}{targetText}。",
                $"I played {decision.Card.Title}{targetText} for you based on the current board.");
        }

        return AiBotText.Pick(config, "操作已执行。", "The action has been executed.");
    }

    private static Player? GetPlayer()
    {
        var runState = RunManager.Instance.DebugOnlyGetState();
        return LocalContext.GetMe(runState);
    }

    private static bool CanPlay(CardModel card)
    {
        AbstractModel? preventer;
        UnplayableReason reason;
        return card.CanPlay(out reason, out preventer);
    }

    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(input.Length);
        foreach (var ch in input.Trim().ToLowerInvariant())
        {
            if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch))
            {
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static bool ContainsAny(string input, params string[] values)
    {
        return values.Any(value => input.Contains(Normalize(value), StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildBoardStateSignature()
    {
        var player = GetPlayer();
        var combatState = player?.Creature?.CombatState;
        var combat = player?.PlayerCombatState;
        if (combatState is null)
        {
            return string.Empty;
        }

        var hand = PileType.Hand.GetPile(player!).Cards
            .Select(card => card.Title)
            .OrderBy(title => title, StringComparer.Ordinal)
            .ToArray();
        var enemies = combatState.HittableEnemies?
            .Where(enemy => enemy is not null)
            .Select(enemy => $"{enemy.Name}:{enemy.CurrentHp}:{enemy.Block}")
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();

        return string.Join("|", new[]
        {
            $"energy={combat?.Energy ?? -1}",
            $"handCount={hand.Length}",
            $"hand={string.Join(",", hand)}",
            $"enemies={string.Join(",", enemies)}"
        });
    }
}
