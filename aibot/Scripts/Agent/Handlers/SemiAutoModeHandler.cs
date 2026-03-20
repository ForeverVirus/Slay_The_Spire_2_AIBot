using MegaCrit.Sts2.Core.Logging;
using aibot.Scripts.Core;

namespace aibot.Scripts.Agent.Handlers;

public sealed class SemiAutoModeHandler : IAgentModeHandler
{
    private readonly AiBotRuntime _runtime;
    private readonly string _activationReason;

    public SemiAutoModeHandler(AiBotRuntime runtime, string activationReason)
    {
        _runtime = runtime;
        _activationReason = activationReason;
    }

    public AgentMode Mode => AgentMode.SemiAuto;

    public Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _runtime.DeactivateLegacyFullAuto();
        Log.Info($"[AiBot.Agent] SemiAuto mode entered. Reason={_activationReason}");
        return Task.CompletedTask;
    }

    public Task OnDeactivateAsync()
    {
        return Task.CompletedTask;
    }

    public Task OnTickAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task<string> OnUserInputAsync(string input, CancellationToken cancellationToken)
    {
        return Task.FromResult("半自动模式骨架已接入。后续阶段会补充聊天窗口、意图解析和 Skill 执行链路。");
    }

    public void Dispose()
    {
    }
}
