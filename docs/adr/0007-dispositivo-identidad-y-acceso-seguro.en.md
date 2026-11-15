# ADR-0007: Asymmetric Identity per Device and Cryptographic Challenge in Lidgren

* **Status:** Accepted
* **Date:** 2026-11-11
* **Context:** Epic 18 (TASK-1801), replaces the shared token and fixed IP whitelist scheme of [ADR-0004](0004-host-authority-client-mod-and-satiety.en.md).

## Context

Access to the direct-IP Stardew Valley 1.6.15/SMAPI multiplayer server used an IP whitelist and a 72-hour pairing code. This scheme had three critical security and usability limitations:

1. **Dependence on the IP as identity:** If an authorized player changed IP address (dynamic home network, laptop on another network or mobile data), Lidgren called `sendAvailableFarmhands` with an empty `userId`; the server evaluated the access before knowing the client's identity and disconnected it immediately with "DeniedNoInvite".
2. **Credential exposure (`OwnerToken`):** To associate the farmhand with the client, the host copied the `OwnerToken` to the native field `farmer.userID.Value`. In Stardew Valley, the list of available farmhands is sent to all clients that start connecting, exposing the tokens of all farmhands to any observer or client on the network. Likewise, the token was stored in plain text in the save and in the sidecar `pelican4ever_pairing.json`.
3. **Inviability of shared secrets (HMAC):** An intermediate prototype of the HMAC-SHA256 challenge required the server to store the same secret as the client, to run an $O(N)$ search over all known tokens and to depend on a token that was already exposed in the native traffic.

## Decision

1. **Asymmetric identity per device (ECDSA NIST P-256):**
   - Each client installation generates an asymmetric key pair in the NIST P-256 standard (`ECCurve.NamedCurves.nistP256`).
   - The private key never leaves the player's device.
   - The server stores only the public key (standard SubjectPublicKeyInfo / SPKI format) and its cryptographic SHA-256 fingerprint (`DeviceFingerprint`).
   - The fingerprint and the device identifier are public data, never secret credentials.

2. **Challenge-response protocol before the farmhand delivery (`P4E\x02`):**
   - During Lidgren's UDP discovery (`sendVersionInfo`), the host issues a challenge that contains: the `P4E\x02` marker, the server session identifier (UUID), a UTC timestamp and a single-use 32-byte cryptographic nonce with a strict 30-second expiry.
   - The client signs the challenge (`Context || SessionId || Timestamp || Nonce || HostEndpoint`) with its private key in IEEE P1363 format (64 bytes) and attaches it in the initial connection message (`RemoteHailMessage`).
   - In `sendAvailableFarmhands`, the host validates the signature against the authorized public key and binds the identity to the `NetConnection` instance through a weak map (`ConditionalWeakTable`). If the proof is invalid, expired or unknown, the connection is rejected **before** sending the farmhand list.

3. **Out-of-band asynchronous approval (Discord DM / Putre):**
   - The registration of new devices does not exchange tokens or passwords over the game channel. The owner authorizes the concrete device fingerprint by direct message with Putre.

4. **Withdrawal of credentials from the network and storage:**
   - The copy of secret tokens to `farmer.userID.Value` is removed. The `userID` field will only contain public identifiers or stay empty for unclaimed farmhands.
   - The transmission of secrets and codes through mod messages (`ModMessages.PairingSubmit`) is removed.

5. **Explicit limits:**
   - Authentication guarantees the identity of the device and the exclusive access to its farmhand slot until entering the save. Encryption of the UDP traffic during the save is out of scope.

## Consequences

* **Positive:**
  - Authorized players can join from any IP without pairing again or suffering dynamic-IP blocks.
  - The server does not hold private keys or passwords; stealing the save file or the sidecar does not allow impersonating any player.
  - The rejection happens before the farmhand list is emitted, protecting the privacy of the characters.
  - Constant-time $O(1)$ verification with no risk of denial of service from an iterative token search.
* **Negative:**
  - It requires both the server and the clients to use the updated mod version with `P4E\x02` protocol support. Outdated clients are safely rejected with an explicit message.
