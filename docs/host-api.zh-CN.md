# 主机 API v1（`Pelican 4Ever` — `TASK-803` 与 `TASK-804`）

本地 HTTP 契约，用于在游戏之外操作 Pelican 4Ever 服务器。它可以实时监控时钟、查询邻居
的状态、协调节庆的批次与仪式、在不断开会话的情况下登记主机的每日休息，并安排受控重启。

**它是可选的。** 没有它 Pelican 4Ever 同样可用：人类主机拥有 `/evento empezar`、Lewis、
控制台命令和自动换日。在配置自己的 `AdminApiToken` 之前 API 不会启动，并且只在本地监听。
一种可能的客户端是 **Stardew Voyager** / **OpenClaw** 的代理主机，它还需要
`AgentHost: true` 才能在不断开连接的情况下休息；本模组不依赖 Voyager，Voyager 也不依赖
本模组。

---

## 1. 安全性、网络与在主线程中执行

1. **默认仅监听回环地址：**
   - 默认情况下（`config.json` 中 `AdminApiBindAllInterfaces: false`），服务器只监听
     `http://127.0.0.1:{AdminApiPort}/` 和 `http://localhost:{AdminApiPort}/`。
   - 不会发送 `Access-Control-Allow-Origin: *` 响应头。
2. **使用默认令牌时拒绝启动：**
   - 如果 `AdminApiToken` 为空，或仍保留默认值 `pelican4ever_secret_admin_token`，
     `AdminApiModule` **不会启动** `HttpListener`，并在 SMAPI 日志中记录一条错误。
3. **所有路由都必须认证：**
   - 无论 Web 面板（`GET /`、`GET /dashboard`）还是任何 `/api/*` 和 `/api/v1/host/*` 路由，
     都要求 `Authorization: Bearer <AdminApiToken>`（Web 面板也可以用 `?token=<AdminApiToken>`）。
   - 没有有效令牌时，服务器返回 `401 Unauthorized`。
4. **在游戏主线程中派发（`UpdateTicked`）：**
   - 所有从 HTTP 线程入队的世界查询或状态变更操作，都在 Stardew Valley 的主线程中通过
     `MainThreadActionQueue.Drain()` 于 `GameLoop.UpdateTicked` 事件期间执行。

---

## 2. 版本化命名空间 `/api/v1/host/` 的路由

### `GET /api/v1/host/clock`

返回游戏时间与真实时间、服务器是否处于深夜时段（`IsNightHold`）、下一次换日的时刻、
上次换日的结果与备份，以及正在提供的存档。

**响应 `200 OK`（`HostClockDto`）：**
```json
{
  "RealTimeUtc": "2026-10-31T15:00:00.0000000Z",
  "LocalTime": "2026-10-31T16:00:00",
  "TimeZoneId": "Europe/Madrid",
  "GameTimeOfDay": 1600,
  "TotalGameDays": 12,
  "Year": 1,
  "Season": "spring",
  "DayOfMonth": 13,
  "IsNightHold": false,
  "NextRolloverLocalTime": "2026-11-01T02:00:00",
  "SecondsUntilNextRollover": 36000,
  "LastRolloverGameDay": 11,
  "LastRolloverOutcomeSummary": "rested=3, penalized=0, untouched=1",
  "LastBackupFileName": "PelicanValley_20261031_020000_000",
  "LastBackupCreatedUtc": "2026-10-31T01:00:00.0000000Z",
  "SaveFolderName": "PelicanValley_452974881",
  "IsMultiplayerServer": true
}
```

`SaveFolderName` 是已载入存档在 `Saves/` 下的文件夹，`IsMultiplayerServer` 表示它是否以
合作服务器的方式开放；无人值守的监控程序可用它们来检查服务器提供的确实是获授权的存档
（指南：[despliegue/host-vps.md](despliegue/host-vps.zh-CN.md)）。

### `GET /api/v1/host/players`

返回已连接并已登记的玩家、他们的当前位置与坐标、分配到的居所、是否在今天的节庆场地内，
以及他们完整的 `PlayerRecord`（休息状态与饱腹值）。

