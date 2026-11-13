# `ARCHITECTURE.md` — 技术架构文档（*Pelican 4Ever*）

## 1. 技术栈与拓扑结构

* **目标平台：** Stardew Valley **1.6.15**（64 位）运行在 Linux（`x86_64`）上，使用 **SMAPI 4.1+**（已在 SMAPI 4.5.2 上测试）与 **Harmony 2.x**（`Lib.Harmony`）执行。
* **专用服务器（原生 1.6 + 容器）：**
  * Stardew Valley 1.6.15 原生支持自动化主机托管（`Game1.HasDedicatedHost`、`Game1.IsDedicatedHost`、`Farmer.IsDedicatedPlayer` 以及通过 `player.team.hasDedicatedHost.Value = true` 启用的 `StardewValley.Network.Dedicated.DedicatedServer` 类）。
  * 部署依赖 **JunimoServer**（或使用 `xvfb` + `noVNC` 的 Linux Docker 容器），将原生 `DedicatedServer` 机器人与 `Pelican 4Ever` 的连续时钟规则结合起来（防止 `DedicatedServer.Tick()` 在 `onlineIds.Count == 0` 时暂停世界）。
* **客户端-服务器拓扑与信任模型：**
  * **同一个 mod 包（`Pelican4Ever.dll` + `Pelican4Ever.Core.dll`）在主机和客户端上都是必需的**（`ClientVersionGuardModule` 会在 `PeerContextReceived` 时踢出任何没有 SMAPI 或 `Major.Minor` 版本不同的客户端）。
  * **主机（`Context.IsMainPlayer`）= 唯一事实来源**，负责时钟、日历、传送门/居所分配以及玩家注册（`ServerWorldState`）。
  * **客户端（`Context.IsLocalPlayer`）= 界面执行器与本地玩家**，负责进食时的能量削减（`Farmer.doneEating`）、在他人工地上的本地工具锁定、传送门与床铺对话。所有共享状态的变更都通过 `Helper.Multiplayer.SendMessage` 向主机提出请求。

---

## 2. 解决方案结构（.NET）

解决方案分为三个项目，以完全将纯领域逻辑与 `Stardew Valley.dll` 的依赖分离，并能在毫秒级内运行单元测试：

```text
Pelican4Ever.sln
├── local.props.example                  # Plantilla de ruta local a Stardew Valley (copiar a local.props)
├── Pelican4Ever.Core/                   # Lógica pura (.NET 6, CERO referencias a Stardew/SMAPI/Harmony)
│   ├── Config/
│   │   └── ServerConfig.cs              # Configuración + validador determinista Validate()
│   ├── Time/
│   │   ├── GameClock.cs                 # Conversión hora real -> timeOfDay (0600..2550) + NightHold
│   │   ├── ServerCalendar.cs            # Calendario perpetuo derivado de CalendarEpoch y CatchUpPlan
│   │   └── ServerTime.cs                # Resolución de TimeZoneInfo IANA y reloj UTC -> Local
│   ├── Players/
│   │   ├── PlayerRecord.cs              # Registro autoritativo por jugador (días en TotalDays)
│   │   └── SleepPolicy.cs               # Derivación de SleepState, evaluación de Rollover y cargo médico
│   ├── Economy/
│   │   └── SatietyBudget.cs             # Presupuesto diario de Saciedad (comida) y tope del Spa
│   └── World/
│       ├── ServerWorldState.cs          # Estado persistido en el Host + migración de esquema (v2)
│       ├── HostPlazaLayout.cs           # Plaza Central y cadena de puentes escalables (TASK-1001)
│       ├── DoorCatalogValidator.cs      # Validación de geometría y portales en la plaza (TASK-1002)
│       ├── SpatialWarpRouter.cs         # Enrutamiento espacial, llegadas y retornos (TASK-1102)
│       ├── TerrainTileClassifier.cs     # Clasificación de terreno y autotiling de caminos (TASK-1103)
│       ├── ResidenceExits.cs            # Puntos de salida por plantilla de granja (TASK-1104)
│       ├── PlazaCatalogFormatter.cs     # Formateo puro de /residencias para chat y consola (TASK-1304)
│       └── PlazaTopologyVerifier.cs     # Verificación topológica, warps y áreas de despeje (TASK-1401/1403)
├── Pelican4Ever/                        # Mod SMAPI (.NET 6, referencia a Pelican4Ever.Core)
│   ├── manifest.json
│   ├── ModEntry.cs                      # Validación de config y registro aislado de módulos (IModule)
│   ├── Framework/
│   │   ├── IModule.cs                   # Contrato IModule { Name, Side, Register(ModContext) }
│   │   ├── ModContext.cs                # Contenedor inyectado (Helper, Monitor, Config, Harmony, WorldState)
│   │   └── MapCloneHelper.cs            # Recorte determinista de puentes (35×30) y anexos
│   ├── Models/
│   │   └── ModDataKeys.cs               # Claves de GameLocation.modData (vista replicada de solo lectura)
│   └── Modules/
│       ├── PersistenceModule.cs         # Carga, migración y guardado de ServerWorldState en el Host
│       ├── ClientVersionGuardModule.cs  # Verificación de versión del mod al conectar un par
│       ├── StatusCommandModule.cs       # Comando de diagnóstico p4e_status
│       ├── RealTimeClockModule.cs       # (Iteración 1) Sincronización 1:1, NightHold y bloqueo de pausa
│       ├── DailyRolloverModule.cs       # (Iteración 1) Avisos, disparo de NewDay nativo y SleepPolicy
│       ├── BedLogoutModule.cs           # (Iteración 1) Intercepción de Sleep_Yes y mensaje al Host
│       ├── FarmSlotModule.cs            # (Iteración 2) Registro en Data/Locations, Cabins, plaza y portales
│       ├── EconomyModule.cs             # (Iteración 3) SatietyBudget, Spa, precios y minas 12h
│       ├── GroundSitModule.cs           # Sentarse en el suelo (tecla GroundSitKey), pose sincronizada entre jugadores
│       └── WorldLifeModule.cs           # (Iteración 4) Micro-wandering de NPCs y festivales
└── Pelican4Ever.Tests/                  # Suite xUnit (.NET 8) sobre Pelican4Ever.Core (1.300+ tests)
```

