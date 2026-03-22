using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Runs;

namespace aibot.Scripts.Agent;

public static class CombatActionGuard
{
    public static bool CanTakeLocalTurnActions(Player? player)
    {
        if (player?.Creature?.CombatState is null)
        {
            return false;
        }

        if (!CombatManager.Instance.IsInProgress || !CombatManager.Instance.IsPlayPhase)
        {
            return false;
        }

        if (CombatManager.Instance.PlayerActionsDisabled)
        {
            return false;
        }

        return !CombatManager.Instance.IsPlayerReadyToEndTurn(player);
    }

    public static bool QueueEndTurn(Player? player)
    {
        if (!CanTakeLocalTurnActions(player) || RunManager.Instance.ActionQueueSynchronizer is null)
        {
            return false;
        }

        var localPlayer = player!;
        var combatState = localPlayer.Creature?.CombatState;
        if (combatState is null)
        {
            return false;
        }

        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(
            new EndPlayerTurnAction(localPlayer, combatState.RoundNumber));
        return true;
    }
}