**响应 `200 OK`（`HostPlayersResponseDto`）：**
```json
{
  "TotalOnline": 2,
  "TotalRegistered": 4,
  "Players": [
    {
      "PlayerId": 1234567890,
      "Name": "VoyagerHost",
      "IsOnline": true,
      "IsHost": true,
      "LocationName": "Town",
      "TileX": 28,
      "TileY": 67,
      "ResidenceSlotId": "Pelican4Ever_Farm_01",
      "ResidenceDoorId": "Town_Door_01",
      "ResidenceFarmType": "Standard",
      "InFestival": false,
      "SleepState": "Rested",
      "LastActiveGameDay": 12,
      "RestedOnGameDay": 12,
      "LastOutcome": "Rested",
      "SatietyUsed": 40,
      "WellFedBuffsUsed": 1,
      "WellFedActive": true,
      "SpaBuffsUsed": 0,
      "SpaBuffActive": false,
      "SatietyGameDay": 12,
      "PendingMarketGold": 0,
      "IsClaimed": true,
      "OwnerTokenMasked": "a1b2...c5d6"
    }
  ]
}
```

### `GET /api/v1/host/festival`

返回今天节庆的状态、时间窗口、地点、是否有主活动或现场仪式、在场内的玩家、已报名下一
批次的玩家、已执行的批次，以及今天已经参与过的人员。

**响应 `200 OK`（`HostFestivalDto`）：**
```json
{
  "IsFestivalToday": true,
  "FestivalId": "festival_spring13",
  "FestivalName": "Festival del Huevo",
  "Season": "spring",
  "DayOfMonth": 13,
  "LocationName": "Town",
  "WindowStart": 1800,
  "WindowEnd": 2350,
  "IsWindowOpen": true,
  "HasMainEvent": true,
  "IsCeremonyFestival": false,
  "PresentPlayerIds": [1234567890, 987654321],
  "SignedUpPlayerIds": [987654321],
  "PlayedPlayerIds": [],
  "RunsCount": 0
}
```

### `GET /api/v1/host/map?location=Town`

返回一个 `HostMapDto`，包含 `LocationName`、`Width`、`Height`、`Tiles`、`Players`、
`Warps`、`AvailableMaps`、`Season` 和 `GameDay`。它只查询已存在的地点，包括以其唯一名称
标识的建筑内部。如果找不到所请求的地点，则显示 Town。未载入世界时返回尺寸 0。

`Tiles` 是由 System.Text.Json 序列化为 **base64** 的 `byte[]`（而不是 JSON 数组）。读取
索引 `y * Width + x` 之前必须先解码。例如，格子 `[1, 3, 8, 11]` 会以 `"AQMICw=="` 的形式
收到。取值为：0 未知、1 地面、2 阻挡、3 水、4 建筑、5 小径、6 草地、7 树木、8 出口、
9 矿井、10 NPC、11 玩家。这是一种监控用的分类，而不是导航网格。

`Players` 包含标记项，其中有 `PlayerId`、`Name`、`TileX`、`TileY`、`IsHost`；`Warps` 包含
`TileX`、`TileY`、`TargetMap`、`TargetX`、`TargetY`。每次世界查询都在主线程中执行，并且
需要与其他路由相同的令牌。

### `POST /api/v1/host/festival/start`

等同于 `FestivalParticipationModule` 中的 `/evento empezar`。它会结束当前报名，并仅为已
报名的玩家启动下一批次主活动。

**响应 `200 OK`（`HostActionResultDto`）：**
```json
{
  "Ok": true,
  "Message": "[Festival] Empieza el evento (tanda #1) con 2 participantes.",
  "ExecutedOnThreadId": 1,
  "EventId": 14
}
```

### `POST /api/v1/host/festival/ceremony`

为在节庆场地内的所有邻居触发现场社区仪式（`TASK-702`，卢奥或月水母之舞）。

**响应 `200 OK`（`HostActionResultDto`）：**
```json
{
  "Ok": true,
  "Message": "Ceremonia iniciada para 3 vecino(s) en festival_summer11.",
  "ExecutedOnThreadId": 1,
  "EventId": 15
}
```

### `POST /api/v1/host/announce`

通过聊天、HUD 或两个渠道向已连接的玩家发送全局公告，并记录到事件日志中。

**请求体（`HostAnnounceRequestDto`）：**
```json
{
  "message": "La búsqueda de huevos comenzará en 5 minutos.",
  "channel": "both"
}
```

**响应 `200 OK`（`HostActionResultDto`）：**
```json
{
  "Ok": true,
  "Message": "Anuncio emitido (both).",
  "ExecutedOnThreadId": 1,
  "EventId": 16
}
```

### `POST /api/v1/host/rest`

登记主机玩家的每日休息（`RestedOnGameDay` 与 `LastActiveGameDay`），既不调用
`Game1.ExitToTitle()`，也不断开会话（`TASK-804`）。

