# Laboratorio Multicliente en Vivo, Diagnóstico Profundo y Benchmarks

Conocimiento consolidado de la verificación `v0.5.0` (hasta **16 clientes reales concurrentes** y **64 residencias pobladas**), [`live-agent-tests-backlog.md`](../../../live-agent-tests-backlog.md) y [`v0.5-residence-scaling-verification-2026-11-17.md`](../../../docs/verifications/v0.5-residence-scaling-verification-2026-11-17.md).

---

## 1. Preparar un Save Dummy Seguro en Entorno de Laboratorio

**Regla estricta:** jamás ejecutes pruebas destructivas, `EnableTestCommands: true` o `p4e_farm_populate` sobre una partida viva de producción.

1. Solicita checkpoint previo (`POST /api/v1/host/checkpoint`) y espera el evento `checkpoint.created` en `GET /api/v1/host/events?since=N`.
2. Clona la carpeta de guardado a un identificador dummy separado (`999999999`), renombra ambos ficheros internos (`<SaveName>` y `<SaveName>_old`) y sustituye `<uniqueIDForThisGame>` **tanto en el XML principal como en `SaveGameInfo`**.
3. Para el tier **64 residencias pobladas** (`VmHWM ≈ 4.94 GB`), recuerda que `MemoryMax=4G` estándar queda corto (`>= 6G` requerido en laboratorio). En el arranque del lab: `EnableTestCommands: true`, `RequirePairingWhitelist: false`.

---

## 2. Levantar Múltiples Clientes Reales Locales (`Xephyr :98` + Aislamiento)

Para conectar clientes reales en paralelo en Linux sin interferir con el escritorio del usuario:

1. **Servidor X anidado sin TCP (`Xephyr`):**
   ```bash
   DISPLAY=:1 Xephyr :98 -screen 1280x720 -nolisten tcp -ac > /tmp/p4e-lab/xephyr.log 2>&1 &
   DISPLAY=:98 xdpyinfo >/dev/null && echo "XEPHYR_OK"
   ```
2. **Aislamiento estricto por cliente (`c01..c16`):**
   - Directorios separados `/tmp/p4e-lab/cXX/{xdg,cache,mods}` (`XDG_CONFIG_HOME`, `XDG_DATA_HOME`, `XDG_CACHE_HOME`).
   - `DISPLAY=:98`, `LP_NUM_THREADS=1`, `SDL_AUDIODRIVER=dummy`, `ALSOFT_DRIVERS=null`.
   - **`mods/SMAPI-config.json` = `{"ListenForConsoleInput": false}`** y `stdin=subprocess.DEVNULL` (evita 100 % CPU leyendo EOF en bucle).
   - En `cXX/mods/Pelican4Ever/config.json`: `"AutoHostSaveName": ""`, `"AdminApiPort": 0`, `"Modules": {"AdminApi": false}`.
   - Lanzamiento: `"$GAME_PATH/StardewModdingAPI" --mods-path "/tmp/p4e-lab/cXX/mods"`.

---

## 3. Patrones Clave para Bots Sintéticos SMAPI (`LabClient` / `V05LabClient`)

Hallazgos confirmados al decompilar `Stardew Valley.dll`, `Lidgren.Network.dll` y `StardewModdingAPI.dll`:

1. **Conectar sin menú manual (`TitleMenu` → `FarmhandMenu`):**
   - `var client = multiplayer.InitClient(new LidgrenClient(host)); TitleMenu.subMenu = new FarmhandMenu(client);`
   - **No esperes a `client.readyToPlay` antes de elegir ranura:** pasa a `true` *después* de elegir peón. Espera `!fh.gettingFarmhands` y consulta la propiedad `fh.MenuSlots`.
2. **Bombeo cuando la ventana pierde foco (`Alt-Tab` o múltiples clientes en `Xephyr`):**
   - Durante `gameMode == 6` (`loadingMode`), SMAPI **suspende `GameLoop.UpdateTicked`** y solo dispara **`Specialized.UnvalidatedUpdateTicked`**. Por eso `ClientSecurityModule.OnUnvalidatedUpdateTicked` bombea `FarmhandMenu.update()`.
3. **Mensajes de cola `OutgoingMessage(11)` (`connectionMessage`):**
   - `Client.processIncomingMessage` (tipo `11`) pasa texto libre por `Game1.content.LoadString(...)` (`ContentLoadException` si hay `:`). `Pelican4Ever` usa el prefijo **`Strings\UI:P4E_Queue:`** limpiando `timeoutTime` y marcando `hasHandshaked = true` (si no, `Client.connect()` desconecta a los **45 s**).
4. **Protocolo de diálogo y renderizado:**
   - `FarmSlotModule.OnModMessageReceived` filtra `e.FromModID == "Pelican4Ever.SVRTMP"`.
   - `SuppressDraw` ahorra CPU en clientes sintéticos, pero mantén render activo cuando `Game1.activeClickableMenu is SaveGameMenu` o `TitleMenu`.

---

## 4. Benchmarks de Referencia (2 vCPU, `LP_NUM_THREADS=1`, 2026-11-17)

| Nivel `FarmSlotCount` | Checkpoint (`ms`) | XML (`MB`) | Rollover (`s`) | Pico `VmHWM` (`GiB`) | Estado bajo `MemoryMax=4G` |
|---:|---:|---:|---:|---:|---|
| `16` | `3.820` | `34.6` | `9.29` | `2.66` | Nominal |
| `32` | `6.551` | `55.5` | `16.06` | `3.45` | **Máximo recomendado en producción** |
| `64` | `8.916` | `97.3` | `32.44` | `4.71` | **Experimental** (exige `MemoryMax>=6G`) |

*(Nota: los detalles privados de SSM, instancias EC2 e iptables/nftables residen en la skill privada `pelican-aws-ops` de `MCP Pelican`.)*
