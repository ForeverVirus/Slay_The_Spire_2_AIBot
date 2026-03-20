using MegaCrit.Sts2.Core.Logging;
using aibot.Scripts.Core;

namespace aibot.Scripts.Agent.Handlers;

public sealed class QnAModeHandler : IAgentModeHandler
{
    private readonly AiBotRuntime _runtime;
    private readonly string _activationReason;

    public QnAModeHandler(AiBotRuntime runtime, string activationReason)
    {
        _runtime = runtime;
        _activationReason = activationReason;
    }

    public AgentMode Mode => AgentMode.QnA;

    public Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _runtime.DeactivateLegacyFullAuto();
        Log.Info($"[AiBot.Agent] QnA mode entered. Reason={_activationReason}");
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
        return Task.FromResult("问答模式骨架已接入。后续阶段会补充知识检索、会话管理和游戏边界过滤。");
    }

    public void Dispose()
    {
    }
}