**响应 `200 OK`（`HostActionResultDto`）：**
```json
{
  "Ok": true,
  "Message": "Descanso registrado. Modo anfitrión agente activo: el servidor permanece en línea.",
  "ExecutedOnThreadId": 1,
  "EventId": 17
}
```

### `POST /api/v1/host/restart`

安排或取消一次服务器受控重启（`TASK-804`），等价于控制台命令 `p4e_safe_restart`。
- 按可配置的提前量（`warningMinutes`）通知已连接的玩家。
- 计算并报告处于风险中的未保存当日进度（`ProgressAtRisk`），因为 Stardew Valley 只在换日
  时保存。
- 默认情况下（`immediate: false`），会等到下一次保存/换日刚结束之后
  （`WaitingForNextSave: true`）。
- 如果因紧急情况请求 `immediate: true`，则在指定的提前通知分钟数之后执行重启。

**请求体（`HostRestartRequestDto`）：**
```json
{
  "reason": "Actualización de mantenimiento",
  "immediate": false,
  "warningMinutes": 5,
  "cancel": false
}
```

**响应 `200 OK`（`SafeRestartStatusDto`）：**
```json
{
  "IsPending": true,
  "Mode": "after_next_save",
  "Reason": "Actualización de mantenimiento",
  "Immediate": false,
  "WaitingForNextSave": true,
  "WarningMinutes": 5,
  "RequestedAtUtc": "2026-10-31T15:10:00.0000000Z",
  "ExecuteNotBeforeUtc": null,
  "WarningAnnouncement": "[Reinicio Controlado] El servidor se reiniciará justo después del próximo guardado/Rollover (preaviso de 5 min)...",
  "ProgressAtRisk": {
    "CurrentGameDay": 12,
    "CurrentGameTime": 1430,
    "GameMinutesSinceMorning": 510,
    "OnlinePlayersCount": 2,
    "UnrestedActivePlayersCount": 1,
    "WouldLoseUnsavedDayProgress": true,
    "Summary": "Progreso no guardado del día 12 (8h 30m desde las 06:00, 2 jugador(es) conectado(s), 1 sin descansar hoy). Stardew Valley solo guarda al cambiar de día (Rollover)."
  },
  "EventId": 57
}
```

### `GET /api/v1/host/events?since=N`

返回 `Id > N` 的事件列表（append-only，仅追加），顺序严格递增且没有重复。如果
`LastEventId` 小于所请求的游标，说明日志已重置，客户端必须从 0 重新查询。
它包括节庆报名（`festival.signup`、`festival.signup.left`）、开始与结束的批次
（`festival.run.started`、`festival.run.ended`、`festival.ceremony.started`）、换日的
开始、完成以及在换日时卡住（`rollover.started`、`rollover.completed`、`rollover.stalled`：
模组不得不重新触发换日淡出；它带有 `nudges` 和 `secondsSinceAdvance`）、已创建的备份
（`backup.created`）、日内检查点（`checkpoint.created`，带 `backupName`、`bytes`、
`totalMs`、`longestTickMs`、`slow` 和 `nextIntervalMinutes`；`checkpoint.skipped`，在到点却
无法保存时，带 `reason`；`checkpoint.failed`，带 `error`）、主机休息（`host.rested`）、
公告（`host.announcement`）、受控重启（`host.restart.scheduled`、`host.restart.cancelled`、
`host.restart.executed`）、绘制借用（`host.render.lease.granted`、
`host.render.lease.released`）、玩家连接/断开（`player.connected`、
`player.disconnected`）、因模组原因被踢出（`player.rejected`，附带原因）、配对
（`player.paired`、`player.pairing.invited`、`player.pairing.revoked`）以及远程命令
（`host.command`、`host.test.command`）。

**响应 `200 OK`（`HostEventsResponseDto`）：**
```json
{
  "SinceId": 0,
  "LastEventId": 2,
  "Events": [
    {
      "Id": 1,
      "Type": "player.connected",
      "TimestampUtc": "2026-10-31T15:01:00.0000000Z",
      "GameDay": 12,
      "GameTime": 1500,
      "Summary": "Jugador conectado: Alex",
      "Data": {
        "playerId": 987654321,
        "playerName": "Alex"
      }
    },
    {
      "Id": 2,
      "Type": "festival.signup",
      "TimestampUtc": "2026-10-31T15:02:00.0000000Z",
      "GameDay": 12,
      "GameTime": 1500,
      "Summary": "Alex se apuntó a festival_spring13.",
      "Data": {
        "festivalId": "festival_spring13",
        "playerId": 987654321,
        "playerName": "Alex",
        "signedUpCount": 1
      }
    }
  ]
}
```

### `GET /api/v1/host/commands`

