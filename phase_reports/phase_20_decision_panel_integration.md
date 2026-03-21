# Phase 20 - Decision Panel Integration Polish

## Goal

Complete the remaining decision-panel integration work from the plan by turning the existing debug feed into a more useful Agent status surface.

## Scope

Only `aibot/` was modified. No changes were made under `sts2/`.

## What changed

### 1. Added current Agent mode display to the decision panel

Updated `aibot/Scripts/Ui/AiBotDecisionPanel.cs`.

Effects:
- the panel now shows the currently active Agent mode in its header
- the mode label stays in sync via `AgentCore.ModeChanged`
- the decision feed is easier to read when switching between Full Auto / Semi Auto / Assist / QnA during one run

### 2. Added minimize / expand behavior

Updated `aibot/Scripts/Ui/AiBotDecisionPanel.cs`.

Effects:
- the panel now has a header toggle button
- minimizing collapses the log body while preserving a compact header
- expanding restores the full decision-feed view without clearing entries
- the existing runtime visibility logic remains unchanged

## Implementation notes

- This phase intentionally keeps the decision panel as a lightweight observer surface rather than introducing a second mode-switch UI.
- The new header-only collapsed state preserves visibility of the panel title and current mode while reducing screen occupation.
- Existing decision-feed queueing, trimming, and rendering behavior remains intact.

## Validation

Ran:

```powershell
dotnet build aibot\aibot.csproj -c Release
```

Result:
- build succeeded
- editor diagnostics for the changed file were clean after the final import fix

## Follow-up notes

- If later phases add draggable / resizable panels, `AiBotDecisionPanel` is now a more suitable place to add those interactions.
- If we want hotkey control for collapse in the future, it can be added without changing the decision-feed backend.
