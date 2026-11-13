# 在本机部署主机服务器（你自己的电脑）

指南：在你的 PC 或 Steam Deck 上搭建并游玩主机存档：游戏画面可见，由你自己管理，
邻居通过直连 IP 加入。

如果你想要的是**VPS 上无屏幕的 7×24 全天候服务器**，请改用
[host-vps.md](host-vps.zh-CN.md)。

相关文档：[cliente.md](cliente.zh-CN.md) · [host-vps.md](host-vps.zh-CN.md) ·
[../host-api.md](../host-api.md) · [../servidor-preview.md](../servidor-preview.zh-CN.md)

---

## 1. 什么是「本机主机模式」

主机就是载入合作存档的玩家。在本机模式下：

- 游戏会**带窗口**打开，你能看到发生的一切（轮到自己时也可以亲自游玩）。
- 你可以**去睡觉并关闭**游戏而不影响其他人：只有主机重新载入存档时，
  世界才会继续运转。
- 邻居通过*直接连接*接入你机器的 UDP 端口 `24642`。

Stardew Valley **没有专用服务器模式**：始终有一位本地玩家站在最前面
（也就是主机）。`config.json` 中的 `DedicatedServerMode` 标志**在当前代码中未被使用**。

---

## 2. 要求

- Stardew Valley **1.6.15**（Steam 或 GOG）。
- **SMAPI 4.1 或更高**（安装在游戏内；模组声明 `MinimumApiVersion: 4.1.0`）。
- 模组 **Pelican 4Ever 0.2.0** 以及完整的模组包，版本与服务器所使用的
  完全一致（见 [../servidor-preview.md](../servidor-preview.zh-CN.md)）。

常用的 `Mods/` 路径：