Pelican 控制台命令的**封闭**列表，可以远程执行。当 SMAPI 控制台被禁用（服务器没有终端）
时，这是启动它们的唯一方式。不在列表中的命令无法调用，即使它确实作为命令存在。

**响应 `200 OK`（`HostCommandCatalogDto`）：**
```json
{
  "Commands": [
    { "Command": "p4e_status", "Risk": "Read", "Usage": "p4e_status", "Available": true },
    { "Command": "p4e_checkpoint_now", "Risk": "Safe", "Usage": "p4e_checkpoint_now", "Available": true },
    { "Command": "p4e_claim_reset", "Risk": "Sensitive", "Usage": "p4e_claim_reset <nombre_o_id>", "Available": true }
  ]
}
```

`Risk`：**`Read`** 仅作报告（`p4e_status`、`p4e_doors`、`p4e_claim_list`、
`p4e_pair_list`）；**`Safe`** 会在不损失进度的前提下做出更改（`p4e_checkpoint_now`：保存
并备份而不换日；`p4e_pair_allow <usuario> <ip>`：准备一次按 IP 的准入，配套有效的配对码
`PairingInviteTtlHours`，默认 72 小时）；**`Sensitive`** 会更改世界或某个玩家
（`p4e_claim_reset`、`p4e_pair_approve`、`p4e_migrate_player`、`p4e_pair_revoke`、
`p4e_forage_wave`、`p4e_festival_move`）：调用者应当已获得所有者的批准。刻意排除在外的
有 `p4e_rollover_now`（提前换日；请使用 `POST /api/rollover`）、`p4e_safe_restart`
（它有带通知的专门路由）、`p4e_time`（打乱真实日历）、`p4e_migrate_save`、`p4e_give_*`
以及测试命令。

有些命令**只**存在于 SMAPI 控制台中，而不在这个列表里：`p4e_pair_reject`
（拒绝一项待处理的申请）、`p4e_checkpoint_spike`（需要 `CheckpointDebugMode`）、
`p4e_farm_*`、`p4e_skill`、`p4e_weather`、`p4e_lightning`（需要 `EnableTestCommands`）、
`p4e_render`、`p4e_rollover_now`、`p4e_safe_restart`、`p4e_time`、`p4e_migrate_save` 和
`p4e_give_*`。

在通过 IP 直连时，已获授权且版本最新的客户端会在发现阶段、也就是收到角色列表之前，响应
服务器的质询（`P4E\x02`）。该质询 30 秒后失效，与主机会话绑定，并且只接受一次响应。客户端
通过 NIST P-256 ECDSA 数字签名来证明其持有非对称私钥，游戏通道中不会传输任何机密；更换
IP 不需要重新配对。服务器和客户端都必须使用这一版本。旧版客户端会收到带有版本提示的安全
拒绝。新设备的批准在带外处理（通过 Discord 私信，经由 Putre），并校验其公钥指纹。

该质询用于认证设备身份，以及在进入游戏前对农民槽位的访问权限。存档期间 UDP 流量的加密
不在已商定的范围内。

### `POST /api/v1/host/command`

**在游戏线程中**执行该列表中的命令，并返回其执行期间写入 SMAPI 日志的内容（最多 40 行，
每行 300 字符；之后在其他 tick 中输出的内容不计入）。它会校验命令、参数个数及其字符集
（`A-Z a-z 0-9 . _ - @ :`，SHA-256 指纹最多 64 位），并要求已载入存档。任何尝试（无论
接受还是拒绝）都会留下一条 `host.command` 事件。

**请求体（`HostCommandRequestDto`）：**
```json
{
  "Command": "p4e_claim_reset",
  "Args": [ "mTx" ]
}
```

**响应 `200 OK`（`HostCommandResultDto`）：**
```json
{
  "Ok": true,
  "Command": "p4e_claim_reset",
  "Risk": "Sensitive",
  "Output": [ "Posesión de 'mTx' liberada." ],
  "Message": "p4e_claim_reset ejecutado.",
  "ExecutedOnThreadId": 1,
  "EventId": 31
}
```

不被允许的命令或参数无效的命令会返回 `Ok: false`，原因写在 `Message` 中，并且不会执行。

### `GET /api/v1/host/render`

**无绘制服务器**（`HeadlessRendering`）的状态。在无人值守的服务器上（已配置
`AutoHostSaveName`），主机会在载入世界的情况下停止绘制：模拟仍以 60 Hz 运行，网络表现
不变，每个客户端绘制自己的画面。没有 GPU 时，即使没人看主机的屏幕，用 CPU 绘制也会占满
整个核心。处于标题画面和加载阶段、存档保存界面打开时（`SaveGameMenu` 在被绘制之前不会
开始保存），以及存在绘制借用期间，仍会进行绘制。