---

## 3. 数据模型与唯一事实来源

### 3.1. 主机上的权威状态（`ServerWorldState`）

为避免在 `Farmer.modData`（其 `NetRoot` 属于每个 `Farmhand` 的客户端）上出现竞态条件，所有玩家的持久化信息都存放在 `ServerWorldState` 中（通过 `Helper.Data.WriteSaveData("Pelican4Ever.ServerWorldState", state)` 序列化到主存档中）：

* **`Players`（`Dictionary<long, PlayerRecord>`）：**
  * `LastActiveGameDay`、`RestedOnGameDay`（以 `Game1.Date.TotalDays` 表示，绝不使用 `dayOfMonth`）。
  * `LastOutcome`（`Untouched` | `Rested` | `Penalized`），`SleepPolicy.GetState` 由此推导出 `Awake`、`Rested`、`OfflineSafe` 或 `OfflinePenalized`。
  * 持续衰减的饱腹值（`Satiety`、`LastSatietyUpdateUtc`）、进行中增益的结束时间（`WellFedUntilUtc`、`SpaBuffUntilUtc`）以及按游戏日自动重置的每日计数（`SatietyGameDay`、`WellFedBuffsUsed`、`SpaBuffsUsed`）。客户端通过 `SatietyReport` 消息将这些数据告知主机（ADR-0006）。
* **`Doors`（`Dictionary<string, DoorAssignment>`）：** 将镇上的每个 `DoorId` 与其 `FarmSlotId`（`Pelican4Ever_Farm_01..04`）、`OwnerId`、`AccessPolicy`（`Locked`、`Friends`、`Public`）以及 `Friends` 集合关联起来。

### 3.2. 在位置中的复制视图（`GameLocation.modData`）

只有位置（由主机控制）会在 `location.modData` 中暴露元数据，作为供客户端界面和本地工具锁定使用的只读视图：
* `Pelican4Ever.OwnerId`（`long`）
* `Pelican4Ever.AccessPolicy`（`Locked` | `Friends` | `Public`）

---

## 4. 子系统技术规范（已针对 Stardew Valley 1.6.15 验证）

### 4.1. 子系统 1：1:1 时钟、`NightHold` 与原生日切

