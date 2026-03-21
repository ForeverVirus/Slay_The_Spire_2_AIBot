# Slay the Spire 2 AI Bot Mod

[中文 README](./README.md)

A Vibe coding dedicated AI mod for Slay the Spire 2. This is not a general-purpose chat assistant. It is a constrained in-game agent focused on game knowledge, game decisions, and game-executable actions only.

## Project Positioning

This project aims to evolve a traditional "autoplay bot" into a multi-mode in-game AI system:

- `Full Auto`: takes over the full run flow automatically
- `Semi Auto`: the player issues natural-language instructions and the agent interprets and executes them
- `Assist`: does not play for the user, but provides recommendations at key decision points
- `QnA`: answers in-game questions and can combine the current board state with the knowledge base to give actionable advice

The project also supports a local structured knowledge base, a user-maintained custom knowledge layer, rule-style guardrails, and LLM-assisted reasoning under controlled scope.

## Current Capabilities

### 1. Four Runtime Modes

- `Full Auto`
  Automatically handles combat, rewards, map routing, shop decisions, campfire choices, events, relic selection, chest relics, and other room-level decisions.
- `Semi Auto`
  Supports natural-language execution, such as "play this turn for me", "choose this reward for me", or "pick a path for me".
- `Assist`
  Displays non-invasive recommendation tags on combat cards and on major decision screens such as card rewards, reward claims, bundles, relics, chest relics, map routing, shop choices, campfire options, and events.
- `QnA`
  Answers questions about cards, relics, mechanics, builds, and context-sensitive questions such as what to play now or what to choose on the current screen.

### 2. Decision Coverage

- combat card play
- potion usage
- end turn
- card reward selection
- map path selection
- shop purchases
- campfire actions
- event options
- relic selection
- chest relic selection
- reward claiming
- some special decision screens such as `Crystal Sphere`

### 3. Knowledge System

- Structured data for:
  characters, builds, cards, relics, potions, powers, enemies, events, enchantments, and game mechanics
- Markdown guide content for:
  overviews, general strategy, and longer-form character guidance
- Local retrieval for:
  QnA answers, current-state advice, and LLM prompt augmentation
- Custom override layer:
  players can override or extend `core` data through the `custom` layer

### 4. Bilingual Support

- UI language switching between Chinese and English
- bilingual project README
- bilingual custom knowledge base guide

## Project Structure

The repository has been cleaned up into four main areas: code, knowledge, development reports, and reference material.

```text
.
|-- aibot/                 # Actual mod project (Godot + C#)
|-- sts2_guides/           # Knowledge base directory, now only core/custom
|   |-- core/              # Built-in project knowledge
|   `-- custom/            # User-maintained custom knowledge
|-- phase_reports/         # Phased development records
|-- sts2/                  # Decompiled/reference game code, read-only reference
|-- localization/          # Localization-related reference files
`-- claude_plan.md         # Original development plan
```

### Key Directories

- [aibot](./aibot)
  The actual mod that gets built and loaded by the game.
- [sts2_guides/core](./sts2_guides/core)
  The canonical built-in knowledge base.
- [sts2_guides/custom](./sts2_guides/custom)
  The user-editable custom knowledge layer. It has higher priority than `core`.
- [phase_reports](./phase_reports)
  A detailed record of what was implemented and validated across each development phase.
- [sts2](./sts2)
  A reference directory used to understand game systems and source behaviors. It should normally be treated as read-only.

## How It Works In Game

### 1. Runtime UI

By default, the mod shows a right-side control panel used to:

- switch modes
- switch language
- open or close the chat dialog
- inspect decision logs

### 2. Default Behavior

With the default configuration, the agent uses:

- `defaultMode = fullAuto`
- automatic takeover on new runs
- automatic takeover on continued runs

These can all be changed in [aibot/config.json](./aibot/config.json).

## Configuration

Main configuration file:

- [aibot/config.json](./aibot/config.json)

Config model definition:

- [AiBotConfig.cs](./aibot/Scripts/Config/AiBotConfig.cs)

### Top-Level Fields

| Field | Meaning |
| --- | --- |
| `enabled` | Whether the mod is enabled |
| `preferCloud` | Whether to prefer a cloud LLM |
| `autoTakeOverNewRun` | Automatically take over new runs |
| `autoTakeOverContinueRun` | Automatically take over continued runs |
| `pollIntervalMs` | Main polling interval |
| `decisionTimeoutSeconds` | Timeout for one decision cycle |
| `screenActionDelayMs` | Delay for general UI actions |
| `combatActionDelayMs` | Delay for combat actions |
| `mapActionDelayMs` | Delay for map actions |
| `showDecisionPanel` | Whether to show the decision panel |
| `decisionPanelMaxEntries` | Max number of log entries in the decision panel |

