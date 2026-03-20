# Phase 09 — Plan 缺口补齐（配置 / 热键 / 构建）

## 已完成内容

### 1. 按 `claude_plan.md` 补齐了配置模型
已更新：

- `aibot/Scripts/Config/AiBotConfig.cs`
- `aibot/config.json`

本阶段优先回到 `plan` 第 24~26 条，对前八阶段尚未落地的“硬缺口”做补齐。

当前已新增并映射：

- `agent.maxConversationHistory`
- `knowledge.enableCustom`
- `knowledge.customDir`
- `knowledge.maxCustomFileSize`
- `ui.modeHotkeys`
- `ui.showModePanel`
- `ui.showRecommendOverlay`

同时保留了现有运行所需的原配置结构，避免破坏之前已经接好的模式与 UI 初始化流程。

---

### 2. 模式切换热键已改为符合计划的 `F5/F6/F7/F8`
已更新：

- `aibot/Scripts/Ui/AgentModePanel.cs`

当前 `AgentModePanel` 已支持直接通过配置里的 `modeHotkeys` 切换模式：

- `F5` → `FullAuto`
- `F6` → `SemiAuto`
- `F7` → `Assist`
- `F8` → `QnA`

这次修正了之前“`F8` 只负责开关面板”的偏差，使行为重新与 `plan` 一致。

另外为了不丢失已有的面板折叠能力，保留了独立的 `modePanelHotkey`，默认调整为：

- `F4` → 显示 / 隐藏模式面板

这样既满足计划中的四模式快捷切换，又保留了现有 UI 便捷性。

---

### 3. 聊天历史上限已改为受配置控制
已更新：

- `aibot/Scripts/Ui/AgentChatDialog.cs`

之前聊天窗口内部将历史消息条数硬编码为 `50`。本阶段已改为读取：

- `config.agent.maxConversationHistory`

这样后续在问答模式、多轮上下文、半自动确认交互进一步扩展时，就有了统一的历史窗口上限配置入口。

---

### 4. 构建脚本已支持递归复制知识库并保留目录结构
已更新：

- `aibot/aibot.csproj`

之前 `.csproj` 只会平铺复制 `sts2_guides` 顶层的 `*.json` / `*.md`，这与 `plan` 中后续的知识库结构化方案不一致。

现在已调整为：

- 使用 `**/*.json` 与 `**/*.md` 递归包含子目录
- 在复制到 `mods/aibot/KnowledgeBase/` 时保留 `RecursiveDir`

这为后续知识库重构提供了基础，尤其是：

- `core/`
- `custom/`
- 更细分的角色 / 构筑 / 机制资料目录

---

### 5. 默认配置已与当前架构更一致
当前 `config.json` 已新增：

- `knowledge` 节点
- `modeHotkeys` 节点
- `showModePanel`
- `showRecommendOverlay`
- `maxConversationHistory`

这意味着：

- 新环境首次运行时就能拿到正确的默认 UI / 模式配置
- 后续实现自定义知识库加载时，无需再次改动配置结构

---

### 6. 第九阶段已完成编译验证
已执行：

- `dotnet build aibot\aibot.csproj -c Release`

结果：

- 构建成功
- 当前无新增编译错误

---

## 本阶段补齐的计划缺口
本阶段主要对应 `claude_plan.md`：

- 第 24 条：配置文件扩展
- 第 25 条：更新 `AiBotConfig.cs`
- 第 26 条：更新 `aibot.csproj`
- 第 20 / 21 条中的热键配置落地完善

也就是说，这一阶段不是新增大功能，而是把前面已经铺开的 Agent/UI 体系，重新和原始 `plan` 的基础设施要求对齐。

---

## 未完成内容

### 1. 半自动模式执行前确认仍未完成
虽然模式切换确认已经具备，但 `SemiAuto` 里对于“识别到可执行动作后的确认执行按钮 / 确认步骤”仍未落地。

这是 `plan` 中聊天交互层的重要缺口之一。

### 2. `GuideKnowledgeBase` 仍未重构为 `core/custom` 双层模型
虽然 `.csproj` 已经可以递归复制知识库，但当前运行时知识库加载逻辑仍是旧结构，还没有真正：

- 区分内置知识与自定义知识
- 建立冲突处理策略
- 对自定义知识做大小 / 格式约束

### 3. `KnowledgeValidator` / `KnowledgeSchema` 仍缺失
当前只是先补了配置入口和目录复制能力，知识库校验层本身还没有实现。

### 4. `AgentLlmBridge` 仍未落地
问答模式目前仍是：

- 本地检索优先
- 超范围拒答

但还没有正式的受限 LLM 桥接层。

### 5. `FullAuto` 仍未完全 Skill 化
当前全自动主流程仍主要依托现有 `AiBotRuntime` legacy 自动执行逻辑，还没有彻底迁移到统一 Skill 驱动体系。

---

## 遇到的问题

### 问题 1：原有 `F8` 被用于面板开关，与计划冲突
之前 `AgentModePanel` 中 `F8` 的含义是“显示 / 隐藏模式面板”，而 `plan` 中 `F8` 明确对应 `QnA` 模式。

### 解决方案
将模式切换热键独立抽到：

- `ui.modeHotkeys.fullAuto`
- `ui.modeHotkeys.semiAuto`
- `ui.modeHotkeys.assist`
- `ui.modeHotkeys.qna`

并把面板显隐保留为独立热键：

- `ui.modePanelHotkey`，默认改为 `F4`

---

### 问题 2：`showModePanel` 不应误伤模式切换热键
如果直接沿用原判断逻辑，关闭模式面板显示时，也会把 `F5/F6/F7/F8` 模式切换一起禁用。

### 解决方案
将“模式切换热键”与“面板显示热键”拆开处理：

- 模式切换热键始终可用
- 只有面板显示/隐藏才受 `showModePanel` 控制

这样更符合计划对“快捷切换”的要求。

---

### 问题 3：聊天历史上限之前是硬编码
如果后续做多轮问答、半自动执行确认、聊天记录联动，硬编码会很快成为障碍。

### 解决方案
先在本阶段把它提到配置层，确保后续扩展不用再改 UI 组件内部常量。

---

## 解决方案与后续建议

### 下一阶段建议 1：补 `SemiAuto` 执行前确认
优先把半自动模式真正补成：

- 识别意图
- 生成候选动作
- 给出简短理由
- 等玩家确认后执行

这会让半自动模式更符合最初产品定义。

### 下一阶段建议 2：开始知识库 `core/custom` 重构
现在构建侧已经准备好了，下一步最适合直接推进：

- `GuideKnowledgeBase` 目录分层
- `KnowledgeValidator`
- `KnowledgeSchema`
- 自定义知识安全边界

### 下一阶段建议 3：补 `AgentLlmBridge`
在问答模式中加入一个真正受限的桥接层，让 LLM 只消费：

- 游戏上下文
- 本地知识检索结果
- 固定领域 prompt

并继续保留越界拒答。

### 下一阶段建议 4：回到 `FullAuto` 的 Skill 化改造
等配置和知识库基础设施补齐后，再把全自动模式从 legacy runtime 逐步迁到统一 Agent Skill 调度，会更稳。
