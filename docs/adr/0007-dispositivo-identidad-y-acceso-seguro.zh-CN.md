# ADR-0007：基于设备的不对称身份与 Lidgren 中的密码学挑战

* **状态：** 已接受
* **日期：** 2026-11-11
* **背景：** 第 18 号史诗（TASK-1801），取代了 [ADR-0004](0004-host-authority-client-mod-and-satiety.zh-CN.md) 中基于共享令牌与固定 IP 白名单的方案。

## 背景

对 Stardew Valley 1.6.15/SMAPI 直接通过 IP 多人游戏的访问，此前使用的是 IP 白名单和 72 小时的配对码。该方案存在三个关键的安全性与易用性局限：

1. **对 IP 的依赖作为身份：** 如果已授权玩家更换了 IP 地址（动态家庭网络、另一网络上的笔记本或移动数据），Lidgren 会以空的 `userId` 调用 `sendAvailableFarmhands`；服务器在尚未获知客户端身份之前就评估访问权限，并立即以「DeniedNoInvite」将其断开。
2. **凭据暴露（`OwnerToken`）：** 为了把农场工与客户端关联起来，主机会把 `OwnerToken` 复制到原生字段 `farmer.userID.Value`。在 Stardew Valley 中，可用农场工列表会发送给所有发起连接的客户端，从而把所有农场工的令牌暴露给网络上的任何观察者或客户端。同样，该令牌以明文形式存储在存档和 sidecar 文件 `pelican4ever_pairing.json` 中。
3. **共享密钥不可行（HMAC）：** 一个使用 HMAC-SHA256 的中期挑战原型要求服务器存储与客户端相同的密钥，对所有已知令牌执行 $O(N)$ 的搜索，并依赖一个已经在原生流量中暴露的令牌。

## 决策

1. **基于设备的不对称身份（ECDSA NIST P-256）：**
   - 每个客户端安装都会在 NIST P-256 标准下生成一对非对称密钥（`ECCurve.NamedCurves.nistP256`）。
   - 私钥绝不离开玩家的设备。
   - 服务器只存储公钥（标准 SubjectPublicKeyInfo / SPKI 格式）及其加密 SHA-256 指纹（`DeviceFingerprint`）。
   - 指纹和设备标识符属于公开数据，绝不是秘密凭据。

2. **在交付农场工之前的挑战-响应协议（`P4E\x02`）：**
   - 在 Lidgren 的 UDP 发现阶段（`sendVersionInfo`），主机发出一个挑战，其中包含：标记 `P4E\x02`、服务器会话标识符（UUID）、UTC 时间戳，以及一个一次性 32 字节加密 nonce，其有效期严格为 30 秒。
   - 客户端使用其 IEEE P1363 格式（64 字节）的私钥对挑战签名（`Contexto || SessionId || Timestamp || Nonce || HostEndpoint`），并将其附加在初始连接消息（`RemoteHailMessage`）中。
   - 在 `sendAvailableFarmhands` 中，主机针对已授权公钥验证签名，并通过弱映射（`ConditionalWeakTable`）把身份绑定到 `NetConnection` 实例。如果校验无效、已过期或未知，则连接 **在** 发送农场工列表 **之前** 就被拒绝。

3. **带外异步审批（Discord 私信 / Putre）：**
   - 新设备的登记不会通过游戏通道交换令牌或密码。所有者通过 Putre 的私信授权该具体设备的指纹。

4. **撤回网络与存储中的凭据：**
   - 移除把秘密令牌复制到 `farmer.userID.Value` 的做法。`userID` 字段只包含公开标识符，或对未被认领的农场工保持为空。
   - 移除通过 mod 消息（`ModMessages.PairingSubmit`）传输秘密与配对码的做法。

5. **明确的边界：**
   - 认证保证的是设备身份以及在进入存档之前对其农场工槽位的独占访问。存档期间 UDP 流量的加密不在本次范围内。

## 后果

* **积极面：**
  - 已授权玩家可以从任意 IP 进入，无需重新配对，也不会因动态 IP 被封禁。
  - 服务器不保管私钥或密码；即使存档文件或 sidecar 被窃取，也无法冒充任何玩家。
  - 拒绝发生在发出农场列表之前，保护了角色的隐私。
  - 恒定时间 $O(1)$ 的验证，没有因迭代式搜索令牌而导致拒绝服务的风险。
* **消极面：**
  - 要求服务器和客户端都使用支持 `P4E\x02` 协议的最新版 mod。过时的客户端会以明确的提示被安全拒绝。
