# Phase 12 — IntentParser 受限 LLM 兜底

## 已完成内容

### 1. 半自动模式已具备“本地规则优先 + 受限 LLM 兜底”双层解析
已更新：

- `aibot/Scripts/Agent/IntentParser.cs`
- `aibot/Scripts/Agent/AgentLlmBridge.cs`
- `aibot/Scripts/Agent/Handlers/SemiAutoModeHandler.cs`

本阶段严格对应 `claude_plan.md`：

- 第 19.2 条：LLM 只做受限意图识别
- 第 32.G.31：实现自然语言 → Skill 映射
- 第 47 条：确保技能集合固定、白名单化

此前 `IntentParser` 的问题是：

- 只靠本地关键词规则
- 一旦规则没命中，就直接视为 `Unknown`

这会让很多表达方式稍有变化的合法游戏指令无法识别。

本阶段补齐后，当前解析顺序变成：

1. 先走本地规则解析
2. 若已命中 Tool / Skill，则直接返回
3. 仅当本地规则失败时，才调用受限 LLM 兜底
4. LLM 只能返回白名单 Skill 名和有限参数字段
5. 若不满足约束，则仍然返回 `Unknown`

这样既保住了稳定性，也把可识别的自然语言范围向前推进了一步。

---

### 2. `AgentLlmBridge` 已新增 Skill 意图识别接口
当前 `AgentLlmBridge` 除了问答补答之外，已经新增：

- `RecognizeSkillIntentAsync(...)`

这个接口专门用于半自动模式的受限意图识别，而不是开放式对话。

它会接收：

- 用户原始输入
- 当前对局上下文
- 白名单 Skill 列表

然后要求模型只输出固定 JSON：

- `skillName`
- `reason`
- `parameters`

其中 `parameters` 仅允许以下有限字段：

- `cardName`
- `targetName`
- `potionName`
- `mapRow`
- `mapCol`
- `optionId`
- `itemName`
- `bundleIndex`
- `gridX`
- `gridY`
- `useBigDivination`

这和 `plan` 对“只能输出已知参数字段”的要求是一致的。

---

### 3. LLM 意图识别已被严格限制在白名单 Skill 集合内
本阶段最关键的安全点是：

- 模型只能从注册表中的 Skill 集合里选一个
- 返回的 `skillName` 必须与白名单中的精确技能名匹配
- 否则整个识别结果作废

也就是说，即使模型输出：

- 一个不存在的技能名
- 一个 Tool 名称
- 一个游戏外动作
- 一个“接近但不精确”的名字

最终也不会通过解析器验证。

这保证了 LLM 在这里是“受限分类器”，而不是“自由发挥的行动代理”。

---

### 4. `SemiAutoModeHandler` 已切换到异步解析入口
当前半自动模式不再直接调用：

- `IntentParser.Parse(...)`

而是改为调用：

- `IntentParser.ParseWithFallbackAsync(...)`

这样 `SemiAuto` 的主交互链路已经正式接上了：

- 本地规则
- 受限 LLM 兜底
- Skill 前置校验
- 执行前确认
- 最终执行

这让半自动模式比之前更接近 `plan` 原本定义的完整交互闭环。

---

### 5. LLM 输出参数会经过本地归一化处理
为了降低模型给出“近似名”时的落地风险，本阶段还对部分参数增加了本地归一化：

- `play_card` 会再次尝试匹配当前手牌中的卡牌名
- `use_potion` 会再次尝试匹配当前可用药水名

这样即使模型输出的是模糊叫法、缩写或不完全一致的名称，也有机会被本地状态进一步修正到真实对象上。

---

### 6. `IntentParser` 自身已负责释放桥接层资源
由于 `IntentParser` 现在内部持有一个可选的 `AgentLlmBridge`，本阶段同时补上了：

- `IntentParser.Dispose()`

并在：

- `SemiAutoModeHandler.Dispose()`

