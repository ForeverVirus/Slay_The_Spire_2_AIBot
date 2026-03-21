# Slay the Spire 2 AI Bot Mod

[English README](./README_en.md)

一个Vibe coding的面向《Slay the Spire 2》的专用 AI Mod。它不是通用聊天助手，而是一个被限制在游戏内知识、游戏内决策和游戏内可执行操作范围内的专用 Agent。

## 项目定位

这个项目的目标，是把传统“自动打牌 Bot”升级为一个带有多模式交互能力的游戏内智能体：

- `Full Auto`：全自动接管整局流程
- `Semi Auto`：玩家用自然语言下达可执行指令，Agent 负责识别并执行
- `Assist`：不代打，只在关键决策点给出推荐
- `QnA`：回答游戏内问题，并结合当前局面给出建议

同时，这个项目支持本地知识库、自定义知识库、结构化规则约束，以及基于 LLM 的受限推理。

## 当前功能

### 1. 四种运行模式

- `Full Auto`
  自动处理战斗、奖励、地图路线、商店、篝火、事件、遗物、宝箱等奖励与房间决策。
- `Semi Auto`
  支持自然语言执行，例如“帮我打这一回合”“帮我选择奖励”“帮我选一条路”。
- `Assist`
  在战斗出牌、卡牌奖励、奖励领取、bundle、遗物、宝箱遗物、地图路线、商店、篝火、事件等界面贴出推荐标签。
- `QnA`
  回答卡牌、遗物、机制、构筑、当前该怎么打、当前该怎么选等奖励/路线/战斗问题。

### 2. 战斗与流程决策能力

- 战斗出牌
- 药水使用
- 结束回合
- 卡牌奖励选择
- 地图路径选择
- 商店购买
- 篝火行动
- 事件选项
- 遗物选择
- 宝箱遗物选择
- 奖励领取
- 一部分特殊界面，例如 Crystal Sphere

### 3. 知识库能力

- 结构化知识：
  角色、构筑、卡牌、遗物、药水、能力、敌人、事件、附魔、机制规则
- Markdown 攻略：
  总览、通用策略、角色长文说明
- 本地检索：
  用于 QnA、局面建议、LLM 提示增强
- 自定义覆盖：
  玩家可以通过 `custom` 层覆盖或补充 `core` 数据

### 4. 双语支持

- UI 支持中英切换
- README 提供中英双版本
- 自定义知识库文档也提供中英文版本

## 项目结构

当前仓库已经整理为“代码、知识库、阶段记录、参考源码”四大块：

```text
.
├─ aibot/                 # 实际 Mod 工程（Godot + C#）
├─ sts2_guides/           # 知识库目录，仅保留 core / custom 两层
│  ├─ core/               # 项目自带知识库
│  └─ custom/             # 玩家自定义知识库
├─ phase_reports/         # 分阶段开发记录
├─ sts2/                  # 游戏参考源码/反编译参考，只读，不建议修改
├─ localization/          # 本地化参考文件
└─ claude_plan.md         # 原始开发计划文档
```

### 关键目录说明

- [aibot](./aibot)
  真正会编译进 Mod 的项目目录。
- [sts2_guides/core](./sts2_guides/core)
  官方整理后的核心知识库。
- [sts2_guides/custom](./sts2_guides/custom)
  玩家可维护的自定义知识层，优先级高于 `core`。
- [phase_reports](./phase_reports)
  记录从 Phase 01 到当前阶段的实际开发推进与验证结果。
- [sts2](./sts2)
  用于理解游戏机制和数据来源的参考目录。通常不要直接改它。

## 运行方式

### 1. 游戏内使用

默认会显示一个右侧控制面板，用来：

- 切换模式
- 切换语言
- 打开或关闭聊天框
- 查看决策日志

### 2. 默认模式

默认配置下，Agent 采用：

- `defaultMode = fullAuto`
- 新开局自动接管
- 继续游戏自动接管

这些都可以在 [aibot/config.json](./aibot/config.json) 中修改。

## 配置说明

主配置文件位于：

- [aibot/config.json](./aibot/config.json)

配置模型定义位于：

- [AiBotConfig.cs](./aibot/Scripts/Config/AiBotConfig.cs)

### 顶层配置项

