# Pelican 4Ever — 系统总体规范（GSS）

🌐 **语言：** [Español](design.md) | [English](design.en.md) | 中文

> **产品名称：** *Pelican 4Ever*（`SV-RTMP`，`UniqueID: Pelican4Ever.SVRTMP`）
> **文档定位：** 系统总体规范（General System Specification, GSS）。说明产品定位、目标用户、持久世界中的行为规则以及必须满足的功能与非功能需求。
> 技术实现细节见 [`architecture.zh-CN.md`](architecture.zh-CN.md)，架构决策见 [`docs/adr/README.zh-CN.md`](docs/adr/README.zh-CN.md)，开发待办见 [`backlog.md`](backlog.md)，智能体可执行的实机测试见 [`live-agent-tests-backlog.md`](live-agent-tests-backlog.md)，真人玩家测试与校准见 [`test-users-tasks.md`](test-users-tasks.md)。
> **版本：** 0.5-draft · **日期：** 2026-11-16

---

## 1. 目的、范围与规范术语表

**Pelican 4Ever** 将 **Stardew Valley 1.6.15+**（基于 **SMAPI 4.1+**）从同步回合制、每日 14 分钟的合作游戏，改造为面向 **4 至 16 名玩家**社群 7×24 运行的 **1:1 实时持久世界**。

### 1.1 核心概念与规模

* **体验支柱：** 一个虚拟的「第二个家」。每位居民每天上线 30 至 90 分钟，照料私人居所（可通过中央广场或桥梁链进入）、经营被动生产、在钓鱼、采矿或捕虫中消耗每日精力与饱腹值预算、通过信箱向城镇商店下单，并在广场、城镇或傍晚/夜间节日中与社区成员相聚。
* **声明式扩展规模：** 通过 `FarmSlotCount`（`4, 8, 12, 16`）可配置为 **4 至 16 块地块**，每块地块最多支持 **2 名居民同居**（房主 + 1 名租客）及最多 **2 座主题附属农场**（`40×32`）。
* **信任与权限模型：** 强制客户端安装模组（`RequireClientMod = true`）的私密/社群服务器。主机是唯一权威来源（`ServerWorldState`），客户端通过非对称密码学（ECDSA P-256）验证设备身份。

### 1.2 范围之外

* 未安装 SMAPI 或未匹配所需模组包（`RequiredClientMods`）的原版客户端。
* 超出模组包校验、设备签名与主机权威不变量之外的、针对篡改本地 DLL 客户端的密码学反作弊。
* 在客户端配置文件中保存服务器密钥、Discord 令牌或主机 API 令牌。
* 不停止游戏进程的热恢复存档（恢复操作始终在停机状态下由 `Putre` / `saves.restore` 执行，见 [ADR-0010](docs/adr/0010-fail-closed-health-and-backup-contract.zh-CN.md)）。
* 在完成并接纳智能体压测（`LIVE-2503` / `PUTRE-401`）之前，在生产环境正式承诺支持 `FarmSlotCount > 8`。

### 1.3 定义与术语表

| 术语 | 定义 |
| :--- | :--- |
| **`NightHold`** | 真实时间 `01:50` 至 `06:00` 的时段，`Game1.timeOfDay` 冻结在 `2550`，避免达到 `2600` 强制昏倒，同时机器仍可继续加工。 |
| **`CalendarEpoch`** | 作为第 1 年春季第 1 天基准的真实星期一日期，用于数学推导游戏年/季/日，并使 28 真实天的季节与现实星期完全对齐。 |
| **原生日切（Rollover）** | 每天服务器时间 `02:00 AM` 通过 `Game1.NewDay` + `SaveGame.Save` 执行的日切流程，包含提前提醒（`01:50`）、SHA-256 备份校验及有序断开对端连接。 |
| **异步睡眠（Bed-Logout）** | 玩家在自己床上选择休眠（*«Descansar y Desconectar»*）标记 `RestedOnGameDay == 今天`，在不暂停或不踢出他人的情况下免除日切一次处罚。 |
| **日内检查点（Checkpoint）** | 周期性热保存世界与在线客户端（`CheckpointIntervalMinutes`），将 `06:00` 至 `02:00` 之间崩溃的进度损失降到最低，并按 `CheckpointRetention` 轮转。 |
| **中央广场与桥梁地图** | 声明式居所拓扑（[ADR-0005](docs/adr/0005-host-plaza-and-bridge-maps.zh-CN.md)）：主机农场（`Farm`）上的 4 个传送门向西串联最多 3 座 `35×30` 桥梁地图（`Pelican4Ever_Bridge_1..3`），每座桥含 4 个传送门与独立信箱。 |
| **居所附属区（Annex）** | 可购买的 `40×32` 格单入口主题扩展地图，采用原版图块拼装（[ADR-0009](docs/adr/0009-annex-vanilla-composition.md)），每块地块上限为 `MaxAnnexesPerResidence = 2`。 |
| **同居机制（`Owner` / `Tenant`）** | 1 名房主（`OwnerId`）与最多 1 名租客（`TenantId`）在同一居所共享建筑与动物管理，同时钱包、邮件、包裹、经验与矿井进度保持各自独立。 |
| **信箱延期订单** | 通过门外或室内信箱全天候向城镇商店下单、即时扣款，并在目标日真实时间 `12:00` 以购买者专属包裹、脚手架或动物形式交付。 |
| **`Lockdown` 模式（Fail-Closed）** | 提议中的保护状态（[ADR-0010](docs/adr/0010-fail-closed-health-and-backup-contract.zh-CN.md)）：若任一 `Security`/`Integrity` 补丁失败或游戏版本未经验证，`ModEntry` 直接阻止加载存档并拒绝联机。 |

