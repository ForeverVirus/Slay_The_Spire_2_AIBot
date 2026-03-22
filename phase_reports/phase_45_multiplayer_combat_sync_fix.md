# Phase 45 - Multiplayer Combat Synchronization Fix

## Background

After enabling multiplayer run activation, combat still showed a critical desync symptom:

- client A could decide to end turn,
- client B could also decide to end turn,
- each client only saw its own local "ready to end turn" state,
- both sides then became stuck waiting for the other player.

This indicated that at least one mod execution path was mutating local combat state without using the game's multiplayer action synchronization layer.

## Investigation Findings

### 1. Root cause: `end_turn` used a local-only call path

The mod's combat end-turn flow was using:

- `PlayerCmd.EndTurn(player, false)`

That call updates local combat readiness state directly.

However, the game's own multiplayer-safe end-turn button uses:

- `RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(...))`

`EndPlayerTurnAction` serializes to a network action (`NetEndPlayerTurnAction`) and is the correct multiplayer synchronization path.

### 2. Audit result for other combat actions

Other key combat execution paths were already multiplayer-aware:

- card play:
  - mod path uses `CardModel.TryManualPlay(...)`
  - game path enqueues `PlayCardAction`
  - multiplayer-safe
- potion use:
  - mod path uses `PotionModel.EnqueueManualUse(...)`
  - game path enqueues `UsePotionAction`
  - multiplayer-safe

So the main confirmed desync bug was specifically the end-turn execution path.

### 3. Secondary issue: post-end-turn over-decision

Even after a local player had already voted to end turn, several mod entry points could still continue generating combat advice or attempting local combat actions while waiting for teammates.

That could lead to repeated or invalid local decisions during multiplayer waiting states.

## Changes

### 1. Added a multiplayer-safe combat action guard

Added:

- `aibot/Scripts/Agent/CombatActionGuard.cs`

Responsibilities:

- detect whether the local player can still legally take combat actions
- block combat decision/execution after the local player has already ended turn
- enqueue multiplayer-safe end-turn actions through the action queue synchronizer

### 2. Replaced direct local end-turn calls

Updated:

- `aibot/Scripts/Agent/Skills/EndTurnSkill.cs`
- `aibot/Scripts/Core/AiBotRuntime.cs`

The mod no longer calls `PlayerCmd.EndTurn(...)` directly from its own combat automation paths.

It now uses:

- `CombatActionGuard.QueueEndTurn(...)`

which routes through:

- `EndPlayerTurnAction`
- `ActionQueueSynchronizer`
- game-native multiplayer synchronization

### 3. Stopped combat logic once the local player is already committed

Updated:

- `aibot/Scripts/Agent/CombatAdvisor.cs`
- `aibot/Scripts/Agent/Skills/PlayCardSkill.cs`
- `aibot/Scripts/Agent/Skills/UsePotionSkill.cs`
- `aibot/Scripts/Ui/AgentRecommendOverlay.cs`

These entry points now stop generating combat actions or combat recommendations when:

- the local player is no longer allowed to act,
- the local player has already voted to end turn,
- player actions are disabled during the waiting state.

## Validation

`dotnet build aibot\aibot.csproj -c Release /p:CopyModAfterBuild=false` produced:

- successful code compilation to `aibot/.godot/mono/temp/bin/Release/aibot.dll`

The final copy step did not complete during this pass because:

- `SlayTheSpire2.exe` was locking `mods/aibot/aibot.dll`

## Expected Outcome

- multiplayer `end_turn` should now propagate through the game's synchronized action system instead of staying local-only
- once the local player has ended turn, the mod should stop trying to keep acting or recommending combat actions on that client
- the previous "both players ended locally but neither client sees the other as ready" deadlock should be resolved for mod-issued end-turn actions
