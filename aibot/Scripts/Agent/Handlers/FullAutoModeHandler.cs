using aibot.Scripts.Core;

namespace aibot.Scripts.Agent.Handlers;

public sealed class FullAutoModeHandler : IAgentModeHandler
{
    private readonly AiBotRuntime _runtime;
    private readonly string _activationReason;

    public FullAutoModeHandler(AiBotRuntime runtime, string activationReason)
    {
        _runtime = runtime;
        _activationReason = activationReason;
    }

    public AgentMode Mode => AgentMode.FullAuto;

    public Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _runtime.ActivateLegacyFullAuto(_activationReason);
        return Task.CompletedTask;
    }

    public Task OnDeactivateAsync()
    {
        _runtime.DeactivateLegacyFullAuto();
        return Task.CompletedTask;
    }

    public Task OnTickAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task<string> OnUserInputAsync(string input, CancellationToken cancellationToken)
    {
        return Task.FromResult("当前处于全自动模式，Agent 会直接接管游戏流程。后续阶段会补充模式切换 UI 与确认机制。");
    }

    public void Dispose()
    {
    }
}