---

## 2. 设计原则

1. **1:1 实时时钟且深夜不强制昏倒：** 1 个真实日等于 1 个游戏日；夜猫子玩家可在 `NightHold`（`2550`）继续游玩，既不因时钟昏倒也不阻塞 `02:00 AM` 日切。
2. **严格的主机权威与可信客户端校验：** 所有共享或敏感的持久化状态均保存在主机的 `ServerWorldState` 中；客户端仅提供本地交互界面与非对称设备身份。
3. **社区世界中的个人成长：** 社区中心、博物馆、Joja 和矿井楼层由全服共享。金币、`Shipping Bin`、饱腹值、技能、邮件、包裹、NPC 好感度和昆虫馆进度均按角色独立记录（同居者之间亦如此）。
4. **30–90 分钟单局节奏：** 离线恢复精力、4 小时持续衰减的饱腹值、坐姿「吃饱」回蓝以及针对无日历限制活动的选择性调节（[ADR-0008](docs/adr/0008-skill-economy-rebalance.zh-CN.md)），鼓励高频短局游玩而非无限刷取。
5. **Fail-Closed 且对智能体可观测的 7×24 运维：** 补丁失效或主线程卡死绝不能静默放行；危险状态必须闭锁拒连或脱离主线程暴露健康状态，使 `Putre` 能及时告警并自动恢复。

---

## 3. 用户画像

| 角色画像 | 核心诉求 | 原版 / 未加固托管的痛点 | *Pelican 4Ever* 的解决方案 |
| :--- | :--- | :--- | :--- |
| **Pelican 4Ever 居民** | 每天游玩 30–90 分钟经营私人农场并在镇上社交，无需等待真人房主上线。 | 依赖房主在线；14 分钟一天迫使所有人同步睡觉。 | 7×24 连续 1:1 世界、专属传送门与信箱、异步睡眠、异步市场与邮购。 |
| **同居者 / 租客（`Tenant`）** | 与好友合住同一农场共享建筑和动物，同时保留自己的金币、进度与存档角色。 | 原版将客户端死绑在固定 `Cabin`，并混用或限制他人农场权限。 | 门外信箱申请与管理同居、共享建筑与 `AnimalPage`、个人进度完全隔离。 |
| **夜猫子玩家** | 深夜之后仍能建造、购物或钓鱼，不被关门和 `02:00` 昏倒打断。 | `02:00` 强制昏倒且商店傍晚打烊。 | `2550` 冻结的 `NightHold` 与次日 `12:00` 交付的 24/7 信箱订购。 |
| **准入申请者（`#pelican-gate`）** | 即使家庭公网 IP 变动，也能安全接入私人服务器。 | IP 白名单脆弱或人工审核缓慢。 | ECDSA P-256 设备身份（`P4E\x02`）、短指纹工单与 **Putre** 2 名额自动观察期。 |
| **智能体主机 / 守卫（`Dr4co` / `Putre`）** | 7×24 运维服务器、发布公告、监测健康并避免数据丢失，且不依赖自由 shell。 | 卡死进程不会退出；失效补丁静默放行；缺乏恢复契约。 | `/api/v1/host/*`（含提议中的脱离主线程 `/health`）、`AgentHost`、systemd 看门狗与 `p4e-backup.json` 契约。 |
| **服务器管理员 / 服主** | 校准经济数值、审批高危操作并可信赖地从校验备份恢复存档。 | 缺乏每日经济遥测与跨盘轮转/恢复保证。 | `DailyEconomy` 遥测、Putre 驱动的 S3 同步与轮转，以及一次性 2 级审批码。 |

