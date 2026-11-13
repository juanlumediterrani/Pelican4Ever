# 在 VPS 上部署主机服务器（7×24 全天候服务器）

指南：让 Pelican 4Ever 在远程 Linux 服务器上永久运行一局存档，无需桌面环境，
也无需有人手动打开游戏。

- **并非**游戏原生的「专用服务器」：主机仍然是一局普通的合作游戏
  （`Game1.multiplayerMode = 2`），并带有一名本地玩家（主机本人）。
  在当前代码中，`config.json` 里的 `DedicatedServerMode` **没有任何作用**：不要把它
  当作任何开关来使用。
- VPS 部署真正特有的地方是**不绘制**（`HeadlessRendering`）并且**没有 GPU**：
  即便什么都不画，游戏也需要图形上下文，而这正是 `Xvfb` 解决的问题。

相关文档：[host-local.md](host-local.zh-CN.md) · [cliente.md](cliente.zh-CN.md) ·
[../host-api.md](../host-api.md) · [../servidor-preview.md](../servidor-preview.zh-CN.md)

---

## 1. 要求

| 资源 | 最低 | 推荐 |
| --- | --- | --- |
| CPU | 2 vCPU | 4 vCPU（16 名玩家） |
| 内存 | 2 GB | 4 GB |
| 磁盘 | 20 GB | 40 GB（存档 + 备份 + 检查点） |
| 操作系统 | Debian 12 / Ubuntu 22.04 x86-64 | 任何带 systemd 的系统 |
| 网络 | 可出站访问互联网 + 入站 UDP 端口 `24642` | — |

在一台 2 vCPU 的虚拟机上实测（已载入存档、无远程玩家、`HeadlessRendering`
开启）：游戏的 CPU 占用约 **23%**（而 CPU 绘制原本占到 94%）。

---

## 2. 准备系统

```bash
sudo apt update
sudo apt install -y xvfb libgl1-mesa-dri libglu1-mesa curl unzip git
sudo useradd --system --create-home --home-dir /var/lib/pelican4ever --shell /usr/sbin/nologin pelican4ever
sudo mkdir -p /opt/pelican4ever/game /var/lib/pelican4ever/.config/StardewValley/Saves \
              /var/lib/pelican4ever/.config/StardewValley/Pelican4Ever_Backups \
              /etc/pelican4ever
sudo chown -R pelican4ever:pelican4ever /var/lib/pelican4ever /opt/pelican4ever
```

`Xvfb` 是必需的：没有图形上下文游戏无法启动。`libgl1-mesa-dri` 提供
虚拟屏幕所使用的软件渲染（`llvmpipe`）。

---

## 3. 安装游戏与 SMAPI

Stardew Valley **没有许可证就无法运行**：使用拥有该游戏的账号通过 SteamCMD，
或者从一台已拥有游戏的电脑上复制游戏文件夹。

### 方案 A · SteamCMD（推荐，游戏可自动更新）

```bash
sudo apt install -y steamcmd
sudo mkdir -p /var/lib/steamcmd
sudo ln -sf /var/lib/steamcmd/steamcmd.sh /usr/bin/steamcmd

sudo -u pelican4ever steamcmd \
  +force_install_dir /opt/pelican4ever/game \
  +login <CUENTA_STEAM> \
  +app_update 413150 validate \
  +quit
```

### 方案 B · 从另一台电脑复制

把完整的游戏文件夹（Windows 或 Linux）复制到 `/opt/pelican4ever/game`，并把
所有者设为 `pelican4ever`。这种方式不会自动更新：每次游戏打补丁都要重复该操作。

### SMAPI

