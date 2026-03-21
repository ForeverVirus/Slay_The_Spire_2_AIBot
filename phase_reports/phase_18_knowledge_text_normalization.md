# Phase 18 - Knowledge Text Normalization

## Goal

Prevent raw knowledge-base placeholders, BBCode tags, and icon placeholders from leaking into player-facing UI text across QnA, lookup tools, and knowledge-backed summaries.

## Scope

Only `aibot/` was modified. No changes were made under `sts2/`.

## What changed

### 1. Centralized knowledge text formatter

Added `aibot/Scripts/Knowledge/KnowledgeTextFormatter.cs`.

Responsibilities:
- resolve common placeholder patterns such as `{Damage:diff()}`, `{Energy:energyIcons()}`, `{Stars:starIcons()}`, `{X:plural:a|b}`, and `{Flag:cond:yes|no}`
- pull default variable values from runtime models when available (`ModelDb` + `DynamicVars`)
- sanitize BBCode-like tags such as `[gold]`, `[blue]`, `[purple]`, `[jitter]`, etc.
- convert image/icon tags to plain readable symbols such as `⚡` and `★`
- fall back to game-formatted runtime descriptions for model-backed entries when unresolved placeholders remain

### 2. QnA / lookup output surfaces now use normalized text

Updated:
- `aibot/Scripts/Knowledge/KnowledgeSearchEngine.cs`
- `aibot/Scripts/Agent/Tools/LookupCardTool.cs`
- `aibot/Scripts/Agent/Tools/LookupRelicTool.cs`
- `aibot/Scripts/Agent/Tools/LookupBuildTool.cs`

Effects:
- card / relic / potion / power / event / enchantment descriptions shown to the player are normalized before display
- build summaries and tips are sanitized before display
- enemy description and banter text also pass through plain-text cleanup

### 3. Knowledge summaries used by other modes are normalized

Updated:
- `aibot/Scripts/Knowledge/GuideKnowledgeBase.cs`
- `aibot/Scripts/Core/AiBotStateAnalyzer.cs`
- `aibot/Scripts/Decision/DeepSeekDecisionEngine.cs`

Effects:
- deck / relic / potion summaries derived from the guide database no longer embed raw placeholder syntax
- preferred build summaries used by analyzer context are sanitized
- guide notes forwarded into decision-engine text no longer carry raw card / relic placeholder text

## Implementation notes

- The formatter prefers model-backed default values where the game exposes them through `DynamicVars`.
- For runtime-dependent text that cannot be fully reconstructed safely in both languages, the formatter degrades gracefully by cleaning the raw guide text and using runtime-formatted text as a fallback only when necessary.
- The formatter is intentionally centralized so future UI surfaces can reuse the same normalization path instead of re-implementing ad-hoc cleanup.

## Validation

Ran:

```powershell
dotnet build aibot\aibot.csproj -c Release
```

Result:
- build succeeded
- no editor diagnostics remained in the changed files after final wiring

## Follow-up notes

- If future knowledge JSON adds new SmartFormat functions, they should be added in `KnowledgeTextFormatter` instead of patching individual tools or handlers.
- If we later need perfect bilingual runtime formatting for both EN and ZH simultaneously, that should be solved as a dedicated localization-layer task rather than by mutating global game language state during gameplay.
