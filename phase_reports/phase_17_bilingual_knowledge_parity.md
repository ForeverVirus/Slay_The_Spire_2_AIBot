# Phase 17 — 中英检索一致性与知识库中文描述补齐

## 本阶段目标

本阶段继续严格遵循 plan 推进，重点解决一个非常具体且重要的问题：

- Agent 对英文输入的检索与解析能力已经较强
- 但中文输入在部分实体查询、QnA 检索、Semi Auto 指令解析中的命中率和反馈质量仍可能弱于英文

因此，本阶段的目标是把“中英输入一致性”做成系统能力，而不是只修补某一两个查询点。

同时，本阶段也继续遵守你的真实性要求：

- 不修改 `sts2/`
- 只修改 `aibot/` 与 `sts2_guides/`
- 所有知识数据只来源于真实源码与真实 `localization/` 文件
- 不猜测，不幻想，不补写无依据内容

---

## 已完成内容

### 1. 统一了知识检索层的中英匹配规则
本阶段对知识运行时做了统一收敛，让更多查询都通过同一套中英评分逻辑完成，而不是散落在各处做英文 `Contains` 匹配。

已更新：

- `aibot/Scripts/Knowledge/GuideKnowledgeBase.cs`
- `aibot/Scripts/Knowledge/GuideModels.cs`

主要调整包括：

- 为 `PotionEntry` / `PowerEntry` / `EnemyEntry` / `EventEntry` / `EnchantmentEntry` 增加 `descriptionZh`
- `FindCard` / `FindRelic` 改为统一评分匹配，纳入 `descriptionZh` 与 `descriptionEn`
- `FindPotion` / `FindPower` / `FindEnemy` / `FindEvent` / `FindEnchantment` 全部纳入中文描述参与匹配
- 新增 `FindBuilds(...)`，统一 build 查询的中英名称 / 摘要 / 要点 / 策略匹配
- 摘要构建优先选择可用中文文本，保证中文输出时信息密度不下降

这意味着：

- 用户输入中文实体名、中文描述片段、中文构筑关键词时
- 运行时不再显著劣于英文输入

---

### 2. 修正了 Intent Parser 的输入解析偏英文问题
已更新：

- `aibot/Scripts/Agent/IntentParser.cs`

本阶段把 `Semi Auto` 下若干核心命令对象解析从“偏英文字符串匹配”改成“知识驱动的双语评分匹配”。

重点包括：

- 手牌卡牌名解析改为先走知识库卡牌匹配，再映射当前手牌
- 药水名解析改为先走知识库药水匹配，再映射当前持有药水
- 新增内部评分逻辑以同时兼容：
  - 英文名
  - 中文名
  - slug
  - 中英文描述片段

这样做之后：

- 用户用中文说卡牌名 / 药水名
- 与用户用英文表达时，能够走同等级别的检索与解析路径

---

### 3. 补齐了查询工具与 QnA 输出层的双语展示
已更新：

- `aibot/Scripts/Knowledge/KnowledgeSearchEngine.cs`
- `aibot/Scripts/Agent/Tools/LookupBuildTool.cs`
- `aibot/Scripts/Agent/Tools/LookupCardTool.cs`
- `aibot/Scripts/Agent/Tools/LookupRelicTool.cs`

本阶段让展示层也与底层匹配规则保持一致，而不是“底层能匹配中文，上层却只展示英文”。

新增或增强的展示包括：

- card / relic 查询结果显示 `描述(ZH)` / `描述(EN)`
- build 查询结果显示 `摘要(ZH)` / `摘要(EN)` 与 `要点(ZH)` / `要点(EN)`
- potion / power / enemy / event / enchantment 的搜索结果加入中英描述输出

这一步的意义在于：

- 中文输入不只是“搜得到”
- 还要“回得好”
- 回答的内容密度和可读性不能低于英文路径

---

### 4. 基于真实 localization 补齐四类知识文件的 `descriptionZh`
已更新：

- `sts2_guides/core/potions.json`
- `sts2_guides/core/powers.json`
- `sts2_guides/core/events.json`
- `sts2_guides/core/enchantments.json`

这些文件此前已经有较真实的英文信息或基础实体结构，但中文描述字段不完整，导致：

- 中文检索时可用文本更少
- QnA 与 lookup 输出中文信息量不足
- 使用中文描述片段检索时命中率偏弱

本阶段已将这四类知识的 `descriptionZh` 全量补齐，并且全部来自真实 `localization/eng|zhs` 文件。

#### potions
来源：

- `localization/eng/potions.json`
- `localization/zhs/potions.json`

映射策略：

- 使用现有 `descriptionEn` 精确对应英文 `.description`
- 再回填相同 key 的中文 `.description`

结果：

- `64 / 64` 条药水拥有 `descriptionZh`

