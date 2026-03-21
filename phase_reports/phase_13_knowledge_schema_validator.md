# Phase 13 — 知识库 Schema / Validator / 分层加载过渡实现

## 已完成内容

### 1. 新增知识库 schema 约束层
已新增：

- `aibot/Scripts/Knowledge/KnowledgeSchema.cs`

本阶段开始把 `claude_plan.md` 中“知识库必须可扩展，但仍要被严格限制在游戏域内”的要求落到代码层。

当前 `KnowledgeSchema` 已定义：

- 允许加载的 JSON 文件名白名单
- 预留支持的新实体文件名
  - `potions.json`
  - `powers.json`
  - `enemies.json`
  - `events.json`
  - `enchantments.json`
  - `game_mechanics.json`
- JSON / Markdown 的默认大小上限
- 单字符串字段长度上限
- 保留 Markdown 文件名集合

这样后续无论是内置知识库还是用户自定义知识库，都会先经过统一的文件类型约束，而不是“目录里放什么就读什么”。

---

### 2. 新增知识库 validator，拦截非游戏域内容
已新增：

- `aibot/Scripts/Knowledge/KnowledgeValidator.cs`

这是本阶段最关键的安全补丁之一。

当前 validator 已具备：

- JSON 文件名白名单校验
- JSON / Markdown 文件大小校验
- JSON 基础结构递归检查
- 过长字段名 / 过长字符串拒绝
- 拦截明显越界的 prompt / tool / shell 类文本标记
- Markdown 中禁止代码块与 URL

这意味着：

- 自定义知识库可以继续存在
- 但它不能轻易夹带 prompt 注入、工具调用暗示、外部链接或明显系统指令

从 Agent 边界控制的角度，这一步是把“知识库可自定义”与“能力边界不可扩张”分开处理的重要基础设施。

---

### 3. `GuideKnowledgeBase` 已改为兼容式分层加载
已更新：

- `aibot/Scripts/Knowledge/GuideKnowledgeBase.cs`

此前知识库加载逻辑完全基于旧的平铺结构：

- 顶层 `00_OVERVIEW.md`
- 顶层 `sts2_knowledge_base.md`
- 顶层 `characters_full.json` / `cards_full.json` 等

本阶段改造后，加载顺序已支持过渡式分层：

1. `custom/` 下的同名文件
2. `core/` 下的同名文件
3. 根目录旧平铺文件
4. `guides/` / `core/guides/` 等兼容路径

对 JSON 类知识数据，现在采用“按 key 合并”的方式：

- `custom` 可覆盖 `core`
- 旧平铺结构仍然兼容
- 不会因为目录尚未完全迁移就直接读挂

这正是本轮开发开始前确定的目标：

- 不一次性破坏已有知识库
- 先建立 `core/custom` 过渡层
- 再逐步迁移数据与模型

---

### 4. 运行时配置已接入知识库加载器
已更新：

- `aibot/Scripts/Core/AiBotRuntime.cs`

当前 `GuideKnowledgeBase` 已能够接收 `AiBotConfig`，并使用：

- `knowledge.enableCustom`
- `knowledge.customDir`
- `knowledge.maxCustomFileSize`

也就是说，知识库安全边界现在不再是完全硬编码，而是已经和配置系统打通。

---

### 5. Guide 模型已补充 `source` 元数据，并为后续扩展预留实体
已更新：

- `aibot/Scripts/Knowledge/GuideModels.cs`

当前已有实体：

- `CharacterGuideEntry`
- `BuildGuideEntry`
- `CardGuideEntry`
- `RelicGuideEntry`

已补充：

- `Source`

并预留新增模型：

- `PotionEntry`
- `PowerEntry`
- `EnemyEntry`
- `EventEntry`
- `EnchantmentEntry`
- `MechanicRule`

这些类型本阶段还没有完全接入检索/摘要链路，但已经先把 schema 能承载的数据形状补出来，为下一步知识扩展做准备。

---

### 6. 已为自定义知识目录补充说明文件
已新增：

- `sts2_guides/custom/README.md`

该文件说明了：

- 自定义知识应放置的位置
- 推荐使用的内置文件名
- 当前 validator 的限制条件
- 覆盖内置知识的基本规则

这一步虽然小，但很必要，因为后续用户要开始维护 `custom` 目录时，需要一个清晰入口，而不是只能读代码猜规则。

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

- 知识库 schema / validator 基础设施
- `core/custom` 的过渡式目录支持
- 用户自定义知识的基础安全边界
- 知识模型的后续扩展承载能力

这意味着当前项目的知识系统不再只是“把静态 guide 文件读进来”，而是开始具备：

- 可分层
- 可校验
- 可覆盖
- 可继续扩展

---

## 未完成内容

### 1. 新增实体尚未全部接入检索链路
虽然模型与 schema 已经预留：

- potion / power / enemy / event / enchantment / mechanic

但当前 `GuideKnowledgeBase` 仍主要在使用：

- 角色
- 构筑
- 卡牌
- 遗物
- Markdown guide

后续还需要把新实体真正接入：

- 检索
- 摘要
- QnA
- Assist / FullAuto 推理上下文

### 2. 还没有正式迁移到 `sts2_guides/core/`
本阶段做的是“兼容式支持”，不是“数据目录全面迁移”。

当前旧平铺结构仍然有效，这样可以保证当前项目继续可用；但后续仍应考虑把内置知识正式整理到：

- `sts2_guides/core/`

并根据需要补 `schema.json` 或更多分目录。

### 3. validator 目前以基础安全约束为主
当前已经能阻挡明显越界内容，但还没有做到：

- 更细粒度字段级 schema 校验
- 每类知识实体的必填字段验证
- 更丰富的 markdown 结构规范检查

这一部分适合放到后续知识系统增强阶段继续细化。

### 4. 还没有为知识库加载单独增加自动化测试
本阶段先保证：

- 编译通过
- 运行时接口兼容
- 当前目录结构不会被破坏

后续如果继续扩展 schema / custom merge 规则，建议为知识库加载器加一组小型单元测试或样例 fixture。

---

## 遇到的问题

### 问题 1：`plan` 期望的是 `core/custom` 分层，但当前仓库仍是旧平铺结构
如果直接按照最终形态改造，当前知识库很容易全部失效。

### 解决方案
本阶段采用兼容式加载策略：

1. 优先读 `custom`
2. 再读 `core`
3. 再回退到旧平铺结构
4. 同时对内容做 validator 过滤

这样既开始落地新架构，也不会破坏当前已有知识数据。

---

### 问题 2：允许用户自定义知识库，但不能让知识库把 Agent 带出游戏域
这是本项目从一开始就强调的核心约束。

### 解决方案
本阶段先加入基础 validator，把明显的越界载体挡在加载前：

- prompt 注入类文本
- tool / shell 指令暗示
- 外部 URL
- markdown 代码块

后续如果需要，还可以继续叠加更强的字段级约束。

---

### 问题 3：未来知识类型会增加，但当前运行链路主要只消费旧实体
如果现在就强行把所有新实体全面接入，会把本阶段范围拉得过大。

### 解决方案
本阶段先完成：

- schema 注册
- validator 支持
- 模型预留
- 分层加载骨架

先把基础设施搭起来，再在下一阶段继续把更多实体接入检索与推理。
