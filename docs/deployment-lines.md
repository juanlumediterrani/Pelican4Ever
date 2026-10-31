# Deployment lines

Two local Git lines separate release candidate from active development. `pelican/preproduccion` currently starts at 072764f; `pelican/desarrollo` also starts there. This is not a stable promotion: same mod source SHA, and no live service acceptance yet. Development changes must not deploy into the preproduction game.

The Pelican preproduction project is built only from this worktree with `local.props` pointing to `~/.local/share/pelican4ever/preprod/game`. Its tracked unit files are `deploy/systemd/pelican4ever-preprod.service` and `pelican4ever-preprod-xvfb.service`; installation is user-local and remains disabled until an isolated new save exists. Do not use the upstream single-instance unit or its `Conflicts=stardew-voyager-live.service`.

API `127.0.0.1:18085` and Voyager bridge `127.0.0.1:25557` are isolated. The repo documents native multiplayer as UDP `24642`; no configurable game-port setting was found in its server config, so it currently collides with development. Do not launch two hosts until a real supported isolation/port solution is tested. No firewall/router exposure is authorized. Admin token/config are local mode-0600 files and never committed.

Promotion requires clean clone tests, analyzer-compatible build, isolated bridge+Pelican builds, runtime verifier green, native new-save smoke, and failure/recovery proof. Until then report candidate, not stable.