1. 从 [smapi.io](https://smapi.io/) 下载 **Linux** 安装包（SMAPI 4.1 或更高）。
2. 将其解压到 `/opt/pelican4ever/game` **内部**，使最终存在
   `/opt/pelican4ever/game/StardewModdingAPI`。
3. 安装 SMAPI 所需的运行时：

```bash
sudo apt install -y dotnet-runtime-6.0
```

继续之前先确认能够启动（它应当在虚拟屏幕上打开标题画面）：

```bash
sudo -u pelican4ever env HOME=/var/lib/pelican4ever DISPLAY=:99 \
  xvfb-run -a sh -c 'cd /opt/pelican4ever/game && ./StardewModdingAPI'
```

---

## 4. 安装模组与模组包

### 4.1 编译（在你自己的开发机上）

模组项目**不能在 CI 中编译**：它需要游戏和 SMAPI 的 DLL，而 GitHub runner 上并不存在。
请在本地编译，然后上传生成的 ZIP。

```bash
cp local.props.example local.props      # ajusta <GamePath> si el juego no está en la ruta habitual
dotnet build Pelican4Ever.sln -c Release
```

编译后会得到：

- 部署好的模组，位于 `<GamePath>/Mods/Pelican4Ever/`（属性 `EnableModDeploy`），
- 安装包 `Pelican4Ever/bin/Release/net6.0/Pelican4Ever 0.2.0.zip`（属性 `EnableModZip`）。

> `global.json` 将 SDK 固定为 **8.0.427** 并设置 `rollForward: disable`：请严格使用该版本
> 进行编译，否则构建会以 CS9057 失败。

### 4.2 上传到服务器

```bash
scp "Pelican4Ever/bin/Release/net6.0/Pelican4Ever 0.2.0.zip" usuario@tu-vps:/tmp/
scp deploy/modpack/Pelican4Ever.NITVCompat usuario@tu-vps:/tmp/
```

在 VPS 上：

```bash
sudo -u pelican4ever mkdir -p /opt/pelican4ever/game/Mods
sudo -u pelican4ever unzip -o -q /tmp/'Pelican4Ever 0.2.0.zip' -d /opt/pelican4ever/game/Mods/
sudo -u pelican4ever cp -r /tmp/Pelican4Ever.NITVCompat /opt/pelican4ever/game/Mods/
```

> `EnableModDeploy` **会覆盖**每次构建的 `Mods/Pelican4Ever`。如果你在玩游戏的同一台
> 机器上编译，请先复制 `config.json` 和 `data/device_key.json`。

### 4.3 服务器上必需的模组包

服务器要求客户端使用**与服务器完全一致的精确版本**安装完整模组包
（见 [../servidor-preview.md](../servidor-preview.zh-CN.md)）。请在 `Mods/` 中安装：Content Patcher、SpaceCore、
Fashion Sense、Dr4co Dragon Body、Nature In The Valley（及其内容包）、SH's Wild Animals
In The Valley，以及兼容包 `Pelican4Ever.NITVCompat`。

如果服务器没有安装其中某个模组，它虽然出现在要求列表中，但**会被忽略并给出一条
警告**日志：只有服务器确实安装了的模组才构成要求。

---

## 5. 创建主机存档

「合作 → 主机 → 新农场」界面需要图形界面，因此实际做法是**在你的电脑上创建存档**
并复制到 VPS：

1. 在你的 PC 上安装好模组后，创建主机存档，并记下 `Saves/` 中出现的**确切文件夹名**
   （例如 `Pelican4Ever_452974881`）。
2. 把该文件夹复制到 VPS：

```bash
scp -r "Saves/Pelican4Ever_452974881" usuario@tu-vps:/tmp/
sudo -u pelican4ever cp -r /tmp/Pelican4Ever_452974881 \
  /var/lib/pelican4ever/.config/StardewValley/Saves/
```

这个名称就是 `AutoHostSaveName` 的值。自动载入模块**从不**选择「最近的存档」：
如果名称不是完全一致，它不会载入任何存档，并会在日志中说明。

---

## 6. 服务器的 `config.json`

它位于 `/opt/pelican4ever/game/Mods/Pelican4Ever/config.json`。完整模板见
[`config.example.json`](../../config.example.json)。在 VPS 上最关键的是：

```json
{
  "TimeZoneId": "Europe/Madrid",
  "AutoHostSaveName": "Pelican4Ever_452974881",
  "RestartExitsProcess": true,
  "HeadlessRendering": true,
  "RequireClientMod": true,
  "RequirePairingWhitelist": true,
  "RequiredClientMods": [
    "Pathoschild.ContentPatcher",
    "spacechase0.SpaceCore",
    "PeacefulEnd.FashionSense",
    "Dr4co.Stardew.FashionSenseBody",
    "Nature.NatureInTheValley",
    "Nature.NatureInValleyContent",
    "SmolHooman.WildAnimalsInTheValley",
    "Pelican4Ever.NITVCompat"
  ],
  "AdminApiPort": 8085,
  "AdminApiToken": "TOKEN-LARGO-Y-ALEATORIO",
  "AdminApiBindAllInterfaces": false,
  "AgentHost": false,
  "BackupRetentionDays": 30
}
```

模组在读取该文件时会自行应用的规则：

- **如果配置无效，模组不会注册**（日志：「配置无效：在修正 config.json 之前，Pelican 4Ever 保持停用状态」）。
  没有降级模式：要么全部可用，要么完全不可用。
- `AdminApiToken` 为空、为默认值 `pelican4ever_secret_admin_token` 或为占位符
  `CAMBIA-ESTE-TOKEN` ⇒ **HTTP API 不会启动**。请设置一个足够长的自有 token。
- `AdminApiBindAllInterfaces: false` ⇒ API 只监听 `127.0.0.1:8085` 和
  `localhost:8085`。设为 `true` 时会在 `http://*:8085/` 上监听（没有防火墙就不要这么做）。
- `RestartExitsProcess: true` **要求** `AutoHostSaveName` 非空。
- `DayStartTime` 必须正好是 `06:00`，`CalendarDayChangeTime` 必须正好是 `02:00`。
- `CalendarEpoch` 必须是**星期一**（格式为 `yyyy-MM-dd`）：永续日历由此推导。
- `TimeZoneId` **不会校验**：不存在的 IANA 标识符会在运行时崩溃。

### 6.1 静音 SMAPI 控制台

在 systemd 下标准输入是 `/dev/null`：SMAPI 读取后遇到文件末尾，便会**不停地反复读取**
（占满一个 CPU 核心）。用 `Mods/SMAPI-config.json` 关闭它：

```json
{
  "ListenForConsoleInput": false
}
```

控制台命令将不再可用；它们改由主机 API 启动
（`POST /api/v1/host/command`），那是一份封闭列表。详情见 [../host-api.md](../host-api.md)。

---

## 7. 安装服务

把仓库克隆或上传到 VPS（服务只需要 `deploy/systemd/` 下的文件）并复制单元文件：

```bash
sudo cp deploy/systemd/pelican4ever-vps-xvfb.service /etc/systemd/system/
sudo cp deploy/systemd/pelican4ever-vps.service        /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now pelican4ever-vps.service
```

这是**系统**单元，不是用户单元：它不依赖有人登录，也不依赖存在桌面环境。它以
`HOME=/var/lib/pelican4ever` 启动游戏，这样存档和备份都存放在 `/opt` 之外：

```
/var/lib/pelican4ever/.config/StardewValley/Saves/            partidas
/var/lib/pelican4ever/.config/StardewValley/Pelican4Ever_Backups/  copias diarias y checkpoints
/opt/pelican4ever/game/Mods/Pelican4Ever/                     mod + config.json
```

如果游戏目录与单元文件中指定的不一致，请在
`/etc/systemd/system/pelican4ever-vps.service` 中调整 `STARDEW_DIR`，或者创建
`/etc/pelican4ever/host.env`（单元文件会在它存在时加载它）。

### 确认它还活着

```bash
sudo systemctl status pelican4ever-vps
sudo journalctl -u pelican4ever-vps -f          # debe aparecer "[AutoHost] Cargando ... como anfitrión"
curl -s -H "Authorization: Bearer $TOKEN" http://127.0.0.1:8085/api/v1/host/clock
```

---

## 8. 网络与防火墙

| 端口 | 协议 | 是否对外开放 |
| --- | --- | --- |
| `24642` | UDP | **是**，游戏唯一的公开端口 |
| `8085` | HTTP（API + Web 面板） | **否**：保持在回环地址，通过 SSH 隧道访问 |
| `5900` | TCP（VNC） | 此部署中不使用（Xvfb 带 `-nolisten tcp` 运行） |

```bash
sudo ufw allow 24642/udp
sudo ufw enable
```

没有 VNC 也没有图形服务器，就无法「看到」这局存档。要查看主机的画面，可以通过 API
申请一次**绘制借用**（1–120 分钟），或使用 `p4e_render`：

```bash
curl -s -X POST http://127.0.0.1:8085/api/v1/host/render \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"requester":"admin","minutes":15}'
```

从你的笔记本通过 SSH 隧道访问 Web 面板（不要开放端口）：

```bash
ssh -L 8085:127.0.0.1:8085 usuario@tu-vps
# luego abre http://127.0.0.1:8085/ y pega el token en la cabecera
```

**没有打开 Steam 就没有邀请码**：玩家直接通过 IP 连接 `24642` 端口
（在菜单中选择*直接连接*）。

---

## 9. 日常运维

### 常用命令（主机 API）

```bash
TOKEN=TOKEN-LARGO-Y-ALEATORIO
H="Authorization: Bearer $TOKEN"

# Estado del reloj y del calendario
curl -s -H "$H" http://127.0.0.1:8085/api/v1/host/clock

# Quién está conectado
curl -s -H "$H" http://127.0.0.1:8085/api/v1/host/players

# Aviso a todos los jugadores
curl -s -X POST -H "$H" -H 'Content-Type: application/json' \
  -d '{"message":"Mantenimiento en 10 minutos","channel":"both"}' \
  http://127.0.0.1:8085/api/v1/host/announce

# Forzar el cambio de día (guarda + backup + cierre de peers)
# Ojo: p4e_rollover_now NO está en la lista de comandos remotos; se usa la ruta clásica.
curl -s -X POST -H "$H" http://127.0.0.1:8085/api/rollover

# Reinicio controlado tras el próximo guardado
curl -s -X POST -H "$H" -H 'Content-Type: application/json' \
  -d '{"reason":"Actualización del mod","immediate":false,"warningMinutes":5,"cancel":false}' \
  http://127.0.0.1:8085/api/v1/host/restart
```

完整的路由和远程命令目录见 [../host-api.md](../host-api.md)。

### 登记一名玩家

1. 创建一个与玩家 IP 绑定的**邀请**（6 位字符代码，只能使用一次，
   有效期 `PairingInviteTtlHours`，默认 72 小时）：
   ```bash
   curl -s -X POST -H "$H" http://127.0.0.1:8085/api/v1/host/command \
     -d '{"command":"p4e_pair_allow","args":["nombre","IP_DEL_JUGADOR"]}'
   ```
2. 玩家连接一次：服务器登记该请求并将其断开，同时显示其
   **短指纹**（12 位十六进制）。
3. 批准该指纹：
   ```bash
   curl -s -X POST -H "$H" http://127.0.0.1:8085/api/v1/host/command \
     -d '{"command":"p4e_pair_approve","args":["a1b2c3d4e5f6"]}'
   ```
4. 玩家重新连接并进入。

`p4e_pair_list` 会显示已授权设备、有效邀请、待处理请求以及最近的被拦截尝试。
`p4e_pair_revoke <usuario|huella|id>` 会撤销某个设备并踢出其已连接的玩家。

---

## 10. 更新模组而不丢失存档

1. 在你的开发机上编译（`dotnet build Pelican4Ever.sln -c Release`）并上传 ZIP。
2. 把新的 ZIP 部署到 `/opt/pelican4ever/game/Mods/`（会覆盖 `Pelican4Ever/`
   文件夹；**不要**删除 `config.json`）。
3. 执行受控重启，它会等待下一次保存并通知玩家：
   ```bash
   curl -s -X POST -H "$H" -H 'Content-Type: application/json' \
     -d '{"reason":"Actualización del mod","immediate":false,"warningMinutes":5}' \
     http://127.0.0.1:8085/api/v1/host/restart
   ```
4. 在 `RestartExitsProcess: true` 下游戏会关闭进程；systemd 会用新模组重新拉起它，
   而 `AutoHost` 会自行重新打开存档。
5. 使用其他模组版本的客户端会被踢出，直到他们完成更新（`RequireClientMod`
   比较 *major.minor*；模组包的其余部分按**精确版本**比较）。

回滚：保留旧的 ZIP，部署旧版本并重复受控重启即可。

---

## 11. 备份与检查点

- **每日备份：** 换日时生成，位于
  `/var/lib/pelican4ever/.config/StardewValley/Pelican4Ever_Backups/<存档>_<UTC 日期>/`，并带
  SHA-256 校验。除非 `BackupRetentionDays` 大于 0，否则不会删除；此时会保留这些天的备份，
  并且**始终**保留最新的一份。
- **日内检查点**（默认关闭）：当 `CheckpointIntervalMinutes` 大于 0 时，
  主机每隔该时长保存一次存档，既不换日也不踢人，位置在
  `Pelican4Ever_Backups/checkpoints/`。保留最近的 `CheckpointRetention` 份。在节庆期间
  或换日前的预告窗口内不会生成。

### 恢复备份（需停止服务）

```bash
sudo systemctl stop pelican4ever-vps
cd /var/lib/pelican4ever/.config/StardewValley/Saves
mv Pelican4Ever_452974881 Pelican4Ever_452974881.antes-de-restaurar
cp -r /ruta/a/la/copia Pelican4Ever_452974881
gunzip Pelican4Ever_452974881/*.gz 2>/dev/null || true   # solo si venía de checkpoints/
sudo systemctl start pelican4ever-vps
```

---

## 12. 诊断

| 症状 | 常见原因 | 处理方式 |
| --- | --- | --- |
| `Address already in use` | UDP `24642` 已被占用：另一个 Stardew 实例或残留进程 | `sudo ss -lunp \| grep 24642` · `pgrep -af Stardew` · 模组最多等待 60 秒后会在日志中说明 |
| 「配置无效」 | `config.json` 中某个键的值不被允许 | 日志会列出**全部**错误；修正后重启 |
| API 返回 401 | token 缺失、错误，或仍为不安全的默认值 | `curl -H "Authorization: Bearer $TOKEN" ...`；更换 `AdminApiToken` |
| API 无响应 | `AdminApiPort <= 0`、token 不安全，或存档未载入 | API **只有**在存档载入时才监听；查看日志 |
| 载入存档后 CPU 占满 100% | 仍在使用 CPU 绘制 | 在 `SMAPI-config.json` 中设置 `HeadlessRendering: true` 和 `ListenForConsoleInput: false` |
| 客户端进入时被踢出 | 模组或模组包版本不一致 | 服务器日志会给出确切原因（`player.rejected`） |
| 启动时存档未载入 | `AutoHostSaveName` 与文件夹名不匹配 | 日志显示 `SaveMissing`；检查 `Saves/` 中确切的文件夹名 |
| 时钟不准确 | `TimeZoneId` 错误，或服务器使用了其他时区 | `Timedatectl` 与 `TimeZoneId` 必须一致 |

快速检查：

```bash
sudo journalctl -u pelican4ever-vps -n 100 --no-pager
sudo systemctl show -p ActiveState,SubState,MemoryCurrent pelican4ever-vps
sudo du -sh /var/lib/pelican4ever/.config/StardewValley/*
```

---

## 13. 已知限制

- 每台机器只能有**一个**游戏实例承载联机存档：引擎会固定
  UDP 端口 `24642`。
- 存档在**换日**时保存（02:00）。意外重启会丢失自上次换日以来的进展：
  请使用受控重启。
- 没有打开 Steam 就没有 Steam 邀请码。
- 主机在自己的床上睡觉：设置 `AgentHost: true` 时会以**不
  断开连接**的方式登记休息；设为 `false`（人类主机）时则属于正常情况。