#### A. Stardew Valley 1.6.15 中的时钟工作原理（反编译所得）
1. 在 `Game1.UpdateGameClock(GameTime time)`（第 6050–6090 行）中，当 `shouldTimePass() && !IsClient` 时，主机会累加 `gameTimeInterval += time.ElapsedGameTime.Milliseconds`。
2. 当 `gameTimeInterval > realMilliSecondsPerGameTenMinutes + ExtraMillisecondsPerInGameMinute * 10`（默认 `7000 ms`）时，它会调用 `Game1.performTenMinuteClockUpdate()`。
3. 在 `performTenMinuteClockUpdate()`（第 5882–6024 行）中，主机累加 `timeOfDay += 10`，归一化分钟数（`% 100 >= 60`），应用 `timeOfDay = Math.Min(timeOfDay, 2600)`，执行 `location.performTenMinuteUpdate(timeOfDay)`、`MineShaft.UpdateMines10Minutes(timeOfDay)`，并通过 `netWorldState.Value.UpdateFromGame1()` 将状态传播给客户端。
4. 当客户端在 `Multiplayer.receiveWorldState`（第 506 行）中收到 `NetWorldState` 并检测到 `timeOfDay != Game1.timeOfDay` 时，它会在本地执行自己的 `Game1.performTenMinuteClockUpdate()`。
5. 在 `Game1.Update`（第 6452 行）中：`if (timeOfDay >= 2600 || player.stamina <= -15f)` 会触发 `player.startToPassOut()`。

#### B. 时间补丁设计（`RealTimeClockModule`）
* **借助 `NightHold` 不会因时间流逝而晕倒：**
  * `GameClock.Read` 将 `06:00 → 0600` 映射到 `01:50 → 2550`。在真实的 `01:50` 与 `06:00` 之间，它返回 `TimeOfDay = 2550, IsNightHold = true`。
  * 由于 `Game1.timeOfDay` **永远不会达到 `2600`**，`Game1.Update`（第 6452 行）中的 `timeOfDay >= 2600` 条件永远不会因时钟而触发，而因体力耗尽（`player.stamina <= -15f`）和因生命值（`health <= 0`）导致的晕倒仍然以 100% 原生方式正常工作，无需触碰 `Farmer.passOutFromTired`。
* **由主机主导的 1:1 同步：**
  * 将 `Game1.realMilliSecondsPerGameTenMinutes = 600_000`（10 分钟真实时间）固定下来，使 `UpdateGameClock` 的环境光插值（第 6059 行和第 6065 行）使用真实除数，并令 `gameTimeInterval = clockReading.MillisecondsIntoSlot`。
  * 在 `UpdateTicked`（主机）中，会锁定自由累加器并计算 `GameClock.TicksToReach(Game1.timeOfDay, clockReading.TimeOfDay)`：当真实时钟跨过 10 分钟的整数倍（且当前不处于 `IsNightHold`）时，主机会精确调用所需步数次的 `Game1.performTenMinuteClockUpdate()`。客户端通过 `NetWorldState` 收到变更并自动执行其 tick。
* **连续时间，无暂停：**
  * 对 `Game1.shouldTimePass` 打补丁（在专用服务器模式下，防止 `DedicatedServer.Tick` 设定 `Game1.netWorldState.Value.IsPaused = true`（在没有玩家连接时），并防止各个菜单/事件单独暂停 `IsTimePaused`）。

#### C. `02:00 AM` 的原生每日日切（`DailyRolloverModule`）
* 在真实的 `02:00:00`（`CalendarDayChangeTime`），主机会用 `ServerCalendar` 同步目标日期，并强制执行原生每日切换（`Game1.NewDay(0f)` / 自动睡眠的 `ReadyCheck`），让 `_newDayAfterFade()` 去执行整个换日流程和 `SaveGame.Save()`。
* 在真实的 `06:00`（`DayStartTime`）启动游戏时钟和 NPC 日程：深夜的日期变更不会提前白天的日常活动。
* 在关闭之前会提醒玩家（`RolloverWarningMinutes`，默认 10），生成带 SHA-256 签名的备份，并以有序方式关闭对端；如果备份或断开连接未通过校验，这一天 **不会** 推进。
* 在 `DayStarted` 中：
  * 对刚刚结束那一天的每个 `PlayerRecord` 求值 `SleepPolicy.Evaluate`。
  * 对相应的 `Farmer` 应用 `SleepPolicy.WakeUpEnergy` 和 `SleepPolicy.MedicalFee`（使用独立钱包 `useSeparateWallets`）。
  * 用 `ServerCalendar.PlanCatchUp` 验证存档日期是否与由 `CalendarEpoch` 推导出的真实日期一致。

---

### 4.2. 子系统 2：中央广场、声明式桥梁与私人居所（`FarmSlotModule`）

