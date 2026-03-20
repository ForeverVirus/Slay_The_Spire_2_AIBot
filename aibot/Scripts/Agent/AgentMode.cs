namespace aibot.Scripts.Agent;

public enum AgentMode
{
    FullAuto,
    SemiAuto,
    Assist,
    QnA
}

public sealed record AgentModeChangeRequest(
    AgentMode CurrentMode,
    AgentMode RequestedMode,
    string Reason,
    bool RequiresConfirmation);