---

## 4. 世界子系统规范

### 4.1. 时间系统、日历与日切（[ADR-0001](docs/adr/0001-native-daily-rollover.zh-CN.md)、[ADR-0002](docs/adr/0002-realtime-clock-and-epoch-calendar.zh-CN.md)）

* **1:1 时钟（`06:00`–`01:50` → `0600`–`2550`）：** 每 10 分钟真实时间推进 10 分钟游戏时间（每段 `600,000 ms`），支持环境光照平滑插值并禁止个人菜单或空服暂停世界时钟。
* **深夜冻结（`NightHold`，`01:50`–`06:00`）：** 时钟冻结在 `2550`（永不到达 `2600`），夜间活动不会因超时昏倒，炉子与机器会在日切时结算剩余深夜分钟。
* **永久纪元日历（`CalendarEpoch`）：** 每季 28 个真实日（每年 112 个真实日）；每季第 1 天恒为真实星期一，第 7 天恒为真实星期日。停机重启后 `CatchUpPlan` 最多连续追赶 `MaxCatchUpRollovers`（默认 `3`）或跳转至真实日期。
* **原生日切（`02:00 AM`）：** 提前 `RolloverWarningMinutes`（默认 `10` 分钟）告警，生成带 SHA-256 校验的备份，有序关闭对端并执行 `Game1.NewDay` + `SaveGame.Save`。若备份或有序断开失败，日期绝不推进。光照与 NPC 日程在 `06:00 AM` 启动。

### 4.2. 异步睡眠（*Bed-Logout*）与日内检查点

* **派生状态（`SleepPolicy`）：** 玩家状态（`Awake`、`Rested`、`OfflineSafe`、`OfflinePenalized`）由 `ServerWorldState.Players[UID]` 中的 `LastActiveGameDay`、`RestedOnGameDay` 与 `LastOutcome` 推导。
  * 在床上选择休眠会标记 `Rested`，下次上线恢复 100% 精力并转入 `OfflineSafe`，不受任何处罚。
  * 日切时仍为 `Awake` 的玩家受到一次性处罚（个人金币 `PenaltyGoldPercent = 0.1`，上限 `PenaltyGoldCap = 1000`，精力 `PenaltyEnergyFraction = 0.5`）并转为 `OfflinePenalized`（之后连续离线不再重复处罚）。
  * 处于 `OfflineSafe` 或 `OfflinePenalized` 期间阻止 NPC 好感度日常衰减。
* **日内检查点（史诗 11）：** 可配置的周期性存档（`CheckpointIntervalMinutes`、`CheckpointCloneFarmhands = true`、可选 gzip 压缩），在日切或节日期间自动跳过，并按 `CheckpointRetention`（默认 `12`）保留轮转。

### 4.3. 精力、1:1 经济、矿井与进度规则（[ADR-0006](docs/adr/0006-session-energy-satiety-wellfed-spa.zh-CN.md)、[ADR-0008](docs/adr/0008-skill-economy-rebalance.zh-CN.md)）

* **按局精力 + 饱腹值：** 基础精力条 `270`（+ *Stardrops*）；离线 `OfflineRegenFullBarHours = 4` 小时即可回满。进食恢复精力受最大精力 50% 的饱腹上限约束（`DailySatietyFraction = 0.5`），饱腹值在 `SatietyDrainHours = 4` 小时内持续衰减。
* **「吃饱」增益与温泉（Spa）：** 每天最多触发 `MaxDailyWellFedBuffs = 2` 次；饱腹值达 `WellFedSatietyThresholdFraction = 0.25` 即获得持续 `60 min` 的「吃饱」增益，坐在椅子或地上（`GroundSitKey`）每 `5 s` 恢复 `1` 点精力。在温泉静立 `30 s` 每天可激活 1 次在 `120 min` 内持续恢复 `120` 精力与 `120` 生命值的增益。
* **1:1 再平衡（[ADR-0008](docs/adr/0008-skill-economy-rebalance.zh-CN.md)，*提议中，待 `TASK-2107` 校准*）：** 每日满额经验上限（`SkillDailyFullXp = 300`，覆盖钓鱼、采集、采矿与战斗；超出部分按 `SkillOverflowXpMultiplier = 0.25` 计算）、咬钩时间 `2.5×`、钓鱼小游戏时长 `1.5×`、矿井梯子概率下调（`MineStoneLadderChanceMultiplier = 0.5`、`MineMonsterLadderChance = 0.08`）及骷髅洞穴按层数扣减精力（`SkullCavernDescentBaseCost = 1.0`、`SkullCavernDescentDepthDivisor = 25.0`）。搭配 1:1 原生加工时长（熔炉约 30 分钟、罐头瓶约 2.8 天、小桶约 6.9 天）与可配置物价乘数（`CropPriceMultiplier`、`ArtisanPriceMultiplier`、`FishPriceMultiplier`、`MineralPriceMultiplier`，默认均为 `1.0x`）。
* **12 小时持久矿井：** 有人所在的楼层不卸载；空置楼层在 `MineFloorCooldownMinutes = 45` 分钟后卸载，无人楼层于真实时间 `06:00` 与 `18:00` 重置（`MineResetTimes`）。
* **共享进度 vs. 个人进度：**