#### powers
来源：

- `localization/eng/powers.json`
- `localization/zhs/powers.json`

映射策略：

- 通过当前 `descriptionEn` 精确匹配英文 `.description` 或 `.smartDescription`
- 再提取相同 key 的中文描述回填 `descriptionZh`

结果：

- `17 / 17` 条 power 拥有 `descriptionZh`

#### events
来源：

- `localization/eng/events.json`
- `localization/zhs/events.json`

映射策略：

- 通过当前英文正文精确匹配 `.pages.INITIAL.description`
- 再提取对应 zhs 的初始事件正文作为 `descriptionZh`

结果：

- `58 / 58` 条 event 拥有 `descriptionZh`

#### enchantments
来源：

- `localization/eng/enchantments.json`
- `localization/zhs/enchantments.json`

映射策略：

- 通过英文 `.description` 精确对应后提取中文 `.description`

结果：

- `23 / 23` 条 enchantment 拥有 `descriptionZh`

---

## 本阶段验证结果

### 1. 字段覆盖率验证通过
已验证：

- `potions.total = 64, with_descriptionZh = 64`
- `powers.total = 17, with_descriptionZh = 17`
- `events.total = 58, with_descriptionZh = 58`
- `enchantments.total = 23, with_descriptionZh = 23`

即：

- 四类目标知识文件 `descriptionZh` 覆盖率均为 `100%`

---

### 2. 项目构建验证通过
已执行：

- `dotnet build aibot\aibot.csproj -c Release`

结果：

- 构建成功
- 无与本阶段改动相关的新增编译错误

---

## 本阶段解决的核心问题

### 问题 1：中文输入命中率低于英文输入
之前一些路径中，英文查询会更容易命中：

- card / relic / build 查询
- 半自动命令里的卡牌名与药水名解析
- QnA 搜索中的实体定位

### 解决方案
本阶段将这些入口尽量统一收敛到知识库评分匹配逻辑中，并把中文名、中文描述、中文摘要、中文要点与英文同等纳入匹配。

---

### 问题 2：知识文件缺少中文描述会导致中文检索天然吃亏
即使代码层支持双语匹配，如果目标知识文件里没有 `descriptionZh`，那么：

- 中文描述检索仍然弱
- 中文问答内容仍然薄
- 中文输入体验无法与英文真正等价

### 解决方案
本阶段基于真实 `localization/eng|zhs` 对四类核心知识文件做了全量中文描述补齐，并逐项验证覆盖率。

---

### 问题 3：必须保持真实性，不能靠手写翻译补洞
由于你已经明确要求“不要幻想”，所以本阶段不能采用：

- 人工意译
- 猜测性翻译
- 基于玩家常识补写描述

### 解决方案
本阶段全部采用“英文现有 canonical 条目 → 英文 localization 精确定位 → 中文 localization 同 key 回填”的方式，确保所有新增中文描述都可追溯到真实游戏文本。

---

## 对 plan 的推进意义

本阶段主要补齐了 agent 真正可用化的一项关键要求：

- 不只是有知识库
- 还要让中文玩家输入与英文玩家输入获得同等级别的检索质量

这对以下能力都有直接帮助：

- `Assist` 模式的推荐解释
- `QnA` 模式的本地知识问答
- `Semi Auto` 模式的自然语言解析
- lookup tools 的结构化查询反馈

也就是说，这一阶段推进的是“Agent 面向双语用户的可用性质量”，而不是单纯补充字段数量。

---

## 当前仍未完成的部分

### 1. 并非所有知识实体都已经拥有同等丰富的中文结构化字段
本阶段主要完成了：

- cards / relics 的 `descriptionZh` 闭环（此前已完成）
- potions / powers / events / enchantments 的 `descriptionZh` 补齐
- 代码侧统一双语匹配入口

但后续如果还要继续提高中文检索质量，仍可以继续关注：

- `tipsZh`
- `strategyZh`
- 更细的 move / mechanic / encounter 级摘要字段

前提仍然是：必须有真实来源。

### 2. 中文输入一致性仍需在真实游戏场景中继续压测
本阶段已经完成：

- 代码改造
- 字段补齐
- 编译验证

但后续仍建议继续在以下场景中做实战验证：

- 中文问卡牌 / 遗物 / 构筑
- 中文半自动命令输入
- 中文问事件 / 药水 / power 效果
- 中文描述片段模糊检索

---

## 结论

本阶段已经把“中文输入不能显著弱于英文输入”从一个口头要求，推进成了实际落地的系统能力：

- 底层检索规则双语化
- 输入解析双语化
- 查询展示双语化
- 核心知识文本字段双语补齐
- 且所有新增数据均可追溯到真实源码与 localization

这使当前 agent 更接近“真正能服务中文玩家的 Slay the Spire 2 专属 Agent”。
