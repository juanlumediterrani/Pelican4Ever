# ADR-0003：4 个声明式农场地块（`Data/Locations`）配合标准模板

* **状态：** 已接受
* **日期：** 2026-10-29

## 背景

最初的设计计划按需用 `new Farm(mapPath, $"Farm_{player.UniqueMultiplayerID}")` 实例化位置，并允许每位玩家在游戏的 8 种农场类型中自行选择。

对 `Stardew Valley.dll` 的检查揭示了引擎的三个限制：
1. 在 `SaveGame.loadDataToLocations` 中，引擎会在写入 XML 数据之前根据 `Data/Locations`（`CreateOnLoad`）重建位置。那些在运行时创建但没有 `Data/Locations` 条目的位置，在重新加载时有可能被丢弃。
2. 每种农场类型的特定机制（林地 forage、山丘矿物、荒野怪物）查询的是全局字段 `Game1.whichFarm`，而不是 `Farm` 实例。
3. `GameLocation.CanBuildHere()`（第 2459 行）要求 `Game1.multiplayer.isAlwaysActiveLocation(this)` 为 `true`，这会把每个农场同步给所有已连接的客户端。

## 决策

1. 将 MVP 限定为 **4 名玩家（`FarmSlotCount = 4`）**，全部使用 **标准农场模板（`Maps\Farm`）**。
2. 通过 `Helper.Events.Content.AssetRequested` 在 `Data/Locations` 中以声明方式注册 4 个固定位置 `Pelican4Ever_Farm_01` 到 `Pelican4Ever_Farm_04`，并设置 `CreateOnLoad` 与 `AlwaysActive = true`。
3. 当在广场上认领住宅传送门时（最初规划在镇上，后来通过 ADR-0005 整合到中央广场与桥梁），为玩家分配其中一个固定地块，并把其 `Cabin` 重新安置到该地块内。

## 后果

* **积极面：** 保存与加载 100% 兼容 `SaveGame.loadDataToLocations`；Robin 允许建造建筑而无需额外补丁；对 4 名玩家而言 `AlwaysActive = true` 的网络流量微乎其微。
* **消极面：** 在 MVP 中所有玩家共享同一套农场地图设计（标准），且默认上限为 4 位所有者。
