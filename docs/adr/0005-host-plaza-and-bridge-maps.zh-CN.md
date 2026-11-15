# ADR-0005：主机农场上的中央广场与声明式桥梁地图

* **状态：** 已接受
* **日期：** 2026-11-03

## 背景

16 处居所分布在主机农场（`Farm`，80×65）与三个串联的桥梁地图（`Pelican4Ever_Bridge_1..3`，35×30）之间，正如 `refactor/residence-entrances` 分支所提议的那样：

* **主机农场：** 五个自有出口，即四个传送门（西北、东北、西南、东南）和一条通向西侧的通道（`x=0`，`y=26..27`）通往桥梁 1。
* **每座桥梁：** 四个传送门（两个在北，两个在南）。其西侧边缘通往下一座桥梁，其东侧边缘则通往前一座桥梁或主机农场。

最初在原型阶段的实现用 `Game1.locations.Add` 在运行时创建桥梁，并依赖未同步的动态副本。这存在严重的局限：

1. 桥梁不会在 `SaveGame.loadDataToLocations` 中持久化，每个客户端都会生成自己那份脱节的副本。
2. 由于依赖 `Maps/BusStop` 的内存副本，任何地图重新加载都会重新插入指向公交站的原版 warp。
3. 几何结构基于各子系统之间不同步的经验阈值。
4. 各种例程只认名称 `"Farm"`。

## 决策

1. **声明式桥梁。** 每座桥梁 `Pelican4Ever_Bridge_N` 都在 `Data/Locations` 中有正式条目（`CreateOnLoad`，类型 `GameLocation`，室外并设置 `Nature.NIV.Outside = "true"`），并拥有自己的地图 `Maps/Pelican4Ever_Bridge_N`。它在 `AssetRequested`（`MapCloneHelper.CreateBridgeMap`）中确定性生成。其实例数量取决于 `FarmSlotCount`：$\lceil(\text{FarmSlotCount} - 4) / 4\rceil$，介于 0 到 3 座桥梁之间（以支持 4 到 16 个地块）。
2. **干净且确定性的桥梁地图。** 生成时：
   * 移除 `Warp` 属性以及模板中的原版 tile 动作（公交、售票机、站牌）。
   * 雕出传送门开口以及东西两侧通道。
   * 地图间的通行 warp 以声明方式写入地图的 `Warp` 属性（`PlazaTopologyVerifier.BuildPassageWarpString`），从而在任何重新加载时都能保持持久。
3. **Core 中的显式几何结构。**
   * `HostPlazaLayout`：描述广场中的每个位置（宽、高、南北路径深度以及串联的通道）。
   * `DoorCatalogValidator`：校验 `doors.json` 中 16 个传送门目录的完整性。
   * `SpatialWarpRouter`：依据方位边界（`PortalEdge`）以一一对应的方式路由到达与返回。
   * `PlazaTopologyVerifier`：提供用于 warp 格式化/解析、3×4 内部净空区域以及带安全回退的确定性返回解析的纯工具函数。
4. **广场的泛化。** 所有传送门机制、碰撞、欢迎消息以及 `/residencias` 命令都基于泛化后的「广场位置」概念（`HostPlazaLayout.IsPlazaLocation`）运行。

## 关于真实 XNB 地图的实测发现

在使用 Stardew Valley 1.6 的真实文件（`Content/Maps/Farm.xnb` 和 `Content/Maps/BusStop.xnb`）进行实现与验证期间，确定了以下精确参数：

1. **`BusStop` 中的 ViewportClamp：** 文件 `Maps/BusStop.xnb` 的物理尺寸为 `65×30`，但包含 Stardew Valley 的原生属性 `ViewportClamp = "10 0 35 30"`。这证明地图可用且规范的区域恰好是以 `(10, 0)` 为原点的 `35×30` 矩形，原版两端空白的列被舍弃。
2. **北侧路径深度（`NorthPathDepth`）：**
   * 在 `Farm` 中，北方的岩壁一直下探到 `y = 7..9`，需要 `NorthPathDepth = 10` 才能到达可行走的平原。
   * 在桥梁（`BusStop`）中，上方的树木与围栏带更窄，需要 `NorthPathDepth = 5` 才能与中央院子相连。
3. **主机农场上的西侧通道：** 通往桥梁 1 的西侧走廊位于 `x = 0..7, y = 26..27`。在 `AlwaysFront`（`x = 1..6, y = 23..27`）中清除静止的樱花树，以避免悬浮的树冠在视觉上阻挡通往桥梁的通道。
4. **利用原版公路（y = 22..25）：** 在桥梁（`BusStop`）中，自然的土/沥青车行道在 `y = 22..25` 行开放且连续地自东向西横贯。这些行被用作桥梁之间以及通往主机农场的天然东西通道（`ArrivalY = 24`），从而保留自然树景并避免在 `y = 13..15` 处打人工孔洞。木围栏只作为封闭终端边缘的收尾（例如桥梁 3 的西侧边缘）。

## 后果与验证

* **持久化与网络：** 桥梁和传送门会干净地序列化进存档，并在主机与客户端之间保持完全一致的同步。
* **面对缩容的韧性：** 如果管理员调低 `FarmSlotCount`（例如从 16 调到 4），主机农场会封闭其西侧通道，任何来自已停用桥梁的返回都会被安全地重定向到主机农场（`64, 16`），不会困住任何玩家。
* **自动化验证（`TASK-1401` 与 `TASK-1403`）：**
  * 通过 `Pelican4Ever.Tests` 中 793 项无外部依赖的单元测试套件验证。
  * 通过在 Stardew Valley 1.6 的真实 `.xnb` 资源（`Maps/Farm.xnb` 和 `Maps/BusStop.xnb`）上进行 **BFS（flood-fill）** 遍历完成拓扑验证，确认所有传送门（01..16）、返回格、3×4 净空区域以及东西连接通道 100% 可通行（Farm 中 3857 个可通行格，桥梁 1 中 695 个，桥梁 2 中 695 个，桥梁 3 中 684 个）。
