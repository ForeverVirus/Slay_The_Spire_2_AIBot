using MegaCrit.Sts2.Core.Logging;
using aibot.Scripts.Core;

namespace aibot.Scripts.Agent.Handlers;

public sealed class AssistModeHandler : IAgentModeHandler
{
    private readonly AiBotRuntime _runtime;
    private readonly string _activationReason;

    public AssistModeHandler(AiBotRuntime runtime, string activationReason)
    {
        _runtime = runtime;
        _activationReason = activationReason;
    }

    public AgentMode Mode => AgentMode.Assist;

    public Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _runtime.DeactivateLegacyFullAuto();
        Log.Info($"[AiBot.Agent] Assist mode entered. Reason={_activationReason}");
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
        return Task.FromResult("辅助模式骨架已接入。后续阶段会补充“推荐”标签覆盖层和推荐理由展示。");
    }

    public void Dispose()
    {
    }
}