### `provider`

Used to configure the cloud model provider.

| Field | Meaning |
| --- | --- |
| `name` | Provider name |
| `model` | Model name |
| `baseUrl` | API base URL |
| `apiKey` | API key |

Notes:

- The current project is compatible with a DeepSeek-style API setup.
- Do not commit a real API key to a public repository.
- If no working API key is available, the system leans more heavily on local rules, local knowledge, and heuristic decisions.

### `agent`

| Field | Meaning |
| --- | --- |
| `defaultMode` | Default mode, one of `fullAuto / semiAuto / assist / qna` |
| `confirmOnModeSwitch` | Whether to confirm before switching modes |
| `maxConversationHistory` | Maximum retained conversation context |

### `knowledge`

| Field | Meaning |
| --- | --- |
| `enableCustom` | Whether to enable the custom knowledge layer |
| `customDir` | Name of the custom directory, default is `custom` |
| `maxCustomFileSize` | Max size for one custom file |

### `ui`

| Field | Meaning |
| --- | --- |
| `language` | UI language, default `zh-CN`, can switch to `en-US` |
| `showChatDialog` | Whether to show the chat window |
| `chatHotkey` | Chat hotkey |
| `modeHotkeys` | Per-mode hotkeys |
| `showModePanel` | Whether to show the mode panel |
| `modePanelHotkey` | Mode panel hotkey |
| `modePanelStartVisible` | Whether the panel is visible on startup |
| `showRecommendOverlay` | Whether to show assist recommendation overlays |

### `logging`

| Field | Meaning |
| --- | --- |
| `verbose` | Whether to output verbose logs |
| `logDecisionPrompt` | Whether to log full decision prompts |

## Knowledge Base

The knowledge directory now intentionally has only two layers:

