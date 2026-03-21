# Phase 14 — 知识实体接入与本地检索扩展

## 已完成内容

### 1. `GuideKnowledgeBase` 已真正加载新增知识实体
已更新：

- `aibot/Scripts/Knowledge/GuideKnowledgeBase.cs`

上一阶段已经补了：

- `KnowledgeSchema`
- `KnowledgeValidator`
- `GuideModels` 的新实体类型

但当时这些新增实体还只是“schema 能识别、模型能承载”，并没有真正进入知识库的运行时加载链路。

本阶段已把以下实体接入 `GuideKnowledgeBase.Load()`：

- `PotionEntry`
- `PowerEntry`
- `EnemyEntry`
- `EventEntry`
- `EnchantmentEntry`
- `MechanicRule`

也就是说，现在知识库在运行时不再只会读取：

- 角色
- 构筑
- 卡牌
- 遗物

而是已经开始具备承载更完整游戏知识图谱的能力。

---

### 2. 新增实体已具备统一查询入口
本阶段为 `GuideKnowledgeBase` 新增了统一查询方法：

- `FindPotion(...)`
- `FindPower(...)`
- `FindEnemy(...)`
- `FindEvent(...)`
- `FindEnchantment(...)`
- `SearchMechanicRules(...)`
- `SearchMarkdownSnippets(...)`

这些方法统一采用名称标准化和轻量评分匹配，而不是完全依赖精确命中。

这带来的价值是：

- 后续 QnA / Assist / 决策引擎都可以直接复用同一套知识入口
- 自定义知识覆盖后也仍能走同一查询链路
- 检索逻辑开始从“零散字符串处理”变成“知识库层的正式能力”

---

### 3. 药水摘要已从“仅 Markdown 片段”升级为“结构化条目优先”
本阶段还增强了：

- `BuildPotionSummary(...)`

此前药水摘要更多依赖 markdown 片段抽取，本阶段已改为：

1. 优先命中结构化 `PotionEntry`
2. 输出稀有度与描述/使用建议
3. 再补充 markdown 片段

这意味着当后续 `potions.json` 被补全后，运行时对药水的理解将不再只是“从 guide 里碰运气截一段字”，而是可以直接消费结构化知识。

---

### 4. `KnowledgeSearchEngine` 已升级为覆盖多类实体的正式本地检索器
已更新：

- `aibot/Scripts/Knowledge/KnowledgeSearchEngine.cs`

此前 `KnowledgeSearchEngine` 虽然已经存在，但覆盖范围主要还是：

- 卡牌
- 遗物
- 构筑
- 角色概要
- 核心机制摘要
- Markdown 片段

本阶段扩展后，当前检索器已经支持：

- 卡牌
- 遗物
- 构筑
- 药水
- 敌人
- Power / Buff / Debuff
- 事件
- 附魔
- 机制规则
- Markdown 片段补充

同时增加了：

- 更统一的 marker 提取
- 基于 query token 的回退匹配
- 返回来源信息 `source=core/custom`

这让它更接近 `plan` 里要求的“轻量、本地、分类化知识检索基础设施”，而不再只是一个问答模式里的小辅助类。

---

### 5. QnA 链路已自动获得更完整的本地知识命中面
当前 `QnAModeHandler` 原本就已经依赖：

- `KnowledgeSearchEngine.Search(...)`

因此本阶段虽然没有大改 `QnAModeHandler` 本身，但随着检索器增强，QnA 模式已经自动获得更完整的本地问答能力：

- 可以命中更多知识类别
- 可以返回更明确的实体条目
- 可以在结果里标识 `core/custom` 来源
- 对未来补齐的 `potions.json` / `powers.json` / `events.json` 等具备直接承载能力

也就是说，这次不是“又加一个模式功能”，而是把问答模式底层真正往 `plan` 定义的知识库系统上靠了一步。

---

### 6. 本阶段已完成编译验证
已执行：

- `dotnet build aibot\aibot.csproj -c Release`

结果：

- 构建成功
- 当前无新增编译错误

---

## 本阶段补齐的计划缺口
本阶段主要补齐：

- 第 11 节新增知识实体的运行时接入
- 第 12.3 条轻量本地检索能力的正式扩展
- 第 30 条问答模式的本地知识基础加强

这意味着当前项目已经从：

- “新增知识类型只是模型占位”

推进到了：

- “新增知识类型已进入运行时加载与本地检索链路”

这是知识系统真正开始服务 Agent 的关键一步。

---

## 未完成内容

### 1. 新知识文件本身仍未补齐
当前代码已经可以加载：

- `potions.json`
- `powers.json`
- `enemies.json`
- `events.json`
- `enchantments.json`
- `game_mechanics.json`

但仓库里这些数据文件本身还没有被完整补出来。

所以本阶段完成的是：

- 运行时承载能力
- 查询与检索接口

而不是知识数据本身的补全。

### 2. 机制规则仍主要依赖摘要 + 轻量规则搜索
当前 `MechanicRule` 已能被加载和搜索，但整体仍然是：

- 结构预留
- 检索入口就位
- 等待后续真实数据填充

距离 `plan` 里“伤害 / 格挡 / 回合结算公式级知识”还有明显差距。

### 3. Assist / FullAuto 还没有直接消费这些新实体
本阶段主要先接入知识库和 QnA 检索层。

后续仍可继续推进：

- Assist 推荐理由直接引用结构化 potion / enemy / event 知识
- FullAuto / heuristic 决策更直接消费机制规则
- `AgentLlmBridge` 在问答与推理中注入更细的结构化知识片段

### 4. 还没有把内置知识目录正式迁移到 `core/`
当前依然是兼容式目录策略：

- `custom/`
- `core/`
- 旧平铺结构

这保证了现有项目继续可用，但 `plan` 里的完整目录重构仍未彻底完成。

---

## 遇到的问题

### 问题 1：新增模型存在，但运行时并不会自动变得“更懂游戏”
如果只是增加实体类和 schema，知识系统仍然无法真正回答更多问题。

### 解决方案
本阶段直接把新实体接入：

- 加载
- 查询
- 本地检索
- QnA 使用路径

先让“知识结构扩展”真正变成“运行时能力扩展”。

---

### 问题 2：本地检索之前过度依赖 digest 与 Markdown 片段
这种方式虽然轻量，但一旦问题偏向实体查询，就不够稳。

### 解决方案
本阶段把本地检索分成两层：

1. 结构化实体命中优先
2. Markdown 片段作为补充

这更符合后续知识库不断补全时的演进方向。

---

### 问题 3：需要增强 QnA，但不能破坏已有 handler 结构
问答模式当前已经能工作，不适合做大范围重写。

### 解决方案
本阶段选择增强 `KnowledgeSearchEngine` 本身，而不是重写 `QnAModeHandler`。

这样做的好处是：

- 改动范围更小
- 风险更低
- 现有 QnA 调用路径自动获益
- 未来其他模式也可复用同一检索器