1. **在 `Data/Locations` 中的声明式注册（加载时无孤立位置）：**
   * 在 `SaveGame.loadDataToLocations` 中，Stardew Valley 1.6 根据 `Data/Locations`（`CreateOnLoad`）创建位置。
   * 通过在 `"Data/Locations"` 上挂钩 `Helper.Events.Content.AssetRequested`，该 mod 以声明方式注入：
     - 私人居所 `Pelican4Ever_Farm_01` .. `Pelican4Ever_Farm_16`（依据 `FarmSlotCount`），带有 `CreateOnLoad = new CreateLocationData { MapPath = template.MapAssetKey, Type = "StardewValley.Farm, Stardew Valley", AlwaysActive = true }`。
     - 依据 `FarmSlotCount`（`ceil((FarmSlotCount - 4) / 4)`）确定性计算出的活动桥梁地图 `Pelican4Ever_Bridge_1..3`：类型为 `GameLocation`，位于室外（`Nature.NIV.Outside = "true"`），绝不依赖 `Game1.locations.Add`，也不依赖内存中易失的桥梁。
   * 引擎说明（已在 `GameLocation.cs` 第 2459 行验证）：`CanBuildHere()` 要求 `Game1.multiplayer.isAlwaysActiveLocation(this)` 为 `true`。对于服务器上的农场主，我们保持 `AlwaysActive = true`，这保证了与 Robin、建筑和动物完全兼容，而网络开销微乎其微。

2. **桥梁地图的确定性生成（`MapCloneHelper.CreateBridgeMap`）：**
   * 每个桥梁地图 `Pelican4Ever_Bridge_N`（35×30）都通过裁剪 `Maps/BusStop` 中央可见区域 `(10, 0, 35, 30)` 生成。
   * 生成器会清除损坏的巴士和中央灌木，封住通往虚空的周边（传送门开口和活动通道除外），在旧公路上设置围栏，并雕出东西两侧通道，将连续的声明式 warp 写入 `map.Properties["Warp"]`。

3. **纯领域架构（`Pelican4Ever.Core/World/`）：**
   * **`HostPlazaLayout`：** 定义广场的拓扑结构与尺寸（80×65 的 `Farm` 和 35×30 的桥梁 `Pelican4Ever_Bridge_1..3`）、依据 `FarmSlotCount` 的桥梁数量，以及东西串联的通道及其双向到达坐标。
   * **`DoorCatalogValidator`：** 校验 `doors.json` 中 16 个住宅传送门的目录（明确的 `PortalEdge` 边界、广场归属、边距以及相邻信箱）。
   * **`SpatialWarpRouter`：** 依据传送门的方位边界（`North`、`South`、`West`、`East`）和农场模板，确定性地计算居所的到达格与朝向、面对开口返回广场的格，以及触发格（`IsPortalTriggerTile`）。
   * **`TerrainTileClassifier`：** 对地形进行分类，以便程序化地雕凿并自动铺设石板路（`stepping_stones`），同时尊重树冠与坚实地面。
   * **`ResidenceExits`：** 确定每个居所模板通向外界的出口点。
   * **`PlazaCatalogFormatter`：** 为聊天与控制台的 `/residencias` 命令整洁地格式化居所列表和地块状态。
   * **`PlazaTopologyVerifier`：** 提供纯工具函数，用于计算 3×4 的内部净空区域（`GetInteriorClearanceTiles`）、序列化与解析 `Warp` 字符串（`BuildPassageWarpString`、`ParseWarpProperty`）、安全地解析返回广场的目标（`ResolvePlazaReturnTarget`），以及拓扑连通性的 BFS 验证（`IsConnected`）。

4. **居所、传送门与信箱的关联：**
   * 每个居所 `Pelican4Ever_Farm_0X` 容纳与所有者 `Farmhand` 关联的 `Cabin`，并在 `location.modData` 中发布 `OwnerId` 和 `AccessPolicy`。
   * 16 个住宅传送门分布在主机农场（4 个传送门）与各桥梁（每座桥 4 个传送门）之间。
   * 当走到朝向地图边缘的传送门时，如果地块空闲，该 mod 会打开建地对话；如果已被占用且允许访问，则会流畅地传送至私人居所。
   * 每个传送门都配有一个独立的相邻信箱（`TownMailboxModule`），便于在进出居所时管理个人邮件而互不干扰。

---

### 4.3. 子系统 3：能量、饱腹值、价格与 12 小时矿井（`EconomyModule`）

