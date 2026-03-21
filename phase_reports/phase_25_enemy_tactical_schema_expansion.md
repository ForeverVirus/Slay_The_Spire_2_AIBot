# Phase 25 - Enemy tactical schema expansion

## Goal

Continue the master plan by improving the structured enemy knowledge model so it better matches the Agent's intended retrieval and explanation surface.

This phase targets the next obvious knowledge gap after powers and potions:

- preserve existing enemy names / move lists / localized labels
- add compact HP range metadata
- add explicit action-pattern summaries
- add explicit special-mechanics summaries
- add explicit threat summaries for tactical retrieval

## Why this phase

Before this phase, `sts2_guides/core/enemies.json` already contained broad enemy coverage, but most entries were still shallow.

The file mostly carried:

- enemy name
- localized name
- move ids / titles
- source layer

That was enough for a basic lookup, but not enough for the Agent to answer questions like:

- “这只怪一般按什么顺序行动？”
- “它的危险点到底是什么？”
- “这只怪有没有开场机制、成长机制或者特殊状态？”
- “大概多少血，属于可以速杀还是要规划资源的目标？”

Encoding those answers directly in the structured dataset gives the local knowledge path much more tactical value and reduces reliance on brittle inference from raw move names.

## Source areas reviewed

The following read-only source files were used to ground this update:

- `sts2/MegaCrit.Sts2.Core.Models.Monsters/CalcifiedCultist.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Monsters/FuzzyWurmCrawler.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Monsters/LagavulinMatriarch.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Monsters/LouseProgenitor.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Monsters/DampCultist.cs` (used as additional cultist-family reference while validating pattern conventions)

## Changes made

### 1. Expanded `EnemyEntry`

Updated `aibot/Scripts/Knowledge/GuideModels.cs` so each `EnemyEntry` can now carry:

- `hpRange`
- `intentPatternEn`
- `intentPatternZh`
- `specialMechanicsEn`
- `specialMechanicsZh`
- `threatSummaryEn`
- `threatSummaryZh`

This remains backward-compatible with the existing JSON while giving local retrieval a much richer tactical surface.

### 2. Updated knowledge search rendering

Updated `aibot/Scripts/Knowledge/KnowledgeSearchEngine.cs` so enemy lookups now include:

- HP range
- bilingual intent pattern summaries
- bilingual special-mechanics summaries
- bilingual threat summaries

This improves local QnA quality for combat / planning questions without requiring extra LLM reasoning.

### 3. Enriched representative enemy entries

Updated `sts2_guides/core/enemies.json` with source-backed tactical metadata for representative enemies:

- `calcified-cultist`
- `fuzzy-wurm-crawler`
- `lagavulin-matriarch`
- `louse-progenitor`

These cover several useful enemy behavior classes:

- simple ritual-based scaler
- low-pressure opener into buffed burst attacker
- sleeping elite / boss with wake-up sequencing
- curl-up defender that mixes block, scaling, and Frail pressure

During this pass, two move lists were also corrected to better match read-only source behavior:

- `fuzzy-wurm-crawler` now includes `INHALE`
- `louse-progenitor` now uses `POUNCE` instead of the stale `ACID_DUST` entry

### 4. Updated schema documentation

Updated `sts2_guides/core/schema.json` to document the richer `EnemyEntry` shape and bumped the schema version.

## Notes on scope

This phase intentionally improves the shape and retrieval quality of the current curated enemy dataset.

It does **not** yet attempt to annotate every enemy in `enemies.json` with tactical summaries.

That broader sweep is still a meaningful remaining phase and can be completed incrementally by enemy family / act / encounter tier.

## Validation

Build validation completed successfully:

```powershell
dotnet build aibot\aibot.csproj -c Release
```

Result:

- build succeeded
- no compile errors introduced by the new `EnemyEntry` fields
- local enemy retrieval remained build-safe

## Outcome

The Agent can now answer enemy questions with much more tactical detail:

- approximate HP expectations
- typical action ordering
- wake-up / scaling / buff / debuff mechanics
- why an enemy is dangerous in practice

This moves the combat-knowledge layer closer to the plan's target of a structured, game-specific Agent that can explain and reason about in-game threats reliably from local data.