| 范围 | *Pelican 4Ever* 规则 |
| :--- | :--- |
| **经济与出货箱** | **个人**（`team.useSeparateWallets = true`、`personalShippingBin`），同居者之间亦各自独立。 |
| **地块、房屋、建筑与动物** | **按地块独立**（`Pelican4Ever_Farm_01..16`），仅在同地块 `OwnerId` 与 `TenantId` 间共享。 |
| **社区中心 / 博物馆 / Joja** | **全服共享（社区）**。 |
| **昆虫馆（NITV 与 Wild Animals）** | 捕获、捐赠、展出的收藏、解锁与奖励均为**个人进度**。每个角色可以独立捐赠同一物种，并在凭本人的捐赠达成条件后领取每项奖励一次。 |
| **矿井电梯、邮件、包裹与技能经验** | **个人**独立。矿井楼层共享；矿井奖励宝箱为个人一次性领取。 |
| **与 NPC 的关系** | **个人**独立，`Offline_*` 状态期间不衰减。 |

主机和所有客户端都必须使用包含此改动的 Pelican 4Ever 版本。已有存档的共享捐赠记录保留在地图数据中。由于旧记录未保存捐赠者身份，每个角色从自己的捐赠开始建立个人收藏。已领取的奖励保留在角色记录中。旧版按物品记录领取状态，因此与已领取物品相同的奖励也视为已领取。

### 4.4. 中央广场、私人居所、附属区、同居与信箱

* **声明式拓扑（[ADR-0003](docs/adr/0003-declarative-farm-slots.zh-CN.md)、[ADR-0005](docs/adr/0005-host-plaza-and-bridge-maps.zh-CN.md)）：** 最多 16 处带 `AlwaysActive = true` 的居所（`Pelican4Ever_Farm_01..16`），支持 8 种官方农场模板，分布于中央广场（`Farm`，4 个传送门）与桥梁链 `Pelican4Ever_Bridge_1..3`（`35×30`，每座 4 个传送门），具备空间朝向对齐（`SpatialWarpRouter`）与空置地块客户端网络剔除。
* **40×32 附属农场（[ADR-0009](docs/adr/0009-annex-vanilla-composition.md)）：** 每处居所最多购买 `MaxAnnexesPerResidence = 2` 个主题附属区（`FirstAnnexPriceMultiplier = 0.4`、`SecondAnnexPriceMultiplier = 1.0`）并拥有独立个人洞穴选择（`PrivateFarmCaves`）。
* **访问权限（`VisitorGuardModule`）：** 房主可设置 `Locked`、`Friends` 或 `Public` 及 `VisitorsCanEdit`。无编辑权限的访客不可使用破坏性工具、打开箱子/包裹或放置/拆除物品与生态箱，但可按策略浇水、钓鱼、使用弹弓或使用 NITV 捕虫网。
* **同居机制（史诗 23）：** 每块地块最多接纳 1 名租客（`TenantId`），通过门外信箱申请与管理。租客共享房屋（`_Home` 含 2 张床及各自醒来坐标）、升级、地窖、温室、附属区以及建筑与动物管理权限（`Robin`、`Wizard`、`Marnie`、`AnimalPage`），可分别领养各自宠物，并在驱逐或房主弃置地块时触发安全疏散与自动过户。
* **12:00 信箱延期订单（史诗 22）：** 通过门外/室内信箱全天候向商店下单并即时扣款，于目标日真实时间 `12:00`（`NightHold` 期间阻塞）以买家专属包裹、脚手架或动物形式送达。

