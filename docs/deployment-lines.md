# Líneas de despliegue: preproducción y desarrollo

Dos líneas Git locales por proyecto, cada una en su worktree, y dos instancias del juego en la misma
máquina. **Desarrollo corre sin red y preproducción con red.** No hay namespaces, veth ni proxies.

| | Preproducción | Desarrollo |
|---|---|---|
| Rama Pelican | `main` (`~/src/Pelican-4Ever-preprod`) | `pelican/desarrollo` (`~/src/Pelican-4Ever-development`) |
| Rama Voyager | `pelican/preproduccion` (`~/src/stardew-voyager-preprod`) | `pelican/desarrollo` (`~/src/stardew-voyager-development`) |
| Juego | copia en `~/.local/share/pelican4ever/preprod/game` | instalación de Steam |
| Mods | solo SMAPI (ConsoleCommands, SaveBackup), Pelican 4Ever y el puente | los de la campaña |
| Saves | `preprod/xdg/config/StardewValley/Saves` | `~/.config/StardewValley/Saves` |
| Partida | `Pelican4Ever_453180189` (dedicada, creada desde el juego) | las de campaña y depuración |
| Red de juego | UDP 24642 nativo, abierto solo a la LAN (firewalld, `192.168.1.0/24`) | ninguna: no aloja cooperativo |
| API Pelican | `127.0.0.1:18085` | — |
| Puente Voyager | `127.0.0.1:25557` | `127.0.0.1:25555` |
| Pantalla | Xvfb `:77` (`pelican4ever-preprod-xvfb.service`) | sesión de Juanlu |
| Unidad | `pelican4ever-preprod.service` (usuario, `enabled`, linger) | la del arnés de campaña |

Stardew 1.6.15 fija el puerto UDP 24642 en `LidgrenServer.initialize()`. Por eso solo una instancia
puede alojar partidas en red: preproducción. Desarrollo no aloja cooperativo. Si alguna vez necesita
probar multijugador, se lanza junto con su arnés dentro de un entorno sin red
(`systemd-run --user -p PrivateNetwork=yes …`) y nunca abre puertos.

## Compilar

`global.json` fija el SDK **8.0.427** (`~/.dotnet`; el 8.0.132 del sistema falla con CS9057 ante el
analizador de ModBuildConfig). Con `DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`:

```bash
dotnet build Pelican4Ever.sln -c Release -p:EnableModDeploy=false -p:EnableModZip=false
dotnet test Pelican4Ever.Tests -c Release --no-build
# solo desde el worktree de preproducción, con el servicio parado:
dotnet build Pelican4Ever/Pelican4Ever.csproj -c Release -p:GamePath=$HOME/.local/share/pelican4ever/preprod/game -p:EnableModZip=false
```

Desarrollo compila siempre con `EnableModDeploy=false`: nunca despliega en la copia de preproducción
ni en la instalación de Steam mientras haya campaña.

## Promoción y rollback

1. Corregir y probar en `pelican/desarrollo` (suite completa verde, 0 avisos).
2. Llevar los commits a `main` (la línea de preproducción de Pelican) con `git cherry-pick -x`.
   Volver a pasar la suite allí.
3. Parar `pelican4ever-preprod.service`, compilar y desplegar desde el worktree de preproducción,
   anotar el hash del DLL en `runtime.json`, arrancar y comprobar el anfitrión (puente
   `worldReady/isMultiplayer/isHost`, UDP 24642 y `GET /api/v1/host/clock`).
4. Rollback: parar el servicio, reconstruir desde el SHA de preproducción anterior registrado en
   `runtime.json` y arrancar. Nunca se apunta preproducción a la instalación ni a los Saves de
   desarrollo, y la partida no se edita a mano: si hiciera falta, se restaura desde
   `Pelican4Ever_Backups/`.

## Cascada automática de ramas

`.github/workflows/cascada-ramas.yml` mantiene `main` → `pelican/desarrollo` →
`refactor/residence-entrances`: con cada push a `main` o a `pelican/desarrollo` fusiona la rama
superior en la inferior y sube el resultado. Si una fusión choca, no toca la rama de destino: sube
`cascada/<origen>-a-<destino>` y abre un PR hacia el destino para resolverlo a mano. Por eso
`refactor/residence-entrances` ya no se rebasa: recibe merges, y un rebase obligaría a forzar el push.

El token de la API y `host.env` viven en `~/.config/pelican4ever/preprod/` (modo 0600) y nunca se
versionan.
