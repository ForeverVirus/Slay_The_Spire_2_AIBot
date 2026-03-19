# Claude Memory — Slay the Spire 2 AIBot

## Goal
This file is a fast handoff / memory document for the next development session. It summarizes the current architecture, what has already been improved, known risks, and the highest-value next tasks.

## Project Structure
- `aibot/` — actual mod project, runtime loop, UI, decision engines, prompt logic.
- `sts2/` — Slay the Spire 2 API/bindings and decompiled support code used to inspect game mechanics.
- `sts2_guides/` — local game knowledge base used to enrich prompts and heuristics.

## Key Files
- `aibot/Scripts/Entry.cs`
  - Mod entry point.
- `aibot/Scripts/Core/AiBotRuntime.cs`
  - Main runtime loop, screen routing, activation, decision dispatch.
- `aibot/Scripts/Core/AiBotStateAnalyzer.cs`
  - Builds `RunAnalysis` from live game state.
- `aibot/Scripts/Decision/DeepSeekDecisionEngine.cs`
  - Main LLM prompt builder and cloud decision engine.
- `aibot/Scripts/Decision/GuideHeuristicDecisionEngine.cs`
  - Local heuristic fallback engine.
- `aibot/Scripts/Decision/HybridDecisionEngine.cs`
  - Cloud-first with heuristic fallback.
- `aibot/Scripts/Knowledge/GuideKnowledgeBase.cs`
  - Loads `sts2_guides/` and builds summaries / digests.
- `aibot/Scripts/Harmony/AiBotPatches.cs`
  - Runtime patches used to activate the bot.
- `aibot/config.json`
  - Runtime config. May contain API keys; do not commit secrets.

## Current Decision Architecture
- `AiBotRuntime` gathers live game state.
- `AiBotStateAnalyzer` converts live state into `RunAnalysis`.
- `HybridDecisionEngine` chooses between:
  - `DeepSeekDecisionEngine` (cloud / LLM)
  - `GuideHeuristicDecisionEngine` (fallback)
- `DeepSeekDecisionEngine` builds:
  - system prompt
  - shared context
  - state-specific context
  - decision options
- Supported decisions currently include:
  - combat action
  - potion usage
  - card reward
  - card selection / transform / remove / upgrade
  - bundle choice
  - crystal sphere action
  - reward claim
  - shop purchase
  - rest site choice
  - map routing
  - event option
  - relic choice

## What Has Already Been Optimized

### 1. Prompt system overhaul
`DeepSeekDecisionEngine.cs` has been heavily upgraded.

Completed improvements:
- Added a stronger `BuildSystemPrompt()` with character-specific strategy knowledge.
- Rewrote all major decision prompts with more STS2-specific guidance.
- Expanded `BuildSharedContext()` to include hard combat and run rules.
- Added stronger deck-building principles:
  - deck quality > deck size
  - scaling vs frontloaded damage
  - archetype coherence
  - removal value
  - shop / rest / map / relic / event heuristics

### 2. Turn-rule / “use it or lose it” fixes
The LLM previously behaved as if energy / hand / block could accumulate across turns.
This has been corrected in multiple prompt layers.

Current enforced rules in prompts:
- energy does **not** carry over between turns
- hand cards are discarded at end of turn unless `Retain`
- normal block is removed at the start of your next turn unless explicitly preserved
- draw pile empty => discard pile is shuffled into a new draw pile

Additional work already done:
- `end_turn` option hint is now much stricter
- combat prompt explicitly warns against ending turn with playable cards and energy left
- combat context includes an `ENERGY WARNING` when energy remains and actions are available

### 3. Incoming-damage / defense-context fixes
Previously the LLM could under-defend or believe “block overflow” made defense inefficient.

Completed improvements:
- Added incoming damage estimation using game intent API (`AttackIntent.GetTotalDamage(...)`)
- Combat context now shows:
  - `IncomingDamage`
  - `CurrentBlock`
  - `UnblockedDamage`
- Added explicit warning when current defense is insufficient
- Reworded neutral `end_turn` guidance so it is less biased toward passing
- Added stronger HP preservation guidance

### 4. Localization / formatting hardening
Completed stability fixes:
- Added safer text formatting wrappers to avoid crashes on broken localized strings
- Fixed orb smart-description formatting crash
- Hardened multiple prompt-building text paths

### 5. Startup / cloud reliability improvements
Completed reliability work:
- first cloud request retry support added
- `HttpClient.Timeout` now tied to configured decision timeout
- fallback path through `HybridDecisionEngine` remains active when cloud fails

### 6. Character-specific combat summaries
`AiBotStateAnalyzer.cs` already provides character combat summaries for:
- Silent
- Ironclad
- Regent
- Necrobinder

These summaries feed `RunAnalysis.CharacterCombatMechanicSummary` and are injected into prompts.

### 7. Defect orb passive prompt support (implemented, not yet battle-tested)
A major recent improvement was added to `DeepSeekDecisionEngine.cs` for Defect orb passives.

Current implemented behavior:
- Added `CalculateOrbPassiveEffects(PlayerCombatState?)`
- Added `OrbPassiveEffects` record
- Prompt now explicitly accounts for end-of-turn orb effects:
  - `FrostOrb` passive block
  - `LightningOrb` passive damage
  - `DarkOrb` end-of-turn evoke growth
  - `GlassOrb` end-of-turn AoE damage