### 4.5. 城镇生活、宠物、聊天与模组协同

* **NPC 与节日：** NPC 微漫游（每隔 45–90 s 半速在 $\pm 2$ 格内移动）以及位于夜间窗口 `18:00–23:50` 的独立节日（`FestivalWindowStart = 1800`、`FestivalWindowEnd = 2350`），支持日期改期（`p4e_festival_move`）、自由进出、报名分组竞赛与自动仪式。
* **客户端宠物领养（史诗 20）：** 在 Robin 处购买 `Pet Bowl` 后，客户端下次醒来触发 Marnie 拜访领养事件并权威绑定宠物。
* **与 `PelicanChat` 及 `Nature In The Valley` 协同（史诗 24）：** 通过 `HostEventLog` + `/api/v1/host/chat` 联动 `/l`、`/g`、`/w` 频道与 Discord 桥接；在 `Pelican4Ever.NITVCompat` 与核心逻辑中支持 NITV 生态代码（`"0"`/`"Outside"`、`"1"`/`"Forest"`、`"2"`/`"Beach"`、`"3"`/`"Underground"`），将捕虫网蓄力减速 `"NatCSpeedN"` 排除在 `85.7x` 时间缩放之外，并允许访客使用捕虫网。
* **外观装扮与 `AgentHost`：** 支持智能体主机在不关闭联机网络的情况下自动休眠，内置 `Dr4co` 与 `Putre` 的 *Fashion Sense* 身体外观。

### 4.6. 安全、身份与和 `Putre` 的边界协作

* **ECDSA P-256 身份（[ADR-0007](docs/adr/0007-dispositivo-identidad-y-acceso-seguro.zh-CN.md)）：** 脱离 IP 绑定的 Lidgren `P4E\x02` 握手、限时配对邀请（`PairingInviteTtlHours = 72`）及 `RequiredClientMods` 强校验。
* **2 名额试用准入协议（`ProbationTelemetryModule`）：** **Putre** 在 `#pelican-gate` 发放临时工单（`MaxConcurrentPutreSlots = 2`），追踪分阶段行为遥测并可拉入 Putre 不可撤回的黑名单（仅服主可解除）。
* **主机 API 与韧性契约（[ADR-0010](docs/adr/0010-fail-closed-health-and-backup-contract.zh-CN.md)）：** 运行于回环地址的 REST API（`/api/v1/host/*`）在主线程执行，并在史诗 25 中扩展脱离主线程的 `/api/v1/host/health`、*fail-closed* 启动阻断、`sd_notify` 看门狗，以及供 Putre 同步 S3 和执行 `saves.restore` 的 `p4e-backup.json` 清单。

---

## 5. 功能需求（`RF`）

### 5.1 时间引擎、日历与日切（`RF-01`..`RF-08`）

| 编号 | 需求描述 | 优先级 |
| :--- | :--- | :--- |
| `RF-01` | 以 1:1 真实时间推进游戏时钟（`06:00` 至 `01:50` 每 10 游戏分钟耗时 `600,000 ms`），不受菜单或无人服务器暂停影响。 | 高 |
| `RF-02` | 在每 10 分钟真实时段内平滑插值环境光照（`MillisecondsIntoSlot`）。 | 中 |
| `RF-03` | 在真实时间 `01:50` 至 `06:00` 间将时钟冻结于 `2550`（`NightHold`），避免超时昏倒同时允许机器完成加工。 | 高 |
| `RF-04` | 基于 `CalendarEpoch` 确定性计算游戏日期（每季 28 个真实日），确保第 1 天恒为真实星期一、第 7 天恒为真实星期日。 | 高 |
| `RF-05` | 在夜间停机重启后通过 `CatchUpPlan`（最多 `MaxCatchUpRollovers` 次或直接跳转）规划追赶。 | 高 |
| `RF-06` | 在换日前通过 HUD 发送提前警告（`RolloverWarningMinutes`，默认于 `01:50` 提前 `10` 分钟）。 | 高 |
| `RF-07` | 于 `02:00 AM` 通过原生 `NewDay` + `SaveGame.Save` 执行日切，含 SHA-256 备份与有序断连；若备份失败则不推进日期。 | 高 |
| `RF-08` | 在真实时间 `06:00 AM`（`DayStartTime`）启动日照与 NPC 白天日程，不在深夜换日后提前执行白天日程。 | 高 |

### 5.2 异步睡眠与日内检查点（`RF-09`..`RF-13`）

