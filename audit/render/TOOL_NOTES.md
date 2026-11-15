# MapRenderer tool notes

## How to run
```
cd /home/dr4co/src/pelican-minimaps/tools/MapRenderer
dotnet run
```
No game runtime required. Reads unpacked TMX/PNG from Content (unpacked)/Maps.

## Calibration: sprite anchoring rule
Chosen rule: anchor each sprite by its bottom edge — draw sprite with bottom at (tileY+1)*16 and top at (tileY+1)*16 - spriteHeight (extends upward). Back 16x16 tiles fill their cell; taller sprites extend upward. Calibrated against vanilla Farm/BusStop full renders.

## Layer order
Back, Buildings, Buildings2, Front, Front2, AlwaysFront, AlwaysFront2, AlwaysFront3 (Paths skipped).

## Issues
- AnnexEntranceStyle/AnnexMapDesignLogic not available in committed code; entrance style reported via local mapping matching AnnexMapDesignLogicTests expectations.
