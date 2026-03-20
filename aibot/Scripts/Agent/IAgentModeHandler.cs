namespace aibot.Scripts.Agent;

public interface IAgentModeHandler : IDisposable
{
    AgentMode Mode { get; }

    Task OnActivateAsync(CancellationToken cancellationToken);

    Task OnDeactivateAsync();

    Task OnTickAsync(CancellationToken cancellationToken);

    Task<string> OnUserInputAsync(string input, CancellationToken cancellationToken);
}