| 配置项 | 说明 |
| --- | --- |
| `enabled` | 是否启用 Mod |
| `preferCloud` | 是否优先使用云端 LLM |
| `autoTakeOverNewRun` | 新开局是否自动接管 |
| `autoTakeOverContinueRun` | 继续游戏是否自动接管 |
| `pollIntervalMs` | 主循环轮询间隔 |
| `decisionTimeoutSeconds` | 单次决策超时 |
| `screenActionDelayMs` | 普通界面动作延迟 |
| `combatActionDelayMs` | 战斗动作延迟 |
| `mapActionDelayMs` | 地图动作延迟 |
| `showDecisionPanel` | 是否显示决策面板 |
| `decisionPanelMaxEntries` | 决策日志最大条数 |

### `provider`

用于配置云端模型：

| 字段 | 说明 |
| --- | --- |
| `name` | 提供商名称 |
| `model` | 模型名 |
| `baseUrl` | API Base URL |
| `apiKey` | API Key |

说明：

- 当前项目默认兼容 DeepSeek 风格接口。
- 不建议把真实 API Key 提交到公共仓库。
- 如果没有可用 Key，系统会更多依赖本地规则、知识库和启发式决策。

### `agent`

| 字段 | 说明 |
| --- | --- |
| `defaultMode` | 默认模式，可选 `fullAuto / semiAuto / assist / qna` |
| `confirmOnModeSwitch` | 切模式时是否确认 |
| `maxConversationHistory` | 对话上下文上限 |

### `knowledge`

| 字段 | 说明 |
| --- | --- |
| `enableCustom` | 是否启用自定义知识库 |
| `customDir` | 自定义目录名，默认 `custom` |
| `maxCustomFileSize` | 自定义单文件大小上限 |

### `ui`

| 字段 | 说明 |
| --- | --- |
| `language` | UI 语言，默认 `zh-CN`，可切到 `en-US` |
| `showChatDialog` | 是否显示聊天窗口 |
| `chatHotkey` | 聊天热键 |
| `modeHotkeys` | 各模式热键 |
| `showModePanel` | 是否显示模式面板 |
| `modePanelHotkey` | 面板热键 |
| `modePanelStartVisible` | 启动时是否显示模式面板 |
| `showRecommendOverlay` | 是否显示辅助推荐标签 |

### `logging`

| 字段 | 说明 |
| --- | --- |
| `verbose` | 是否输出详细日志 |
| `logDecisionPrompt` | 是否记录完整决策提示词 |

## 知识库说明

知识库目录现在只保留两层：

```text
sts2_guides/
├─ core/
└─ custom/
```

### `core`

项目自带知识库，涵盖：

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

玩家自定义知识库层。它会在加载时覆盖 `core` 中同 `id` 或同 `slug` 的条目。

推荐入口文档：

- [自定义知识库说明（中文）](./sts2_guides/custom/README.md)
- [Custom Knowledge Guide (English)](./sts2_guides/custom/README_en.md)

### 自定义知识库适合修什么

- Agent 的错误规则理解
- 特定角色/流派偏好
- 自己更认可的路线选择策略
- 某些事件/遗物/卡牌的优先级

例如，近期项目已经用 `custom/game_mechanics.json` 增强了这类规则：

- 未用完能量不会结转到下一回合
- 格挡默认不会跨回合保留
- 未保留手牌会在回合结束时离手
- Ethereal 留手会被消耗
- 单回合 retain 会失效
- 临时费用修正会在回合结束清理

## 自定义知识库写法建议

### 1. 优先写 JSON

如果你在修正“机制理解错误”或“实体认知缺失”，优先使用 JSON。

最常用的是：

- `game_mechanics.json`
- `cards.json`
- `relics.json`
- `events.json`

### 2. Markdown 适合长文策略

如果你想补的是：

- 角色打法总览
- 某个构筑的完整思路
- 路线、Boss、资源管理长文建议

那更适合写 Markdown。

### 3. 覆盖策略

- 想覆盖旧规则：复用原 `id` 或 `slug`
- 想新增知识：新增一个不冲突的 `id`

## 构建与部署

### 环境

- Godot .NET SDK `4.5.1`
- .NET `9.0`
- Windows
- 已安装《Slay the Spire 2》

### 项目文件

- [aibot.csproj](./aibot/aibot.csproj)

### 常用构建命令

在仓库根目录执行：

```powershell
dotnet build aibot\aibot.csproj -c Release /p:CopyModAfterBuild=false
```

如果想在构建后自动复制到游戏目录，则可以直接：

```powershell
dotnet build aibot\aibot.csproj -c Release
```

### 注意事项

- 如果游戏正在运行，`aibot.dll` 可能会被 `SlayTheSpire2.exe` 锁定，导致复制失败。
- 这种情况下请先关闭游戏，再重新构建或手动复制 DLL。
- `aibot.csproj` 中默认的游戏路径是本机开发路径，如果你换机器，需要修改工程里的 `Sts2Dir`。

