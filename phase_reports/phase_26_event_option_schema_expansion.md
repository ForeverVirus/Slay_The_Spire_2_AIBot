# Phase 26 - Event option schema expansion

## Goal

Continue the master plan by improving the structured event knowledge model so it can support local QnA and future semi-auto / assist reasoning more directly.

This phase targets the next clear knowledge gap after enemy tactical data:

- preserve existing event names and localized flavor text
- add explicit trigger restriction summaries where relevant
- add structured option lists
- add concise localized option-result summaries

## Why this phase

Before this phase, `sts2_guides/core/events.json` mostly stored event names and descriptive text.

That was good enough for lore-style lookup, but not good enough for practical Agent questions like:

- “这个事件有哪些选项？”
- “选这个会发生什么？”
- “这个事件有没有出现条件？”
- “这是付钱换收益，还是进战斗，还是纯资源交换？”

Those are exactly the kinds of questions that matter in QnA mode and when the Agent later needs to explain or recommend event choices.

Encoding the option structure directly in the local dataset is more reliable than asking the Agent to infer choice outcomes from flavor text every time.

## Source areas reviewed

The following read-only source files were used to ground this update:

- `sts2/MegaCrit.Sts2.Core.Models.Events/BattlewornDummy.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Events/CrystalSphere.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Events/DenseVegetation.cs`
- `sts2/MegaCrit.Sts2.Core.Models.Events/DrowningBeacon.cs`
- `sts2/MegaCrit.Sts2.GameInfo.Objects/EventInfo.cs` (used as a reference for option-centric event metadata shape)

## Changes made

### 1. Expanded `EventEntry`

Updated `aibot/Scripts/Knowledge/GuideModels.cs` so each `EventEntry` can now carry:

- `triggerRestrictionEn`
- `triggerRestrictionZh`
- `options`

Also added a structured `EventOptionGuideEntry` with:

- `id`
- `titleEn`
- `titleZh`
- `resultEn`
- `resultZh`

This remains backward-compatible with existing event JSON while making room for more decision-useful metadata.

### 2. Updated knowledge search rendering

Updated `aibot/Scripts/Knowledge/KnowledgeSearchEngine.cs` so event lookups now include:

- trigger restrictions
- structured event options
- localized result summaries for each option

This gives local retrieval a much more actionable answer surface for event questions.

### 3. Enriched representative event entries

Updated `sts2_guides/core/events.json` with source-backed structure for representative event types:

- `battleworn-dummy`
- `crystal-sphere`
- `dense-vegetation`
- `drowning-beacon`

These cover several useful event patterns:

- combat challenge with tiered rewards
- restricted-appearance event with tradeoff-based minigame entry
- branch event that can convert into forced combat
- straightforward binary reward tradeoff event

### 4. Updated schema documentation

Updated `sts2_guides/core/schema.json` to document the richer `EventEntry` shape and bumped the schema version.

## Notes on scope

This phase intentionally improves the structure and retrieval quality of the current curated event dataset.

It does **not** yet attempt to annotate every event in `events.json` with full option trees and outcomes.

That wider pass is still a meaningful remaining phase and can be completed later by event family / act / reward pattern.

## Validation

Build validation completed successfully:

```powershell
dotnet build aibot\aibot.csproj -c Release /p:CopyModAfterBuild=false
```

Result:

- build succeeded
- no compile errors introduced by the new `EventEntry` fields
- local event retrieval remained build-safe

## Outcome

The Agent can now answer event questions with much more operational detail:

- whether an event has trigger restrictions
- what the main options are
- what each option roughly gives or costs
- whether a branch leads to combat, healing, gold payment, curses, relics, or potions

This moves the event-knowledge layer closer to the plan's target of a structured, game-specific Agent that can explain and reason about in-game decisions reliably from local data.