**响应 `200 OK`（`HostRenderStatusDto`）：**
```json
{
  "HeadlessRenderingEnabled": true,
  "Drawing": false,
  "Reason": "Suppressed",
  "Leases": [],
  "Message": ""
}
```

`Reason`：`Disabled`（该选项已禁用）、`Attended`（没有 `AutoHostSaveName`：有人值守主机）、
`WorldNotReady`（标题画面或加载中）、`DrawRequiredMenu`（存档保存界面）、`Lease`
（借用仍然有效）或 `Suppressed`（未绘制）。

### `POST /api/v1/host/render`

申请或撤销一次**带有效期的绘制借用**，供需要看到主机画面的人使用（操作菜单的 Stardew
Voyager、Putre 或某个诊断流程）。每位申请者最多持有一个：再次申请会为其续期。时长在
1 到 120 分钟之间（到期即失效）；如果申请者掉线，它会自行过期，主机将重新回到不绘制的
状态。`Minutes: 0` 会撤销该借用。它不会影响游戏状态。`takeMapScreenshot` 的截图
（Voyager 桥接的那些）不需要借用。

另外还有：控制台命令 `p4e_render [status] | <minutos> | off`（申请者为 `console`），
以及供其他模组使用的模组 API `RequestRendering(requester, minutes)` /
`ReleaseRendering(requester)` / `IsHostDrawing()`
（`Helper.ModRegistry.GetApi<...>("Pelican4Ever.SVRTMP")`）。

**请求体（`HostRenderRequestDto`）：**
```json
{
  "Requester": "putre",
  "Minutes": 15
}
```

**响应 `200 OK`（`HostRenderStatusDto`）：**
```json
{
  "HeadlessRenderingEnabled": true,
  "Drawing": true,
  "Reason": "Lease",
  "Leases": [
    { "Requester": "putre", "ExpiresAtUtc": "2026-11-09T05:15:00Z" }
  ],
  "Message": "Dibujado prestado a 'putre' hasta 2026-11-09T05:15:00.0000000Z."
}
```

---

## 3. 经典路由 `/api/`

较早的路由，为兼容外部客户端（Voyager 桥接、监控脚本）而保留。认证规则和主线程规则
完全相同。

| 方法 | 路由 | 效果 |
| --- | --- | --- |
| GET | `/api/status` | `ServerStatusDto`：模组版本、时钟、`DedicatedHost`、玩家、门和市场 |
| POST | `/api/rollover` | 强制换日（保存、备份并断开 peers） |
| POST | `/api/mines/reset` | 重新生成无人占据的矿洞层 |
| POST | `/api/festival/reschedule` | 重新安排节庆：请求体 `{"originalDate": "...", "newDate": "..."}`，两者均为必填（缺少其一则返回 400） |

其他任何路由都返回 `404 {"error": "Not found"}`。

---

## 4. 非远程的控制台命令

API 中的列表是控制台命令的一个刻意挑选的子集。这些只能从 SMAPI 控制台（或从其他模组）
启动：

| 命令 | 作用 |
| --- | --- |
| `p4e_pair_reject <huella_o_alias>` | 拒绝一项待处理的准入申请 |
| `p4e_rollover_now` | 强制换日（有专门的路由：`POST /api/rollover`） |
| `p4e_safe_restart [motivo] [--immediate\|-i] [--minutes N\|-m N] [--cancel\|-c]` | 受控重启（有专门的路由） |
| `p4e_time <+X\|-X\|reset\|set HH:mm>` | 打乱时钟（仅用于测试） |
| `p4e_render [status\|<minutos 1-120>\|off]` | 绘制借用 |
| `p4e_migrate_save [slot\|geo]` | 将存档迁移到中央广场 |
| `p4e_give_gold <cantidad> [jugador]`, `p4e_give_item <id_o_nombre> [cantidad] [jugador]` | 调试 |
| `p4e_farm_found`, `p4e_farm_tp`, `p4e_farm_inspect`, `p4e_farm_spawn`, `p4e_lightning`, `p4e_weather`, `p4e_skill` | 居所测试：需要 `EnableTestCommands: true` |
| `p4e_checkpoint_spike [clonar] [forzar]` | 破坏性诊断：需要 `CheckpointDebugMode: true` |
| `p4e_dr4co_sprite`, `p4e_putre_sprite` | 主机外观 |
