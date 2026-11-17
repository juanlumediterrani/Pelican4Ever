# ADR-0010 · 快速失败（Fail-Closed）启动、主机健康检查与 Putre 备份契约

🌐 **语言：** [Español](0010-fail-closed-health-and-backup-contract.md) | [English](0010-fail-closed-health-and-backup-contract.en.md) | 中文

- **状态：** 提议中
- **日期：** 2026-11-16
- **范围：** `Pelican 4Ever`（主机 mod）及其运营生产虚拟机的兄弟仓库 `Putre`（`putre-ops` / Hermes）。
- **关联文档：** [ADR-0001](0001-native-daily-rollover.zh-CN.md)（日切与 SHA-256 备份）、[ADR-0004](0004-host-authority-client-mod-and-satiety.zh-CN.md)（主机权威）、[ADR-0007](0007-dispositivo-identidad-y-acceso-seguro.zh-CN.md)（按设备身份验证）。待办列表：[backlog.md](../../backlog.md) 史诗 25 与 Putre 待办列表（`Putre/backlog.md`）。

## 背景

2026-11-16 的一次审计发现了 7×24 运行中的四项静默故障风险：

1. **Harmony 补丁默认以「放行（fail-open）」失败。** 系统包含约 84 个补丁。负责应用的三个辅助方法（`ClientSecurityModule.TryPatch`、`FarmSlotModule.PatchSafe` 和 `MailOrderModule.PatchSafe`）在捕获异常后仅记录日志便继续运行。一旦 `GameServer.checkFarmhandRequest` 未成功打上补丁，服务器就会在没有 ADR-0007 设备质询的情况下放行客户端。`VisitorGuardModule`、包裹所有权和地块隔离同理。游戏更新（1.6.16+）或 SMAPI 升级即可触发该问题，而 `manifest.json` 并未锁定游戏版本。
2. **主线程卡死无法被察觉。** systemd 单元使用 `Restart=always/on-failure`，仅在进程退出时触发。若游戏主线程陷入死循环、死锁或存档卡死，进程仍存活却无人知晓。Putre 目前仅在 `01:50` 的维护窗口内检查健康状态。
3. **备份缺乏正式契约。** `SaveBackupService` 在复制时校验 SHA-256，但未生成清单文件。`BackupRetentionDays = 0`（不轮转）是默认值。Putre 中已存在 `backup.saves_to_s3`，但尚未调度。`saves.restore` 在与 Pelican 约定格式前始终返回 `unavailable`。
4. **从未实测过设计规模。** 设计面向 2 vCPU CPU 渲染虚拟机上的 8–16 名玩家，而实机测试最多只验证过 Host + 2 个客户端。

## 决策

### 1. 补丁关键等级与 Fail-Closed 启动

每个 Harmony 补丁都在统一注册表（`PatchRegistry`）中声明其关键等级：

| 关键等级 | 示例 | 失败时动作 |
| --- | --- | --- |
| `Security` | `checkFarmhandRequest`、`sendPlayerIntroduction`、`FarmhandSlot`、`VisitorGuardModule`、`MailOrderModule` 包裹开启、`ClientVersionGuardModule` | **锁定（Lockdown）** |
| `Integrity` | 原生日切、`SaveGame`、检查点、1:1 时钟/`NightHold`、`findStructure`、`ServerWorldState` 持久化 | **锁定（Lockdown）** |
| `Experience` | 附属小地图、外观装扮、NITV、聊天、菜单 | 带告警的**降级（Degraded）**启动 |

是否锁定的判断属于 Core 中的纯领域逻辑（`PatchHealthPolicy.Decide`），并由单元测试覆盖。处于**锁定（`Lockdown`）**状态时：

- **不加载存档，也不开放多人服务器。** 该防线不依赖任何 Harmony 补丁，由 `ModEntry` 在处理 `AutoHostSaveName` 之前直接阻断。因此即使认证补丁失败，也不会留下敞开的后门。
- 主机 API 保持响应，以便 Putre 读取状态并通知管理员。
- 仅可通过修复根本故障或所有者显式豁免退出锁定：`LockdownOverride` 列出按名称接受豁免的补丁，并记录 `host.lockdown.overridden` 事件；不存在「无论如何强行启动」的全局开关。

### 2. 已验证游戏版本列表

`ServerConfig.VerifiedGameVersions`（默认 `["1.6.15"]`）与 `MinimumSmapiVersion`（默认 `4.1.0`）。若游戏版本不在列表中，将触发原因为 `UnverifiedGameVersion` 的**锁定**，除非配置了 `AllowUnverifiedGameVersion = true`。该选项仅用于隔离实验环境，不得在生产环境开启。在虚拟机中，游戏通过 `steamcmd` 固定版本安装，禁止自动更新。Putre 在执行 `release.apply` 与 `pelican.runtime_update` 前也会校验该列表。

### 3. 脱离游戏主线程的可观测健康接口

