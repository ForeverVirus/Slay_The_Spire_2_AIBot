using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Runs;
using aibot.Scripts.Core;

namespace aibot.Scripts.Agent.Skills;

public sealed class EndTurnSkill : RuntimeBackedSkillBase
{
    public EndTurnSkill(AiBotRuntime runtime) : base(runtime)
    {
    }

    public override string Name => "end_turn";

    public override string Description => "End the current combat turn.";

    public override SkillCategory Category => SkillCategory.Combat;

    public override bool CanExecute()
    {
        var runState = RunManager.Instance.DebugOnlyGetState();
        return CombatActionGuard.CanTakeLocalTurnActions(LocalContext.GetMe(runState));
    }

    public override async Task<SkillExecutionResult> ExecuteAsync(AgentSkillParameters? parameters, CancellationToken cancellationToken)
    {
        var runState = RunManager.Instance.DebugOnlyGetState();
        var player = LocalContext.GetMe(runState);
        if (!CombatActionGuard.QueueEndTurn(player))
        {
            return new SkillExecutionResult(false, "当前无法结束本地玩家的回合。");
        }

        var actionExecutor = RunManager.Instance.ActionExecutor;
        if (actionExecutor is not null)
        {
            await actionExecutor.FinishedExecutingActions().WaitAsync(cancellationToken);
        }

        return new SkillExecutionResult(true, "已发起结束当前回合请求。");
    }
}
