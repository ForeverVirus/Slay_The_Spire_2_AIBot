# Phase 44 - Multiplayer Activation Entry Support

## Background

The mod was functioning correctly in singleplayer, but multiplayer runs appeared unsupported.

After checking both the mod code and the decompiled STS2 runtime, the main architectural finding was:

1. most gameplay decisions in the mod already operate on the local player by using `LocalContext.GetMe(...)`,
2. most executable actions already go through game-native UI or command paths that are multiplayer-aware,
3. but the Harmony activation entry only listened for `NGame.StartNewSingleplayerRun`.

That meant a newly started multiplayer run would not automatically trigger the mod's takeover/activation flow, even though the rest of the runtime was largely compatible with local-player multiplayer usage.

## Changes

### 1. Added multiplayer new-run activation hook

- Updated `aibot/Scripts/Harmony/AiBotPatches.cs`
- Added a postfix patch for:
  - `NGame.StartNewMultiplayerRun`
- The new patch now forwards the returned `Task<RunState>` into:
  - `AiBotRuntime.Instance.NotifyNewRunTask(__result)`

This brings multiplayer new-run activation in line with the existing singleplayer flow.

## Validation

Build succeeded with:

```powershell
dotnet build aibot\aibot.csproj -c Release /p:CopyModAfterBuild=false
```

Result:

- `0 warnings`
- `0 errors`
- `aibot.dll` built successfully
- mod copy step completed successfully

## Technical Conclusion

The current codebase is not fundamentally "singleplayer-only".

What is already multiplayer-friendly:

- local-player resolution is consistently based on `LocalContext.GetMe(...)`
- command execution mostly rides on game-native multiplayer-aware systems
- map selection in STS2 multiplayer uses vote actions through the existing map UI, and the mod already triggers map selection by clicking the actual UI node
- player-choice flows in the base game use synchronizers such as `PlayerChoiceSynchronizer`

What was missing:

- automatic activation for newly started multiplayer runs

## Remaining Boundaries

This change makes multiplayer support materially more viable, but the intended model is still:

- the mod controls only the local player's context on that client
- it should not try to make decisions for remote teammates
- each client that wants autonomous local assistance or automation should run its own mod instance

If future multiplayer-specific issues appear, the next places to inspect are likely:

- shared voting flows
- local-vs-remote screen ownership
- multi-player special-case decision UIs