- Combat context now includes `END-OF-TURN ORB PASSIVES` section
- Frost passive block is included in effective defense calculations
- Lightning / Glass passive damage is included in lethal opportunity messaging
- If Frost already covers incoming damage, prompt now tells the LLM not to over-invest in block

Important implementation note:
- Orb values use `PassiveVal` / `EvokeVal`, which are already Focus-adjusted for Frost / Lightning / most orb logic.
- Orb type detection currently uses concrete orb classes from `MegaCrit.Sts2.Core.Models.Orbs`.

## Current Build Status
Latest known status:
- `dotnet build aibot\aibot.csproj -c Release`
- result: success
- warnings: 0
- errors: 0

## Known Open Issues / To Verify

### High priority
1. **Defect orb passive calculations are implemented but not actually tested in live gameplay yet**
   - Need to confirm prompt output matches real combat behavior.
   - Need to verify Frost block timing is correct relative to enemy attack resolution.
   - Need to verify Lightning lethal messaging is helpful and not misleading in multi-enemy fights.
   - Need to verify Glass orb AoE handling is useful and not overestimated.
   - Current lethal estimation for Lightning in multi-enemy fights is still heuristic, because Lightning targets random enemies.

2. **Other characters may still have passive / delayed effects not explicitly modeled into prompt reasoning**
   - Silent, Ironclad, Regent, Necrobinder may have powers, pets, summons, or delayed mechanics that are not surfaced numerically enough.
   - Current character summaries exist, but many mechanics are still descriptive rather than calculational.
   - Need to inspect whether there are “free end-of-turn / next-turn / passive” effects that should be surfaced like orbs were.

## Recommended Next Tasks

### Task A — Live-verify Defect orb behavior
Recommended manual tests in game:
- 3 Frost orb scenario vs known incoming damage (e.g. incoming 15, current block 0)
- 1–3 Lightning orb lethal checks on low-HP enemy
- multi-enemy fight with Lightning to see if lethal suggestions become too optimistic
- full orb slots to ensure evoke reminder is still useful
- Frost + Lightning mixed board
- Dark orb growth over multiple turns
- Glass orb AoE lethal case

What to inspect:
- Does the prompt text shown in logs/UI match real combat outcomes?
- Does the bot stop wasting energy on extra block when Frost already covers lethal?
- Does it correctly take offensive lines when Lightning passive should finish enemies?

### Task B — Audit passive mechanics for non-Defect characters
Potential directions:
- Silent:
  - poison next-turn lethal awareness
  - After Image per-card block value already exists during play, but maybe prompt should surface expected block more clearly
  - retained cards / discard synergies / delayed poison kill messaging
- Ironclad:
  - Feel No Pain expected block from exhaust this turn
  - Dark Embrace expected draw from exhaust
  - Rupture / self-damage payoff valuation
- Regent:
  - star economy may still need more tactical calculation, not just description
  - companion / ally board-state value could be surfaced better
- Necrobinder:
  - summon / Osty value may need explicit threat / protection scoring
  - soul density and payoff timing may need numeric hints

### Task C — Improve lethal / damage modeling further
Current lethal estimation is still approximate.
Potential upgrades:
- estimate exact card damage more accurately instead of rough card dynamic vars only
- include multi-hit scaling and vulnerable/weak interactions more precisely
- handle random-target passive damage probabilistically instead of evenly distributed heuristic
- incorporate player powers/relic hooks into more calculations

### Task D — Improve block / mitigation modeling further
Potential upgrades:
- account for non-card passive defense beyond orbs if present
- estimate block from known powers / triggers this turn
- identify cases where killing one attacker reduces incoming total more than blocking

### Task E — Better context observability / debugging
Would help future tuning a lot.
Potential improvements:
- add optional debug logging of final prompt fragments for combat only
- log computed orb passive summary and effective block during combat decisions
- add a compact “why this choice was legal” trace in the decision UI
- expose the exact state context block shown to the LLM in a dev/debug panel

### Task F — Knowledge-base and prompt evolution
Potential improvements:
- inject more card-specific heuristics from `cards_full.json`
- improve relic synergy summaries using known archetype weighting
- add enemy-specific tactical hints where stable enough
- add act/boss-specific plan guidance

## Known Design Constraints
- This project has no simple black-box test suite; most validation is in live game runs.
- `aibot/config.json` may contain secrets.
- The mod copies on build, so `dotnet build` is the fastest dev loop before launching the game.
- Prompt tuning should remain focused and avoid making unrelated gameplay logic changes.

## Practical Build Command
From repo root:

```powershell
dotnet build aibot\aibot.csproj -c Release
```

If local game path differs, pass `Sts2Dir` explicitly.

## Suggested Next Session Start Checklist
1. Read this file.
2. Read `aibot/Scripts/Decision/DeepSeekDecisionEngine.cs`.
3. Read `aibot/Scripts/Core/AiBotStateAnalyzer.cs`.
4. Launch game and test Defect orb scenarios first.
5. If Defect looks good, audit non-Defect passive mechanics next.

## Short Status Summary
- Prompt system: heavily upgraded
- Combat turn rules: reinforced
- Incoming damage context: improved
- Defect orb passive prompt support: implemented
- Live gameplay verification of orb behavior: still needed
- Non-Defect passive-effect modeling: still incomplete
