# 架构决策记录（ADR）

ADR 是一项**决策**及其理由：当背景变化时不会重写，而是由另一个 ADR 取代。下表是索引；
细节在各自的文件中。

| ADR | 决策 | 状态 | 日期 |
| --- | --- | --- | --- |
| [0001](0001-native-daily-rollover.md) · [EN](0001-native-daily-rollover.en.md) · [中文](0001-native-daily-rollover.zh-CN.md) | 原生换日，时间为 **02:00**，黎明为 **06:00** | 已接受 | 2026-11-02 |
| [0002](0002-realtime-clock-and-epoch-calendar.md) · [EN](0002-realtime-clock-and-epoch-calendar.en.md) · [中文](0002-realtime-clock-and-epoch-calendar.zh-CN.md) | 1:1 时钟（`0600–2550`），带 `NightHold` 与由 `CalendarEpoch` 推导的日历 | 已接受 | 2026-10-29 |
| [0003](0003-declarative-farm-slots.md) · [EN](0003-declarative-farm-slots.en.md) · [中文](0003-declarative-farm-slots.zh-CN.md) | `Data/Locations` 中声明式的农场槽位（标准模板） | 已接受（由 0005 扩展） | 2026-10-29 |
| [0004](0004-host-authority-client-mod-and-satiety.md) · [EN](0004-host-authority-client-mod-and-satiety.en.md) · [中文](0004-host-authority-client-mod-and-satiety.zh-CN.md) | 权限归于主机，客户端强制安装模组，模块为 `IModule` | 已接受；第 3 点（每日饱腹值预算）由 0006 取代 | 2026-10-29 |
| [0005](0005-host-plaza-and-bridge-maps.md) · [EN](0005-host-plaza-and-bridge-maps.en.md) · [中文](0005-host-plaza-and-bridge-maps.zh-CN.md) | 主机农场上的中央广场与声明式的桥接地图 | 已接受 | 2026-11-03 |
| [0006](0006-session-energy-satiety-wellfed-spa.md) · [EN](0006-session-energy-satiety-wellfed-spa.en.md) · [中文](0006-session-energy-satiety-wellfed-spa.zh-CN.md) | 按会话计算精力：持续消耗的饱腹值、坐着时的「吃饱」和温泉增益 | 已接受（取代 0004 的第 3 点） | 2026-11-06 |
| [0007](0007-dispositivo-identidad-y-acceso-seguro.md) · [EN](0007-dispositivo-identidad-y-acceso-seguro.en.md) · [中文](0007-dispositivo-identidad-y-acceso-seguro.zh-CN.md) | 按设备的非对称身份（ECDSA P-256）与 Lidgren 上的 `P4E\x02` 质询 | 已接受 | 2026-11-11 |

## 如何阅读

- **背景：** 为什么当初需要这项决策。
- **决策：** 决定了什么，以其确切的措辞。
- **后果：** 有什么被绑定到该决策上。

## 规则

1. ADR 不会为了改变决策而编辑：它被标记为*已取代*，并另写一个。
2. 所引用的数字（端口、时刻、上限）是**规范性的**：代码必须与之一致。
3. 每一项影响数据格式、协议、日历或权限模型的新决策都值得写一份 ADR。