| 操作系统 | 路径 |
| --- | --- |
| Linux / Steam Deck | `~/.steam/steam/steamapps/common/Stardew Valley/Mods/`（或 `~/.local/share/Steam/steamapps/common/Stardew Valley/Mods/`） |
| Windows | `C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\Mods\` |

---

## 3. 安装模组

1. 在你的开发机上编译（`dotnet build Pelican4Ever.sln -c Release`），或使用团队交给你的
   ZIP `Pelican4Ever 0.2.0.zip`。
2. 把 ZIP 解压到 `Mods/` 文件夹**内部**，使最终存在
   `Mods/Pelican4Ever/manifest.json`。
3. 同时安装模组包（Content Patcher、SpaceCore、Fashion Sense、Dr4co Dragon Body、
   Nature In The Valley、SH's Wild Animals In The Valley 和 `Pelican4Ever.NITVCompat`）：服务器
   要求客户端使用**与主机完全一致的精确版本**。
4. **通过 SMAPI** 启动游戏（启动器 `StardewModdingAPI`），而不是普通可执行文件。

启动时，模组会生成 `Mods/Pelican4Ever/data/device_key.json`，其中包含你的密钥对
（ECDSA P-256）。在 Linux 和 macOS 上会将其权限设为 `0600`。

---

## 4. 创建主机存档

在游戏中：**合作 → 主机 → 新农场**。选择主机的农场和你想要的
模板（Standard、Riverland、Forest、Hilltop、Wilderness、Four Corners、Beach 或
Meadowlands）。保存存档并记下 `Saves/` 中出现的**确切文件夹名**
（例如 `Pelican4Ever_452974881`）。

这个名称有两个用途：

- `config.json` 中的 **`AutoHostSaveName`**：到达标题画面时，模组只会把该
  存档作为主机载入。如果名称不是完全一致，**不会载入其他任何存档**：
  它会把这个情况写进日志。
- 存放配对 sidecar 文件的文件夹：`Saves/<存档>/pelican4ever_pairing.json`。

---

## 5. 本机的 `config.json`

它位于 `Mods/Pelican4Ever/config.json`（若不存在，SMAPI 会自动用默认值创建；
完整模板见 [`config.example.json`](../../config.example.json)）。

```json
{
  "TimeZoneId": "Europe/Madrid",
  "AutoHostSaveName": "Pelican4Ever_452974881",
  "RestartExitsProcess": false,
  "HeadlessRendering": false,
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
  "AgentHost": false
}
```

本机情况下的注意事项：

- **`HeadlessRendering: false`**：你是在看着屏幕，而不是为了省掉绘制。
  如果你载入存档后不去看它，把它设为 `true` 以节省 CPU。
- **`RestartExitsProcess: false`**：受控重启会回到标题画面，随后自动载入
  在同一进程内重新打开存档（3 秒）。设为 `true` 时游戏会关闭，由重新拉起它的一方
  （systemd、脚本）用新模组再次打开。
- **`AdminApiBindAllInterfaces: false`**：API 只绑定 `127.0.0.1`。设为
  `true` 时会在所有接口上监听：在没有防火墙的公共网络上不要这么做。
- **`AdminApiToken`**：如果为空、仍为默认值
  `pelican4ever_secret_admin_token` 或为占位符 `CAMBIA-ESTE-TOKEN`，API **不会启动**。
- **如果 `config.json` 无效，模组不会注册**（日志：「配置无效：在修正 config.json
  之前，Pelican 4Ever 保持停用状态」）。没有降级模式。

---

## 6. 向全世界开放游戏

游戏会在 **UDP 端口 24642** 上开启合作服务器（由 1.6.15 引擎固定）。
每台机器只能有一个实例承载联机存档。

- **同一局域网内的邻居：** 提供承载游戏那台机器的本地 IP
  （`ip addr` / `ipconfig`）以及端口 `24642`。
- **通过互联网的邻居：** 在路由器（端口转发）和系统
  防火墙中开放 UDP `24642`，并使用你的公网 IP。如果 IP 变了，基于指纹的配对仍然
  可以继续使用（身份不依赖 IP），但邻居必须改指向新地址。
  使用固定 IP 的 VPS 可以避免这个问题：对于稳定的团队这是推荐方案。

> **不需要打开 Steam**：没有它就没有 Steam 邀请码，只能通过
> *直接连接*进入。

---

## 7. 登记邻居

服务器受设备白名单保护（`RequirePairingWhitelist`）。完整的分步流程见
[cliente.md](cliente.zh-CN.md)。从主机侧的概要如下：

1. **创建一个邀请**并与邻居的 IP 绑定。会生成一个 6 位字符的一次性代码，
   有效期为 `PairingInviteTtlHours`（默认 72 小时）：
   ```
   p4e_pair_allow <nombre> <IP_DEL_VECINO>
   ```
2. 邻居尝试连接：服务器登记其设备并将其断开，同时显示其
   **短指纹**（12 位十六进制，例如 `a1b2c3d4e5f6`）。
3. **批准该指纹**：
   ```
   p4e_pair_approve a1b2c3d4e5f6
   ```
   （或者用 `p4e_pair_approve <huella> <id_de_peón>` 在批准的同时绑定角色）。
4. 邻居重新连接并进入农场工选择界面。其他玩家的农场工会以
   `[De otro jugador]` 标签灰显，无法选择。

辅助命令：

| 命令 | 作用 |
| --- | --- |
| `p4e_pair_list` | 已授权设备、有效邀请、待处理请求、被拦截的尝试 |
| `p4e_pair_reject <huella>` | 拒绝一个待处理的请求 |
| `p4e_pair_revoke <usuario\|huella\|id>` | 撤销某个设备的访问权限并踢出其玩家 |
| `p4e_claim_list` | 列出农场工及其归属 |
| `p4e_claim_reset <nombre_o_id>` | 释放某个角色的占用，以便其主人重新认领 |

> 通过 API 可用的远程命令列表比控制台**更短**：`p4e_pair_reject` 和
> `p4e_claim_list` 只存在于控制台（API 确实提供 `p4e_claim_list`、`p4e_pair_list`、
> `p4e_pair_allow`、`p4e_pair_approve`、`p4e_migrate_player`、`p4e_claim_reset`、
> `p4e_pair_revoke`）。确切目录见 [../host-api.md](../host-api.md)。

---

## 8. 本机 API

存档载入后，API 即可使用：

```bash
TOKEN=TOKEN-LARGO-Y-ALEATORIO
curl -s -H "Authorization: Bearer $TOKEN" http://127.0.0.1:8085/api/v1/host/clock
```

此版本没有 Web 面板：面板位于 `feat/web-panel` 分支，不在 `main` 或 `dev` 中。
它原本能做的所有事情都通过 API（`/api/v1/host/`）完成。
节庆、地图和操作等标签页。把 token 粘贴到「Bearer Token」请求头中。

---

## 9. 玩完之后

- 你可以**上床睡觉并退到主菜单**而不影响其他人：每位玩家各自睡觉并
  单独断开连接。如果主机是人类玩家，关闭游戏**会让存档处于无服务器状态**，
  直到存档被重新载入。
- 想让世界在无人值守时继续运转，就让主机保持无人值守：`AgentHost: true`
  会让主机的休息**在不断开连接、也不退到标题画面的情况下**登记。
- 设置了 `AutoHostSaveName` 后，手动回到标题画面会在 3 秒后重新打开存档：要
  停止服务器，请停止进程（在 systemd 下：`systemctl --user stop pelican4ever-host`）。

可用于本机无人值守主机的 systemd 单元：
`deploy/systemd/pelican4ever-host.service`（用户级，带图形会话）和
`deploy/systemd/pelican4ever-preprod.service` + `pelican4ever-preprod-xvfb.service`
（用户级，独立的虚拟屏幕，`HOME`/`XDG_*` 相互隔离）。

---

## 10. 常见问题

| 症状 | 原因 | 解决方案 |
| --- | --- | --- |
| 游戏能启动但模组没出现 | `config.json` 无效 | SMAPI 日志会列出所有配置错误 |
| 邻居进入时被踢出 | 模组或模组包版本不一致 | 服务器日志会给出确切原因；API 的 `player.rejected` 会记录它 |
| 「服务器受白名单保护……」 | 设备未获授权 | 用 `p4e_pair_allow` 创建邀请并批准指纹 |
| 「你的 IP 的所有邀请都已被占用……」 | 该 IP 上存在来自其他指纹的邀请（NAT） | 为该别名创建另一个邀请，或批准待处理的那个 |
| 重新打开游戏时存档未载入 | `AutoHostSaveName` 不匹配 | 查看日志（`SaveMissing`）以及 `Saves/` 中确切的文件夹名 |
| `Address already in use` | 另一个 Stardew 实例，或端口 24642 被占用 | `ss -lunp \| grep 24642` 和 `pgrep -af Stardew` |
| 邻居看得到存档却看不到 NITV 的动物 | 客户端缺少 `Pelican4Ever.NITVCompat` | 客户端需使用模组包的精确版本，这是必需的 |