中完成释放，避免半自动模式频繁切换时积累不必要的资源占用。

---

### 7. 本阶段已完成编译验证
已执行：

- `dotnet build aibot\aibot.csproj -c Release`

结果：

- 构建成功
- 当前无新增编译错误

---

## 本阶段补齐的计划缺口
本阶段主要补齐：

- `claude_plan.md` 第 19.2 条：LLM 只做受限意图识别
- 第 32.G.31：自然语言 → Skill 映射进一步完善
- 第 47 条：技能集合固定、白名单化

这意味着当前 `SemiAuto` 不再完全依赖手写关键词规则，而是拥有了一个被严格约束的自然语言兜底层。

---

## 未完成内容

### 1. 统一会话管理仍未完成
当前只是把 LLM 兜底接到了 `IntentParser`，但 `plan` 第 18 条所说的：

- 历史裁剪
- 知识检索注入
- 统一结构化上下文
- 文本响应过滤

还没有在 `SemiAuto` / `QnA` 之间形成统一会话层。

### 2. Intent 识别目前只覆盖 Skill，不覆盖 Tool
本阶段严格按 `plan` 做的是“合法 Skill 映射”的 LLM 兜底，还没有把 Tool 型问题也纳入同一桥接层。

当前 Tool 仍然主要依赖本地规则。

### 3. 参数归一化仍是基础版
目前只对：

- 手牌卡牌名
- 药水名

做了额外归一化，后续还可以继续扩展到：

- 商店商品名
- 事件选项名
- 卡牌奖励候选名
- Crystal Sphere 坐标与操作描述

### 4. 模型返回的 `reason` 还未接入 UI 展示
当前解析结果中已经包含 `reason` 字段，但还没有进一步用于：

- 聊天窗口解释
- pending 确认提示增强
- 决策面板 explainability 展示

---

## 遇到的问题

### 问题 1：如果直接把 LLM 作为自由解析器，容易越界
模型天然会倾向于“尽量回答”，如果没有严格限制，很容易输出：

- 非白名单动作
- 非法参数
- 游戏外能力

### 解决方案
本阶段采用双层限制：

1. Prompt 只允许从白名单 Skill 中选择
2. 返回后在代码层再次校验 `skillName` 是否精确命中白名单

这样即使模型偏航，也不会直接进入执行层。

---

### 问题 2：半自动模式不应牺牲已有本地规则稳定性
如果直接改成“所有输入都先问 LLM”，反而会让现有稳定、快速的本地规则失去价值，并增加网络依赖。

### 解决方案
继续保持：

- 本地规则优先
- 只有本地失败时才走 LLM 兜底

符合 `plan` 对轻量与本地优先的原则。

---

### 问题 3：模型给出的对象名称不一定和当前游戏状态完全一致
即使模型识别出正确 Skill，也可能给出不精确的卡牌名或药水名。

### 解决方案
在 `IntentParser` 内继续用当前局面做一次本地归一化，把参数尽量映射到真实对象上，再交给执行层。

---

## 解决方案与后续建议

### 下一阶段建议 1：统一会话管理
现在 `QnA` 和 `SemiAuto` 都已经有 LLM 相关链路，最适合继续推进 `plan` 第 18 条，把：

- 历史裁剪
- 系统提示注入
- 上下文组织
- 安全过滤

逐步收敛到统一会话层。

### 下一阶段建议 2：开始知识库系统重构
问答和半自动的 LLM 桥接都已经成型，接下来如果切入：

- `KnowledgeValidator`
- `KnowledgeSchema`
- `GuideKnowledgeBase` 的 `core/custom` 重构

就能把 `plan` 中剩余的基础设施硬缺口继续往前推进。

### 下一阶段建议 3：把 LLM 意图 `reason` 接到确认 UI
当前待确认动作已经存在，如果把 LLM 给出的 `reason` 进一步接到确认面板和聊天窗口，会明显提升半自动模式的可解释性。