- `GET /api/v1/host/health` 直接在 HTTP 线程读取原子快照提供服务，**不经过 `MainThreadActionQueue`**，即使游戏主线程卡死也能立即响应。规范字段：`Status`（`Ok` | `Degraded` | `Lockdown` | `Starting`）、`LastTickUtc`、`SecondsSinceLastTick`、`SaveLoaded`、`Lockdown.Reasons[]`、`Patches.Total`、`Patches.Failed[]`（名称与关键等级）、`GameVersion`、`SmapiVersion`、`ModVersion` 与 `UptimeSeconds`。
- `HostEventLog` 新增事件：`host.lockdown`、`host.degraded` 与 `host.lockdown.overridden`。
- **systemd 看门狗：** 若存在 `NOTIFY_SOCKET`，后台线程每 15 秒发送一次 `WATCHDOG=1`，**仅当** `SecondsSinceLastTick <= HangThresholdSeconds`（默认 120 秒，高于实测最长存档耗时）时发送。服务单元声明 `WatchdogSec=180` 与 `NotifyAccess=all`。若游戏卡死，systemd 将终止进程并通过 `Restart=always` 重启。无 `NOTIFY_SOCKET` 时（本地测试或 Docker），该线程不执行任何操作。

### 4. 备份与恢复契约

- 每份备份（每日备份或检查点）均包含 `p4e-backup.json`，字段如下：
  - `FormatVersion = 1`；
  - `Kind`（`Daily` | `Checkpoint` | `PreRestore`）；
  - `SaveFolderName`、`TotalGameDays`、`GameDate`、`CreatedUtc`、`ModVersion` 与 `GameVersion`；
  - `Files[]`（相对路径、字节数与未压缩内容的 `Sha256`）；
  - `Compressed`。
  
  `ServerWorldState` 随主存档一起保存（`WriteSaveData`），因此每份备份都是一致快照。**不包含**：由 Putre 与 IaC 管理的 `config.json`，以及 `data/` 下的遥测数据。
- **默认本地保留策略：** `BackupRetentionDays = 14` 且 `CheckpointRetention = 12`。完整历史存放在服务器外的 S3。Putre 在每次 `backup.created` 后以不带 `--delete` 的 `aws s3 sync` 同步；过期清理仅由 IaC 中的 S3 存储桶生命周期策略控制，Putre 永不删除备份（其契约中属于 Level 3 禁止项）。
- **恢复**始终通过 Putre 的 `saves.restore`（Level 2，需所有者审批）按以下顺序执行：
  1. 停止游戏。
  2. 校验清单与全部 SHA-256。
  3. 将当前存档移至 `Pelican4Ever_Backups/pre-restore/<UTC>/`（永不删除）。
  4. 复制目标备份到存档目录。
  5. 启动游戏。
  6. 要求 `health.Status == Ok`、`SaveLoaded` 为真且 `TotalGameDays == manifest.TotalGameDays`（或由 `CatchUpPlan` 计算的目标日）。
  
  Pelican 不支持热恢复。

### 5. 先实测规模再承诺

在智能体压测台（见实时智能体测试待办列表）于参考虚拟机上完成 8、12、16 个模拟客户端的实测并记录存档体积、日切保存耗时、`LastCheckpointLongestTickMs`、单玩家带宽以及 NITV 生物密度（`locationCap × numberOfPlayers`）之前，不在生产环境宣称支持 `FarmSlotCount > 8`。实测数据将在接纳本 ADR 时补充至此。

## 仓库间职责划分

| 模块 | `Pelican 4Ever` | `Putre` |
| --- | --- | --- |
| 补丁注册、策略与锁定 | 实现 | 读取 `health` 并告警 |
| 已验证版本 | 启动时强制检查 | 更新前检查；在虚拟机固定 `steamcmd` 版本 |
| `/health` 与事件 | 暴露接口 | 每分钟轮询；仅在故障与恢复时通知 |
| systemd 看门狗 | 发送 `WATCHDOG=1` | 在虚拟机服务单元声明 `WatchdogSec`/`NotifyAccess` |
| 备份清单 | 生成并校验 | 校验、同步至 S3 并执行恢复 |
| 压测平台 | 提供模拟客户端与性能指标 | 提供参考虚拟机并保存报告 |

## 后果

- **收益：** 游戏升级或补丁损坏会让服务器明确关闭并上报告警，而非静默暴露。主线程卡死会转为自动重启与告警。恢复成为可验证的标准流程，使 Putre 得以启用 `saves.restore`。
- **代价：** 表观不可用时间可能增加。游戏版本更新后，服务器在完成版本验证前不会开放。若 `HangThresholdSeconds` 设置过低可能误杀长存档，因此需借助压测台校准。此外，每个新增补丁都必须声明关键等级。
- **规范数值**（索引规则 2）：`WatchdogSec=180`、心跳周期 15 s、`HangThresholdSeconds=120`、`BackupRetentionDays=14`、`CheckpointRetention=12`、`VerifiedGameVersions=["1.6.15"]`。
