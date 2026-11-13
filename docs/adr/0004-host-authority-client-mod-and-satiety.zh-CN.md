# ADR-0004：主机权威、客户端强制安装 Mod 与饱腹值预算

* **状态：** 已接受。第 3 点（每日饱腹值预算）已被 [ADR-0006](0006-session-energy-satiety-wellfed-spa.zh-CN.md) 取代。
* **日期：** 2026-10-29

## 背景

在 Stardew Valley 的网络层中，每个 `Farmhand` 都是自己那个 `Farmer`（`NetRoot<Farmer>`）的所有者，并在自己的客户端上执行 `Farmer.doneEating`、工具使用和对话。如果主机直接写入已连接客户端的 `farmer.modData`，就会与客户端的复制产生竞态条件。

同样，彻底移除食物带来的即时能量，会让厨房对那些希望做些不只是耗尽 270 点基础能量条的活跃玩家失去意义。

## 决策

1. **主机与客户端都必须安装 Mod（`ClientVersionGuardModule`）：** 主机在 `PeerContextReceived` 中验证客户端安装有 `Pelican 4Ever` 且 `Major.Minor` 版本相同，并在下一个 tick 踢出不满足该要求的客户端。
2. **主机上的唯一事实来源（`ServerWorldState`）：** 玩家状态（`PlayerRecord`）与门状态（`DoorAssignment`）仅存储在主机的 `ServerWorldState` 中。`GameLocation.modData` 仅作为供客户端读取的复制视图使用。
3. **每日饱腹值预算（`SatietyBudget`）：** 食物仍会返还即时能量以及生命值和增益，但受每日预算限制（`DailySatietyFraction = 0.5`，即每个真实日为基础能量条的 +50%），并辅以每天最多 2 次针对烹饪菜肴的被动回复增益，以及 Spa 的每日上限（`SpaDailyCap = 60`）。
4. **隔离的模块化注册（`IModule`）：** 所有纯逻辑都位于 `Pelican4Ever.Core` 中（不引用游戏本体，并用 xUnit 测试），每个 SMAPI/Harmony 子系统都实现 `IModule`，并在注册时具备错误隔离。

## 后果

* **积极面：** `Farmer.modData` 上没有竞态条件；厨房在受控的每日上限内保留了其战略价值；全部业务逻辑都能用 `dotnet test` 测试而无需打开游戏。
* **消极面：** 所有玩家都必须在客户端上安装该 mod 才能进入服务器。