| 编号 | 需求描述 | 优先级 |
| :--- | :--- | :--- |
| `RF-09` | 允许每位玩家在床上休眠（*«Descansar y Desconectar»*）标记 `RestedOnGameDay == 今天`，不暂停世界也不踢出他人。 | 高 |
| `RF-10` | 仅由主机基于 `ServerWorldState.Players[UID]` 推导 `SleepState`（`Awake`、`Rested`、`OfflineSafe`、`OfflinePenalized`）。 | 高 |
| `RF-11` | 对日切时处于 `Awake` 的玩家只执行一次处罚（`PenaltyGoldPercent = 0.1`，上限 `1,000g`，`PenaltyEnergyFraction = 0.5`），之后离线日（`OfflinePenalized`）不再处罚。 | 高 |
| `RF-12` | 在玩家处于 `OfflineSafe` 或 `OfflinePenalized` 期间冻结 NPC 每日好感衰减。 | 中 |
| `RF-13` | 按 `CheckpointIntervalMinutes` 执行日内检查点并克隆在线客户端（`CheckpointCloneFarmhands`），在日切与节日期间跳过，并按 `CheckpointRetention` 轮转。 | 高 |

### 5.3 经济、饱腹值与 1:1 进度（`RF-14`..`RF-20`）

| 编号 | 需求描述 | 优先级 |
| :--- | :--- | :--- |
| `RF-14` | 在离线 `OfflineRegenFullBarHours = 4` 小时内自动回满基础精力条（`270` + *Stardrops*）。 | 高 |
| `RF-15` | 通过连续衰减的饱腹值容量（`DailySatietyFraction = 0.5`、`SatietyDrainHours = 4` 小时）限制进食即时回蓝，同时不阻止回血与增益。 | 高 |
| `RF-16` | 每日最多触发 `MaxDailyWellFedBuffs = 2` 次「吃饱」增益（阈值 `WellFedSatietyThresholdFraction = 0.25`），坐在家具或地面（`GroundSitKey`）时每 `5 s` 恢复 `1` 精力。 | 高 |
| `RF-17` | 在温泉静立 `30 s` 每日激活 1 次持续 `120 min` 恢复 `120` 精力与 `120` 生命值的增益。 | 中 |
| `RF-18` | 应用可配置的 1:1 再平衡规则（提议 ADR-0008）：`SkillDailyFullXp` 上限、钓鱼咬钩/小游戏乘数、矿井梯子率、骷髅洞穴层耗、物价乘数与每日经济遥测（`DailyEconomyTelemetry`）。 | 中 |
| `RF-19` | 保持有人矿层持续激活，并在 12 小时班次（`06:00` 与 `18:00`）或空置超过 `MineFloorCooldownMinutes = 45` 分钟后重置无人楼层。 | 高 |
| `RF-20` | 保持个人钱包（`useSeparateWallets = true`）与 `personalShippingBin` 独立，同时社区中心、博物馆、Joja、矿井楼层与昆虫馆维持共享。 | 高 |

### 5.4 居所、桥梁、附属区、租客与信箱（`RF-21`..`RF-30`）

| 编号 | 需求描述 | 优先级 |
| :--- | :--- | :--- |
| `RF-21` | 在 `Data/Locations` 中声明式注册 4 至 16 处居所（`FarmSlotCount`），分布于中央广场（`Farm`，4 个传送门）与桥梁 `Pelican4Ever_Bridge_1..3`（`35×30`，各 4 个传送门）。 | 高 |
| `RF-22` | 认领地块时允许选择 8 种官方农场模板之一，并按 `North`/`South` 边界保持一致的空间传送朝向（`SpatialWarpRouter`）。 | 高 |
| `RF-23` | 对客户端应用动态网络剔除，隐藏并避免传输无人在场的空置居所与附属区。 | 高 |
| `RF-24` | 允许每处居所购买最多 `MaxAnnexesPerResidence = 2` 个 `40×32` 附属区（`FirstAnnexPriceMultiplier = 0.4`、`SecondAnnexPriceMultiplier = 1.0`）与独立个人洞穴选择（`PrivateFarmCaves`）。 | 中 |
| `RF-25` | 在主机与客户端执行访问策略（`Locked`、`Friends`、`Public`、`VisitorsCanEdit`），阻止访客使用破坏性工具或翻看箱子。 | 高 |
| `RF-26` | 支持通过门外信箱进行 1 名房主 + 最多 1 名租客的申请、邀请、接受、退出与驱逐流程。 | 高 |
| `RF-27` | 将用于网络认证的技术 `Cabin` 与共享居所室内（`Pelican4Ever_Farm_XX_Home`，含 2 张床与各自醒来坐标）解耦，避免同居登录槽位冲突。 | 高 |
| `RF-28` | 在房主与租客间共享建筑（`Robin`、`Wizard`）、动物（`Marnie`、`AnimalPage`）、地窖与附属区权限，同时隔离钱包、邮件、包裹、经验与矿井。 | 高 |
| `RF-29` | 门禁为 `Locked` 时安全疏散被驱逐的租客、重定向其在途包裹，并在房主弃置地块时自动过户（`OwnerId = TenantId`）。 | 高 |
| `RF-30` | 支持通过信箱全天候下单、即时扣款并在 `12:00`（`NightHold` 期间阻塞）以买家专属包裹形式交付。 | 高 |