1. **在 `Farmer.doneEating` 中的饱腹值（第 8851–8945 行，ADR-0006）：**
   * 在 `Farmer.doneEating()` 中，引擎累加 `Stamina = Math.Min(MaxStamina, Stamina + @object.staminaRecoveredOnConsumption())` 与 `health = Math.Min(maxHealth, health + @object.healthRecoveredOnConsumption())`，并应用 `GetFoodOrDrinkBuffs()`。
   * `Prefix` 保存 `float prevStamina = __instance.Stamina`。
   * `Postfix` 先应用持续衰减（`PlayerRecord.UpdateSatietyDecay`），计算 `int requestedGain = (int)Math.Round(__instance.Stamina - prevStamina)`，查询 `SatietyBudget.AllowedGain(requestedGain, record.Satiety, capacidad)`，然后设定 `__instance.Stamina = prevStamina + allowedGain`（兼顾 `Stardrop (O)434` 等特殊情况）。用 **任何食物**（不仅仅是做好的菜肴）进食，且当饱腹值达到阈值 `WellFedSatietyThresholdFraction`（1/4）并且 `MaxDailyWellFedBuffs` 仍有剩余时，会激活「吃饱」增益（`PlayerRecord.TryActivateWellFed`），允许在 60 分钟内通过坐着恢复能量（`SittingRegenLogic`，`1 点 / 5 秒`）。
   * 在 `OneSecondUpdateTicked` 中，在水中静止 30 秒每天会触发一次 Spa 增益（`PlayerRecord.TryActivateSpaBuff`），在 120 分钟内分配 `120` 能量和 `120` 生命值（`PassiveRegenLogic`）。
2. **按 12 小时班次的矿井持久化（`06:00` 与 `18:00`）：**
   * 在 `MineShaft.UpdateMines10Minutes`（第 4788 行）中，引擎在每个 10 分钟 tick 调用 `MineShaft.clearInactiveMines()`（私有静态，第 4704 行），卸载所有没有玩家的楼层。在换日时（`Game1.cs` 第 7925 行）会调用 `MineShaft.clearActiveMines()`。
   * 对 `MineShaft.clearInactiveMines` 应用一个 `Prefix`，在正常班次期间返回 `false`；在真实的 `06:00` 和 `18:00`，主机会对没有活跃玩家的楼层执行清理（并通过 HUD 通知新的矿工班次）。

---

### 4.4. 子系统 4：城镇生活与节日（`WorldLifeModule` — Post-MVP）

* **NPC 微漫游（`NpcMicroWanderController`）：** 在主机上，当 `NPC` 完成其定时的 `PathFindController` 后，会记录其锚定格，并每隔 45–90 秒真实时间以 `speed = 1` 走到半径 $\pm 2$ 内一个既不是 `Warp` 也不发生碰撞的空格。
* **下午/夜间节日（`FestivalInstanceManager`）：**
  * 通过在 `AssetRequested` + `InvalidateCache` 中编辑 `Data/Festivals/FestivalDates` 来重排日期。
  * 在 `Data/Festivals/<season><day>` 中，将 `conditions` 的第二个 token 改写为 `"1800 2350"`，把通行窗口移至真实的 `18:00–23:50`；并拦截 `Game1.warpFarmer`（第 9978 行）中 `ReadyCheckDialog("festivalStart")` 的锁定，允许选择不参加活动的人正常通过 `Town` 通行。

---

## 5. 架构决策记录（ADR）

关键的架构决策及其被否决的备选方案记录于 [`docs/adr/`](docs/adr/)：

* **ADR-0001：** 通过原生 `NewDay` + `Save` 流程在 `02:00 AM` 进行每日日切（`06:00 AM` 黎明）。
* **ADR-0002：** 1:1 时钟（`0600–2550`），在深夜冻结（`NightHold`），并采用由 `CalendarEpoch` 推导的日历。
* **ADR-0003：** 声明式农场地块（`Data/Locations`），MVP 使用标准模板。
* **ADR-0004：** 主机权威（`ServerWorldState`）、客户端强制安装 mod、隔离的模块化注册（`IModule`）。
* **ADR-0005：** 主机农场上的中央广场与声明式桥梁地图（`Data/Locations`），以扩展到最多 16 处私人居所。
* **ADR-0006：** 按局管理的能量：持续衰减的饱腹值、激活坐着休息的「吃饱」增益以及 Spa 每日增益。
* **ADR-0007：** 基于设备的不对称身份（ECDSA P-256）以及 Lidgren 握手中的 `P4E\x02` 挑战。
