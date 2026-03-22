using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using aibot.Scripts.Core;

namespace aibot.Scripts.Agent.Skills;

public sealed class UsePotionSkill : RuntimeBackedSkillBase
{
    public UsePotionSkill(AiBotRuntime runtime) : base(runtime)
    {
    }

    public override string Name => "use_potion";

    public override string Description => "Use a combat potion for the local player.";

    public override SkillCategory Category => SkillCategory.Combat;

    public override bool CanExecute()
    {
        var runState = RunManager.Instance.DebugOnlyGetState();
        var player = LocalContext.GetMe(runState);
        return CombatActionGuard.CanTakeLocalTurnActions(player) && (player?.Potions.Any(IsUsable) ?? false);
    }

    public override async Task<SkillExecutionResult> ExecuteAsync(AgentSkillParameters? parameters, CancellationToken cancellationToken)
    {
        var runState = RunManager.Instance.DebugOnlyGetState();
        var player = LocalContext.GetMe(runState);
        if (!CombatActionGuard.CanTakeLocalTurnActions(player))
        {
            return new SkillExecutionResult(false, "当前不在可由本地玩家继续使用药水的战斗阶段。");
        }

        var localPlayer = player!;
        var potions = localPlayer.Potions.Where(IsUsable).ToList();
        if (potions.Count == 0)
        {
            return new SkillExecutionResult(false, "当前没有可使用的药水。");
        }

        var potion = !string.IsNullOrWhiteSpace(parameters?.PotionName)
            ? potions.FirstOrDefault(candidate => candidate.Id.Entry.Contains(parameters.PotionName, StringComparison.OrdinalIgnoreCase))
            : null;
        potion ??= potions[0];

        var enemies = localPlayer.Creature.CombatState?.HittableEnemies?.Where(enemy => enemy.IsAlive).ToList() ?? new List<Creature>();
        var target = ChooseTarget(potion, localPlayer.Creature, enemies, parameters?.TargetName);
        potion.EnqueueManualUse(target);

        var actionExecutor = RunManager.Instance.ActionExecutor;
        if (actionExecutor is not null)
        {
            await actionExecutor.FinishedExecutingActions().WaitAsync(cancellationToken);
        }

        return new SkillExecutionResult(true, $"已使用药水：{potion.Id.Entry}", target is null ? null : $"目标：{target.Name}");
    }

    private static bool IsUsable(PotionModel potion)
    {
        return !potion.IsQueued
            && !potion.HasBeenRemovedFromState
            && potion.PassesCustomUsabilityCheck
            && potion.Usage is PotionUsage.CombatOnly or PotionUsage.AnyTime;
    }

    private static Creature? ChooseTarget(PotionModel potion, Creature playerCreature, IReadOnlyList<Creature> enemies, string? targetName)
    {
        if (!string.IsNullOrWhiteSpace(targetName))
        {
            var explicitTarget = enemies.FirstOrDefault(enemy => enemy.Name.Contains(targetName, StringComparison.OrdinalIgnoreCase));
            if (explicitTarget is not null)
            {
                return explicitTarget;
            }
        }

        return potion.TargetType switch
        {
            TargetType.AnyEnemy => enemies.OrderBy(enemy => enemy.CurrentHp).FirstOrDefault(),
            TargetType.AnyPlayer or TargetType.Self => playerCreature,
            _ => null
        };
    }
}