### 5.5 城镇生活、节日、宠物与模组协同（`RF-31`..`RF-36`）

| 编号 | 需求描述 | 优先级 |
| :--- | :--- | :--- |
| `RF-31` | 在服务端驱动村民半速自主微漫游（每隔 45–90 s 在 $\pm 2$ 格内移动），避开碰撞与 `Warps`。 | 中 |
| `RF-32` | 在傍晚/夜间窗口（`1800–2350`）运行可改期（`p4e_festival_move`）的独立节日，支持自由进出且不冻结时间。 | 中 |
| `RF-33` | 在竞技类节日中管理报名分组、自动仪式（`20:45` 提醒 / `21:00` 开始）与延迟发奖（`PendingFestivalPrizes`）。 | 中 |
| `RF-34` | 客户端购买 `Pet Bowl` 后解锁专属宠物领养事件，并权威绑定到食盆与玩家。 | 中 |
| `RF-35` | 保障与 *Nature In The Valley* 兼容：在 `Pelican4Ever.NITVCompat` 暴露 `"0"`..`"3"` 生态代码、排除 `"NatCSpeedN"` 减速的 `85.7x` 缩放，并允许访客使用捕虫网同时锁定 `Terrarium`。 | 中 |
| `RF-36` | 支持可与 `PelicanChat` 对接的多频道聊天（`/l`、`/g`、`/w`）、托管式社区市场、`AgentHost` 休眠模式与 `Dr4co`/`Putre` 外观包。 | 中 |

### 5.6 安全、身份与主机 API（`RF-37`..`RF-42`）

| 编号 | 需求描述 | 优先级 |
| :--- | :--- | :--- |
| `RF-37` | 通过脱离 IP 的非对称 ECDSA P-256 设备密钥（Lidgren 中的 `P4E\x02`）与限时邀请码（`PairingInviteTtlHours = 72`）认证客户端。 | 高 |
| `RF-38` | 在初始握手阶段校验模组版本与精确的 `RequiredClientMods` 列表（`ClientVersionGuardModule`）。 | 高 |
| `RF-39` | 运行 Putre 的 2 名额试用准入协议（`MaxConcurrentPutreSlots = 2`）、行为遥测（`ProbationTelemetryModule`）与 Putre 不可撤回黑名单。 | 高 |
| `RF-40` | 暴露主机 REST API（`/api/v1/host/*` 与 `HostEventLog`），包含白名单远程命令与安全重启（`p4e_safe_restart`）。 | 高 |
| `RF-41` | （*提议 ADR-0010 / 史诗 25*）在 `PatchRegistry` 中按 `Security`、`Integrity`、`Experience` 注册全部 Harmony 补丁，并在关键补丁失败或版本不在 `VerifiedGameVersions` 时进入 *fail-closed* `Lockdown`。 | 高 |
| `RF-42` | （*提议 ADR-0010 / 史诗 25*）提供脱离游戏主线程的 `GET /api/v1/host/health`，通过 `sd_notify` 发送 `WATCHDOG=1` 心跳，并在每次备份生成 `p4e-backup.json` 清单。 | 高 |

---

## 6. 非功能需求与运维韧性（`RNF`）