## 项目内部架构

### Agent 核心

- [AgentCore.cs](./aibot/Scripts/Agent/AgentCore.cs)
- [AgentMode.cs](./aibot/Scripts/Agent/AgentMode.cs)
- [CurrentDecisionAdvisor.cs](./aibot/Scripts/Agent/CurrentDecisionAdvisor.cs)

### 模式处理器

- [FullAutoModeHandler.cs](./aibot/Scripts/Agent/Handlers/FullAutoModeHandler.cs)
- [SemiAutoModeHandler.cs](./aibot/Scripts/Agent/Handlers/SemiAutoModeHandler.cs)
- [AssistModeHandler.cs](./aibot/Scripts/Agent/Handlers/AssistModeHandler.cs)
- [QnAModeHandler.cs](./aibot/Scripts/Agent/Handlers/QnAModeHandler.cs)

### 决策引擎

- [GuideHeuristicDecisionEngine.cs](./aibot/Scripts/Decision/GuideHeuristicDecisionEngine.cs)
- [DeepSeekDecisionEngine.cs](./aibot/Scripts/Decision/DeepSeekDecisionEngine.cs)
- [HybridDecisionEngine.cs](./aibot/Scripts/Decision/HybridDecisionEngine.cs)

### UI

- [AiBotDecisionPanel.cs](./aibot/Scripts/Ui/AiBotDecisionPanel.cs)
- [AgentChatDialog.cs](./aibot/Scripts/Ui/AgentChatDialog.cs)
- [AgentRecommendOverlay.cs](./aibot/Scripts/Ui/AgentRecommendOverlay.cs)
- [AgentModePanel.cs](./aibot/Scripts/Ui/AgentModePanel.cs)

### 知识系统

- [GuideKnowledgeBase.cs](./aibot/Scripts/Knowledge/GuideKnowledgeBase.cs)
- [KnowledgeSearchEngine.cs](./aibot/Scripts/Knowledge/KnowledgeSearchEngine.cs)
- [KnowledgeValidator.cs](./aibot/Scripts/Knowledge/KnowledgeValidator.cs)
- [KnowledgeSchema.cs](./aibot/Scripts/Knowledge/KnowledgeSchema.cs)

## 阶段记录

项目的详细开发过程在：

- [phase_reports](./phase_reports)

从 `Phase 01` 到当前阶段，基本覆盖了：

- Agent 架构重构
- 多模式支持
- 自然语言交互
- 推荐覆盖扩展
- 知识库结构化
- 自定义知识库
- 输入/热键/UI 调整
- 非全自动模式的决策覆盖补齐

## 已知边界

这个项目刻意保持“游戏内专用 Agent”的边界：

- 不处理与 STS2 无关的通用问题
- 不提供游戏外的文件/系统/网络代理能力
- LLM 只在受控上下文中参与问答和决策
- 执行动作必须映射到注册过的游戏内 Skills / Tools

## 未来可以继续增强的方向

### 1. 决策质量

- 对不同模式分流不同模型
- 为关键战斗建议引入更强的 reasoning 模型
- 提升多步计划执行与中断恢复能力

### 2. 知识库

- 持续补齐更多卡牌/遗物/事件/敌人细节
- 为更多规则加入“禁止错误推理”的约束式描述
- 给 `custom` 层增加模板文件

### 3. UI/交互

- 更丰富的推荐理由展示
- 战斗建议时间轴
- 更稳定的输入法与焦点处理
- 更细粒度的面板布局自定义

### 4. 可维护性

- 将更多日志、模式、提示词做成可视化调试页
- 补自动化测试
- 补 schema 校验工具和知识库 diff 工具

## 适合谁使用

- 想做全自动演示或挂机实验的玩家
- 想让 AI 辅助自己做决策，但不希望完全接管的玩家
- 想研究 STS2 机制并沉淀自定义知识库的玩家
- 想继续开发这个 Mod 的贡献者

## 相关文档

- [English README](./README_en.md)
- [自定义知识库说明（中文）](./sts2_guides/custom/README.md)
- [Custom Knowledge Guide (English)](./sts2_guides/custom/README_en.md)
- [原始开发计划](./claude_plan.md)
- [阶段开发记录](./phase_reports)

## 免责声明

本项目是面向《Slay the Spire 2》的 Mod 工程与实验性 Agent 系统。请在你理解其自动化行为和云端模型配置含义的前提下使用，尤其注意保护自己的 API Key 和本地游戏环境。