```text
sts2_guides/
|-- core/
`-- custom/
```

### `core`

Built-in project knowledge, including:

- `characters.json`
- `builds.json`
- `cards.json`
- `relics.json`
- `potions.json`
- `powers.json`
- `enemies.json`
- `events.json`
- `enchantments.json`
- `game_mechanics.json`
- `guides/*.md`

### `custom`

The user-maintained custom knowledge layer. During loading, it can override `core` entries with the same `id` or `slug`.

Recommended entry docs:

- [Custom Knowledge Guide (Chinese)](./sts2_guides/custom/README.md)
- [Custom Knowledge Guide (English)](./sts2_guides/custom/README_en.md)

### What Custom Knowledge Is Good For

- correcting the agent's misunderstanding of a game rule
- encoding personal preferences for a character or build
- tightening route or reward priorities
- reinforcing specific card, relic, event, or resource-management heuristics

For example, the project already uses `custom/game_mechanics.json` to reinforce rules such as:

- unused energy does not carry over by default
- block normally does not carry to the next turn
- non-retained cards leave hand at end of turn
- Ethereal cards are exhausted if still in hand
- one-turn retain effects expire
- many temporary cost changes are cleared at end of turn

## Custom Knowledge Authoring Advice

### 1. Prefer JSON First

If you are fixing a wrong mechanical assumption or missing entity data, JSON is usually the best choice.

Common files include:

- `game_mechanics.json`
- `cards.json`
- `relics.json`
- `events.json`

### 2. Use Markdown For Long-Form Strategy

Markdown is better for:

- character playstyle overviews
- complete build ideas
- routing, boss, and resource-management essays

### 3. Override Strategy

- To override an existing entry: reuse the same `id` or `slug`
- To add a new entry: create a non-conflicting new `id`

## Build And Deployment

### Environment

- Godot .NET SDK `4.5.1`
- .NET `9.0`
- Windows
- Slay the Spire 2 installed locally

### Project File

- [aibot.csproj](./aibot/aibot.csproj)

### Common Build Command

Run from the repository root:

```powershell
dotnet build aibot\aibot.csproj -c Release /p:CopyModAfterBuild=false
```

If you want the build to copy directly into the game directory:

```powershell
dotnet build aibot\aibot.csproj -c Release
```

### Notes

- If the game is currently running, `aibot.dll` may be locked by `SlayTheSpire2.exe`, causing the copy step to fail.
- In that case, close the game first, then rebuild or copy the DLL manually.
- The default `Sts2Dir` in `aibot.csproj` points to a local development machine path and may need adjustment on another machine.

## Internal Architecture

### Agent Core

- [AgentCore.cs](./aibot/Scripts/Agent/AgentCore.cs)
- [AgentMode.cs](./aibot/Scripts/Agent/AgentMode.cs)
- [CurrentDecisionAdvisor.cs](./aibot/Scripts/Agent/CurrentDecisionAdvisor.cs)

### Mode Handlers

- [FullAutoModeHandler.cs](./aibot/Scripts/Agent/Handlers/FullAutoModeHandler.cs)
- [SemiAutoModeHandler.cs](./aibot/Scripts/Agent/Handlers/SemiAutoModeHandler.cs)
- [AssistModeHandler.cs](./aibot/Scripts/Agent/Handlers/AssistModeHandler.cs)
- [QnAModeHandler.cs](./aibot/Scripts/Agent/Handlers/QnAModeHandler.cs)

### Decision Engines

- [GuideHeuristicDecisionEngine.cs](./aibot/Scripts/Decision/GuideHeuristicDecisionEngine.cs)
- [DeepSeekDecisionEngine.cs](./aibot/Scripts/Decision/DeepSeekDecisionEngine.cs)
- [HybridDecisionEngine.cs](./aibot/Scripts/Decision/HybridDecisionEngine.cs)

### UI

- [AiBotDecisionPanel.cs](./aibot/Scripts/Ui/AiBotDecisionPanel.cs)
- [AgentChatDialog.cs](./aibot/Scripts/Ui/AgentChatDialog.cs)
- [AgentRecommendOverlay.cs](./aibot/Scripts/Ui/AgentRecommendOverlay.cs)
- [AgentModePanel.cs](./aibot/Scripts/Ui/AgentModePanel.cs)

### Knowledge System

- [GuideKnowledgeBase.cs](./aibot/Scripts/Knowledge/GuideKnowledgeBase.cs)
- [KnowledgeSearchEngine.cs](./aibot/Scripts/Knowledge/KnowledgeSearchEngine.cs)
- [KnowledgeValidator.cs](./aibot/Scripts/Knowledge/KnowledgeValidator.cs)
- [KnowledgeSchema.cs](./aibot/Scripts/Knowledge/KnowledgeSchema.cs)

## Phase Reports

Detailed development progress lives in:

- [phase_reports](./phase_reports)

From `Phase 01` to the latest phase, the reports cover:

- agent architecture refactors
- multi-mode support
- natural-language interaction
- recommendation coverage expansion
- structured knowledge base growth
- custom knowledge support
- input, hotkey, and UI adjustments
- decision coverage outside full auto mode

## Known Scope Boundaries

This project intentionally stays within the boundary of an in-game STS2 agent:

- it does not handle unrelated general-purpose questions
- it does not expose arbitrary file/system/network operations inside the game
- the LLM participates only inside controlled prompt and tool contexts
- executable actions must map to registered in-game skills or tools

## Future Enhancements

### 1. Decision Quality

- route different models to different modes
- use stronger reasoning models for high-value combat advice
- improve multi-step plan execution and interruption recovery

### 2. Knowledge Base

- keep filling missing detail for more cards, relics, events, and enemies
- add more explicit "do not reason this way" rule constraints
- provide more starter templates in the `custom` layer

### 3. UI And Interaction

- richer display of recommendation reasoning
- lower latency for combat recommendations
- even more robust IME and focus handling
- finer-grained panel layout customization

### 4. Maintainability

- expose more logs, prompts, and modes in visual debug tools
- add automated tests
- add schema validation tools and knowledge-base diff tools

## Who This Project Is For

- players who want a full-auto demonstration or automation experiment
- players who want AI-assisted decisions without fully giving up control
- players who want to study STS2 mechanics and refine a custom knowledge layer
- contributors who want to continue developing this mod

## Related Docs

- [中文 README](./README.md)
- [Custom Knowledge Guide (Chinese)](./sts2_guides/custom/README.md)
- [Custom Knowledge Guide (English)](./sts2_guides/custom/README_en.md)
- [Original Development Plan](./claude_plan.md)
- [Phase Reports](./phase_reports)

## Disclaimer

This project is an experimental AI-agent mod for Slay the Spire 2. Use it with a clear understanding of its automation behavior and cloud-model configuration, and take care to protect your API keys and local game environment.
