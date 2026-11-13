# Pelican 4Ever 文档

项目活跃文档的索引。此处没有列出（或位于 [`archive/`](archive/)）的内容属于历史资料：
某个特定日期的报告、已收尾史诗的计划，或已被这些指南取代的流程。

## 部署指南

每份指南都有**西班牙语**（原文）、**英语**（`*.en.md`）和**简体中文**（`*.zh-CN.md`）
三个版本。文件名、命令、配置键和路径在三种语言中保持一致。

| 指南 | 语言 | 面向谁 | 涵盖内容 |
| --- | --- | --- | --- |
| VPS 上的主机 | [ES](despliegue/host-vps.md) · [EN](despliegue/host-vps.en.md) · [中文](despliegue/host-vps.zh-CN.md) | 主机 | Linux VPS 上 7×24 全天候服务器：安装、`Xvfb`、systemd、防火墙、备份、更新与诊断 |
| 本地主机 | [ES](despliegue/host-local.md) · [EN](despliegue/host-local.en.md) · [中文](despliegue/host-local.zh-CN.md) | 主机 | 在你自己的电脑上主持的存档：配置、玩家准入 |
| 客户端模式 | [ES](despliegue/cliente.md) · [EN](despliegue/cliente.en.md) · [中文](despliegue/cliente.zh-CN.md) | 玩家 | 安装模组、连接到服务器、按指纹配对、聊天命令 |
| preview 服务器 | [ES](servidor-preview.md) · [EN](servidor-preview.en.md) · [中文](servidor-preview.zh-CN.md) | 测试者 | 进入**官方测试服务器**，以及它的模组包要求和设备批准 |

## 参考

每份参考文档都有**西班牙语**（原文）、**英语**（`*.en.md`）和**简体中文**（`*.zh-CN.md`）三个版本。

| 文档 | 语言 | 内容 |
| --- | --- | --- |
| 主机 API | [ES](host-api.md) · [EN](host-api.en.md) · [中文](host-api.zh-CN.md) | 主机 v1 REST API：路由、认证、允许的远程命令与事件 |
| ADR | [索引](adr/) · [EN](adr/README.en.md) · [中文](adr/README.zh-CN.md) | 架构决策记录，每份都有这三种语言 |
| 架构 | [ES](../architecture.md) · [EN](../architecture.en.md) · [中文](../architecture.zh-CN.md) | 系统的技术架构 |
| 游戏设计 | [ES](../design.md) · [EN](../design.en.md) · [中文](../design.zh-CN.md) | 游戏设计 |
| 待办列表 | [ES](../backlog.md) | 技术路线图：史诗、任务及其解决情况（**仅有西班牙语**：这是一份 1700 行的历史记录） |
| 主机外观 | [ES](../agents/README.md) | 龙头像素画与像素画工具（**仅有西班牙语**） |

## Web 面板（不在 `main` 与 `dev` 中）

Web 面板**不在** `main` 与 `dev` 中：它位于 `feat/web-panel` 分支，并且已被重构为从
`assets/dashboard.html` 提供，而不是内嵌在 DLL 中的常量。REST API（`/api/v1/host/*` 与 `/api/*`）
不依赖面板，保持不变。

要恢复面板：

```bash
git checkout feat/web-panel -- Pelican4Ever/assets/dashboard.html \
    Pelican4Ever.Core/Admin/AdminApiLogic.cs Pelican4Ever/Modules/AdminApiModule.cs \
    Pelican4Ever.Tests/AdminApiLogicTests.cs scripts/
```

## 规矩

- 游戏是 **Stardew Valley 1.6.15**，搭配 **SMAPI 4.1+** 使用。
- 模组名为 **Pelican 4Ever**（`Pelican4Ever.SVRTMP`），当前版本 **0.2.0**。
- 服务器只公开**一个公共端口**：UDP `24642`。主机 API（`8085`）保留在回环地址上。
- `config.json` 在**启动时校验**：如果有任何不一致，模组不会注册。没有降级模式。
- 主机始终是一个本地玩家：**不存在原生的专用服务器模式**。选项 `DedicatedServerMode` 在
  当前代码中并未使用。
- 服务器要求的模组包版本是**精确版本**（而不是 *major.minor*）。模组自身的版本则按
  *major.minor* 比较。
- 每个玩家的身份是其**设备密钥**（ECDSA P-256），而不是 IP：更换 IP 不需要重新配对。

## 历史

已归档的文档位于 [`archive/`](archive/)，每份文档开头都有一则说明其状态的说明。未经审阅
不要删除：其中一些包含尚未移植到活跃指南中的技术细节（数据格式、bug 清单）。
