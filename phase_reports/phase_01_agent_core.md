# Phase 01 — Agent Core 与模式骨架

## 已完成内容

### 1. Agent 核心架构已落地
已新增以下基础文件并接入项目：

- `aibot/Scripts/Agent/AgentMode.cs`
- `aibot/Scripts/Agent/IAgentModeHandler.cs`
- `aibot/Scripts/Agent/AgentCore.cs`
- `aibot/Scripts/Agent/Handlers/FullAutoModeHandler.cs`
- `aibot/Scripts/Agent/Handlers/SemiAutoModeHandler.cs`
- `aibot/Scripts/Agent/Handlers/AssistModeHandler.cs`
- `aibot/Scripts/Agent/Handlers/QnAModeHandler.cs`

本阶段完成后，项目已经从原来的单一 `AiBotRuntime` 自动化入口，升级为：

- 由 `AgentCore` 统一管理当前模式
- 由模式处理器 `IAgentModeHandler` 负责具体行为
- 由 `FullAutoModeHandler` 承接现有全自动能力
- 为后续半自动、辅助、问答模式预留正式接入位

### 2. FullAuto 模式已正式接管现有自动化流程
本阶段没有贸然把 `AiBotRuntime` 内部 1200+ 行旧逻辑大迁移，而是采用了兼容方案：

- 将现有自动化运行逻辑保留在 `AiBotRuntime`
- 新增 `ActivateLegacyFullAuto` / `DeactivateLegacyFullAuto`
- 由 `FullAutoModeHandler` 调用这两个兼容入口
- `AiBotRuntime.Activate()` / `Deactivate()` 现在改为通过 `AgentCore` 驱动模式切换

这样做的结果是：

- 现有全自动能力不退化
- 第一阶段就完成了“架构归位”
- 后续可以渐进式把旧逻辑从 Runtime 内进一步拆到 handler / skill 层，而不是一次性重构全部导致高风险

### 3. 配置层已加入 Agent 默认模式配置
已更新：

- `aibot/Scripts/Config/AiBotConfig.cs`
- `aibot/config.json`

新增配置：

```json
"agent": {
  "defaultMode": "fullAuto",
  "confirmOnModeSwitch": true
}
```

并在 `AiBotConfig` 中加入 `AgentRuntimeConfig`，支持：

- 默认模式读取
- 模式字符串到 `AgentMode` 枚举映射
- 后续模式切换确认策略扩展

### 4. 第一阶段已完成编译验证
已执行：

- `dotnet build aibot\aibot.csproj -c Release`

结果：

- 构建成功
- 当前无新增编译错误

---

## 未完成内容

以下内容明确尚未在本阶段完成：

### 1. 其他三种模式仍为骨架实现
当前：

- `SemiAutoModeHandler`
- `AssistModeHandler`
- `QnAModeHandler`

都已创建并可被 `AgentCore` 正式管理，但暂时仍是占位实现，只负责：

- 进入/退出模式
- 停止旧的全自动接管
- 返回占位提示文本

尚未完成：

- 半自动模式的聊天窗口和意图解析
- 辅助模式的“推荐”标签覆盖层
- 问答模式的本地知识检索和受限会话调用

### 2. 模式切换确认 UI 尚未接入
虽然 `AgentCore` 已支持“需要确认”的模式切换请求事件，但当前还没有 UI 消费该事件，因此：

- 自动进入默认模式可正常工作
- 手动模式切换 UI 还未落地
- 真实的二次确认弹窗将在后续 UI 阶段实现

### 3. 现有自动化逻辑尚未真正拆解到 Skill / Tool 层
本阶段选择的是“先挂接、后拆解”的低风险路线，因此尚未开始：

- `IAgentSkill`
- `IAgentTool`
- Skill Registry
- Tool Registry
- FullAuto / SemiAuto 共享执行层

这些将在下一阶段推进。

---

## 遇到的问题

### 问题 1：一次性迁移 `AiBotRuntime` 风险过高
`AiBotRuntime.cs` 当前承载了完整的：

- tick 主循环
- overlay 处理
- 战斗处理
- 地图处理
- 房间处理
- 商店/奖励/事件/水晶球等交互
- selector 与 UI panel 逻辑

如果第一阶段就强行把这些全部迁移到 `FullAutoModeHandler`，风险包括：

- 引入大量回归问题
- 编译虽然通过但运行行为可能微妙退化
- 后续难以定位是架构问题还是行为问题

### 问题 2：模式切换确认依赖 UI 层
计划里要求高风险模式切换二次确认，但当前 UI 系统尚未完成模式面板与确认对话框，因此第一阶段无法真正给出可点击确认的界面。

### 问题 3：配置层原本没有模式概念
原始配置里只有自动接管和 LLM 相关设置，没有统一 Agent 模式配置。若不先补配置模型，后续所有模式切换都只能硬编码，难以维护。

---

## 解决方案与后续建议

### 对问题 1 的解决方案
采用“兼容包裹式重构”：

- 保留旧 Runtime 自动化逻辑
- 通过 `FullAutoModeHandler` 包裹接管
- 后续逐步把 Runtime 内逻辑继续外提到：
  - `FullAutoModeHandler`
  - `IAgentSkill`
  - `IAgentTool`

这样能兼顾：

- 当前稳定性
- 后续架构演进空间

### 对问题 2 的解决方案
在下一阶段优先实现：

- `AgentModePanel`
- 模式切换快捷键
- 二次确认弹窗

等 UI 层完成后，再把 `AgentCore.ModeChangeRequested` 接上确认流程。

### 对问题 3 的解决方案
已经通过新增 `AgentRuntimeConfig` 完成了第一步。下一阶段可以继续扩展：

- 更多模式配置
- UI 相关配置
- 自定义知识库配置

---

## 下一阶段建议目标

建议严格按计划进入下一阶段：

1. 抽象 `IAgentSkill`
2. 抽象 `IAgentTool`
3. 建立 Skill / Tool 注册表
4. 为 FullAuto 模式逐步切换到底层 Skill 执行
5. 为 SemiAuto 模式打通未来的自然语言 → Skill 映射能力

这样第二阶段完成后，项目就会从“模式骨架已经有了”进入“真正具备 Agent 能力模型”的状态。
