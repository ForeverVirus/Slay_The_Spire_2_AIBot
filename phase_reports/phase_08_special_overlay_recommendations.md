# Phase 08 — Assist 特殊选择场景推荐

## 已完成内容

### 1. Assist 推荐层已补齐三类特殊 overlay 场景
已更新：

- `aibot/Scripts/Ui/AgentRecommendOverlay.cs`

本阶段继续扩展了辅助模式的推荐覆盖范围，新增支持：

- 奖励领取推荐
- Bundle 选择推荐
- Crystal Sphere 推荐

这样一来，当前 `Assist` 推荐层已经覆盖了大部分高价值决策节点，包括常规房间、地图、商店、战斗，以及多个 overlay 选择界面。

---

### 2. 奖励领取推荐已接入 `ChooseRewardAsync`
当前当 `NRewardsScreen` 打开时，推荐层会：

1. 收集当前可交互的 `NRewardButton`
2. 根据玩家药水槽状态获取推荐上下文
3. 调用 `DecisionEngine.ChooseRewardAsync()`
4. 将“推荐”标签贴到被选中的奖励按钮上

这样辅助模式就能给出当前奖励界面的最优领取建议，而不会自动点击。

---

### 3. Bundle 推荐已接入 `ChooseBundleAsync`
当前当 `NChooseABundleSelectionScreen` 出现时，推荐层会：

1. 收集当前所有 `NCardBundle`
2. 构造与自动流程一致的 `AiCardSelectionContext`
3. 调用 `DecisionEngine.ChooseBundleAsync()`
4. 将推荐标签贴到对应 Bundle 上

这样可以保证：

- Assist 模式的 Bundle 推荐与全自动逻辑一致
- 推荐层不需要再维护一套独立的 Bundle 评分逻辑

---

### 4. Crystal Sphere 推荐已接入 `ChooseCrystalSphereActionAsync`
当前当 `NCrystalSphereScreen` 打开时，推荐层会：

1. 如果可以直接 Proceed，则优先给 Proceed 按钮贴推荐
2. 否则读取当前仍隐藏的 `NCrystalSphereCell`
3. 通过反射获取 minigame entity
4. 调用 `DecisionEngine.ChooseCrystalSphereActionAsync()`
5. 将推荐标签贴到对应格子上
6. 在理由中补充“大范围占卜 / 小范围占卜”提示

这让 Crystal Sphere 这种特殊小玩法界面也纳入了辅助模式推荐体系。

---

### 5. 推荐层签名去重已同步扩展
为了避免 overlay 场景反复重复刷新，本阶段继续扩展了签名机制。当前新增签名覆盖：

- rewards
- bundle
- crystal sphere

只要界面结构没有变化，就不会重新计算推荐，从而减少抖动和无意义计算。

---

### 6. 第八阶段已完成编译验证
已执行：

- `dotnet build aibot\aibot.csproj -c Release`

结果：

- 构建成功
- 当前无新增编译错误

---

## 未完成内容

### 1. Assist 模式已接近完整，但仍有少量边角场景未覆盖
目前尚未补齐的主要是：

- 卡牌选择类细分场景（升级/移除/变形等 grid 选择）
- 奖励界面中的替代选项细分提示
- 更细粒度的药水使用时机提示

### 2. 推荐层仍然只展示单个最佳项
当前每个场景仍然只显示一个 `推荐` 标签，不展示：

- 次优候选
- 排序分数
- 风险/收益差异标签

### 3. 推荐理由仍以 tooltip 为主
目前推荐的理由仍是轻量文本，主要放在 tooltip 中，还没有更强的 explainability UI。

---

## 遇到的问题

### 问题 1：特殊 overlay 场景的目标节点类型各不相同
本阶段新增的三个场景分别对应：

- `NRewardButton`
- `NCardBundle`
- `NCrystalSphereCell` / `NProceedButton`

它们的 UI 结构和点击入口都不同，不能直接复用之前卡牌/遗物/地图的目标定位逻辑。

### 解决方案
继续保持统一策略：

- 每种场景内部独立完成“决策对象 → UI 控件”的映射
- 最终统一通过 `ReplaceWithSingleBadge()` 贴标签

这样既能适配差异，又不破坏推荐层整体结构。

---

### 问题 2：Crystal Sphere 的核心数据不直接暴露
推荐决策依赖 `CrystalSphereMinigame`，但当前屏幕层没有直接公开可用属性。

### 解决方案
本阶段继续沿用了自动流程中的做法：

- 通过反射读取 `NCrystalSphereScreen` 内部 `_entity`
- 将其转换为 `CrystalSphereMinigame`
- 再调用现有决策接口

这样可以复用既有逻辑，同时避免额外修改 `sts2/`。

---

### 问题 3：Proceed 按钮和格子推荐需要区分优先级
在 Crystal Sphere 中，如果已经可以直接 Proceed，就不应该继续给隐藏格子贴推荐，否则会造成误导。

### 解决方案
当前逻辑按优先级处理：

1. 先检查 Proceed 是否可用
2. 若可用，则直接推荐 Proceed
3. 否则才计算格子推荐

这样和实际自动流程更一致。

---

## 解决方案与后续建议

### 下一阶段建议 1：补卡牌选择类 grid 场景推荐
这会让辅助模式继续向“几乎全流程推荐”靠近，尤其适用于：

- 升级
- 移除
- 变形
- 附魔
- 奖励网格选择

### 下一阶段建议 2：把推荐写入决策日志
当前推荐层主要是视觉提示，后续可以把推荐 trace 也统一写入 `AiBotDecisionFeed`，方便：

- 调试
- 回顾
- 与聊天窗口联动

### 下一阶段建议 3：加入更完整的 explain UI
例如：

- 点击推荐标签弹出短解释
- 在聊天窗口输出“为什么推荐这个”
- 决策面板中显示当前推荐来源和理由

### 下一阶段建议 4：考虑统一 Recommend Provider 抽象
随着 Assist 场景越来越多，后续可以把推荐层中的各场景推荐逻辑继续抽象成独立 provider，使 UI 与推荐决策映射进一步解耦。