| 编号 | 需求 | 指标 / 验收准则 |
| :--- | :--- | :--- |
| `RNF-01` | **纯领域逻辑分离** | 所有时间、日历、睡眠、经济、拓扑、安全与策略纯逻辑均位于 `Pelican4Ever.Core`（`net6.0`），对 `StardewValley`、`SMAPI`、`MonoGame` 与 `Harmony` 保持 **0** 引用。 |
| `RNF-02` | **自动化单元测试套件** | `Pelican4Ever.Core` 的纯逻辑由 `Pelican4Ever.Tests`（`net8.0`）的 `xUnit` 测试验证，可通过 `dotnet test` 一键执行（1,684 项测试通过）。 |
| `RNF-03` | **编译质量门禁** | `Pelican4Ever.Core` 与 `Pelican4Ever` 启用 `TreatWarningsAsErrors = true` 与 `Nullable = enable`（允许 `0` 警告）。 |
| `RNF-04` | **国际化（`i18n`）** | 全面支持英文（`i18n/default.json`）、西班牙文（`i18n/es.json`）与简体中文（`i18n/zh-CN.json`）及回退提供器，不得出现未翻译键名。 |
| `RNF-05` | **隔离模块化架构** | 所有 SMAPI 模块实现 `IModule`，按运行侧（`Host` / `Client`）及开关配置（`ModToggleModule`）干净注册。 |
| `RNF-06` | **最小网络暴露面与客户端零密钥** | 仅公开 `24642/udp`；REST API（默认 `AdminApiPort = 8085`，回环绑定）默认不对外暴露，且客户端不保存任何服务器或 Discord 令牌。 |
| `RNF-07` | **日切与检查点完整性** | 每次每日备份与检查点均对文件逐个校验 SHA-256；`RolloverDayGuard` 防止重复处罚，且若备份或有序断连失败则绝不推进日期。 |
| `RNF-08` | **无头运行与算力预算** | 专用主机支持抑制绘制的 headless 运行（`HeadlessRenderModule` + `Xvfb`），目标虚拟机规格为 `2 vCPU`、`LP_NUM_THREADS=1`、`Nice=5`、`MemoryMax=4G`。 |
| `RNF-09` | **Fail-Closed 启动（提议 ADR-0010）** | 任一 `Security`/`Integrity` 补丁失败，或版本不在 `VerifiedGameVersions`（`["1.6.15"]`）与 `MinimumSmapiVersion`（`4.1.0`）内，均在无需依赖 Harmony 补丁的情况下阻止加载 `AutoHostSaveName` 与联机接入，仅允许 `LockdownOverride` 按补丁名显式豁免。 |
| `RNF-10` | **无锁可观测健康接口（提议 ADR-0010）** | `GET /api/v1/host/health` 直接从 HTTP 线程响应（`< 50 ms`）且绕过 `MainThreadActionQueue`，输出 `Status`、`SecondsSinceLastTick`、`SaveLoaded`、`Lockdown.Reasons[]` 与 `Patches.Failed[]`。 |
| `RNF-11` | **看门狗卡死检测（提议 ADR-0010）** | 存在 `NOTIFY_SOCKET` 时每 `15 s` 发送 `WATCHDOG=1`，仅当 `SecondsSinceLastTick <= HangThresholdSeconds`（`120 s`）；`systemd` 声明 `WatchdogSec=180` 与 `NotifyAccess=all`。 |
| `RNF-12` | **Putre 负向监控告警（提议 ADR-0010）** | `Putre` 每 `60 s` 轮询 `/api/v1/host/health`，在 `Ok` 状态下保持静默，仅在转入 `Lockdown`、`Degraded`、卡死、崩溃或备份失败及恢复时触发 `owner.alert`。 |
| `RNF-13` | **备份清单与 S3 保留契约（提议 ADR-0010）** | 每份备份均包含 `p4e-backup.json`（`FormatVersion = 1`），默认本地保留 `BackupRetentionDays = 14` 与 `CheckpointRetention = 12`；Putre 在每次 `backup.created` 后执行无 `--delete` 的 `aws s3 sync`，过期策略交由 IaC 管理。 |
| `RNF-14` | **停机校验恢复（提议 ADR-0010）** | `saves.restore`（Putre 2 级）先停服，校验 `p4e-backup.json` 与 SHA-256，将原存档移至 `Pelican4Ever_Backups/pre-restore/<UTC>/`（永不删除），复制目标备份，启动后要求 `health.Status == "Ok"`、`SaveLoaded == true` 且 `TotalGameDays` 一致。 |
| `RNF-15` | **规模实测验证门禁（提议 ADR-0010）** | 在基于 8、12、16 个模拟客户端测得存档体积、`SaveGame.Save()` 耗时、`LastCheckpointLongestTickMs`、带宽及 NITV 生物密度（`LIVE-2503` / `PUTRE-401`）之前，不在生产环境宣称支持 `FarmSlotCount > 8`。 |
