# Phase 23 - Power rule schema expansion

## Goal

Continue the master plan by improving the structured `powers.json` dataset so it better matches the intended Agent knowledge model.

This phase focuses on the P0/P1 overlap around Power knowledge quality:

- keep the existing power names / types / descriptions
- add explicit stack semantics for retrieval
- add explicit resolution semantics for QnA and explanation workflows
- surface those new fields through the local knowledge search pipeline

## Why this phase

After phase 22, `game_mechanics.json` had better source-backed combat rules, but `powers.json` was still relatively shallow.

It only exposed:

- name
- type
- localized description

That was enough for basic lookup, but it did not satisfy the plan's requirement that Power data should include stack behavior and settlement / resolution behavior.

Because many player questions are framed as:

- “这个状态怎么叠？”
- “它什么时候结算？”
- “这个效果是一层层减还是永久的？”

it is more useful to encode those answers directly in the Power entries instead of forcing the Agent to infer them from raw localized descriptions.

## Source areas reviewed

The following read-only source files were used to ground the new rule fields:

- `sts2/MegaCrit.Sts2.Core.Models/PowerModel.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Powers/ArtifactPower.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Powers/BarricadePower.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Powers/BlurPower.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Powers/BufferPower.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Powers/DexterityPower.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Powers/FocusPower.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Powers/FrailPower.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Powers/IntangiblePower.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Powers/NoDrawPower.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Powers/PlatingPower.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Powers/PoisonPower.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Powers/RegenPower.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Powers/RitualPower.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Powers/StrengthPower.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Powers/ThornsPower.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Powers/VulnerablePower.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Powers/WeakPower.cs`

## Changes made

### 1. Expanded `PowerEntry`

Updated `aibot/Scripts/Knowledge/GuideModels.cs` so each `PowerEntry` can now carry:

- `stackType`
- `stackRuleEn`
- `stackRuleZh`
- `resolutionRuleEn`
- `resolutionRuleZh`

This keeps the data model backward-compatible while allowing richer knowledge retrieval.

### 2. Updated knowledge search rendering

Updated `aibot/Scripts/Knowledge/KnowledgeSearchEngine.cs` so Power lookups now include:

- stack type
- stack rules
- resolution rules

This makes QnA and other retrieval consumers return more useful power explanations without needing extra LLM inference.

### 3. Enriched `sts2_guides/core/powers.json`

Added source-backed structured metadata for the currently curated power set, including:

- Artifact
- Barricade
- Blur
- Buffer
- Dexterity
- Focus
- Frail
- Intangible
- No Draw
- Plating
- Poison
- Regen
- Ritual
- Strength
- Thorns
- Vulnerable
- Weak

For each entry, the file now stores:

- whether the power behaves like `Counter` or `Single`
- how reapplication / stacking should be interpreted
- how the power actually resolves in combat

### 4. Updated human-readable schema docs

Updated `sts2_guides/core/schema.json` to document the richer `PowerEntry` shape for future maintenance and custom knowledge editing.

## Notes on scope

This phase intentionally improves the structured shape and retrieval quality of the existing curated Power set.

It does **not** yet attempt to exhaustively extract every in-game Power into `powers.json`.

That larger extraction step can be handled later as a dedicated source-to-knowledge completion phase.

## Validation

Build validation completed successfully:

```powershell
dotnet build aibot\aibot.csproj -c Release
```

Result:

- build succeeded
- no compile errors introduced by the new `PowerEntry` fields
- knowledge search rendering remained build-safe

## Outcome

The Agent now has a better structured answer surface for Power-related questions.

Instead of only returning name/type/description, it can now expose:

- whether a Power stacks as a counter or a single-instance effect
- how additional applications should be interpreted
- when and how the effect actually resolves

This moves the knowledge layer closer to the plan's target of a game-specific Agent that can explain mechanics reliably using local structured data.
