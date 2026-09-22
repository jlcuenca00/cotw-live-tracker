# COTW Live Tracker

A read-only Windows companion app for **theHunter: Call of the Wild**.

## Goal

Build a live tracker that can display currently spawned animals, their positions, species, distance from the camera, and useful herd-management information without modifying game memory or save data.

## Current status

### v0.3 live + population correlation diagnostic

The tracker can now:

- detect `theHunterCotW_F.exe`;
- open the process with read/query permissions only;
- fingerprint the running executable with SHA-256;
- load a versioned offset profile;
- validate the profile against live game structures;
- read camera XYZ;
- enumerate currently spawned animal pointers;
- decode species from the game's animal/bird ragdoll paths;
- read animal XYZ and current/max HP;
- calculate distance from the camera;
- continuously refresh in console watch mode;
- locate a reserve population save file without touching backup slot copies;
- decompress the read-only COTW population payload;
- recognize candidate 32-byte animal records containing gender, weight, score, Great One/scripted flags, visual-variation seed, ID and stored map position;
- compare those stored population map positions against live animal X/Z as a research diagnostic.

The population correlation is intentionally diagnostic in v0.3. Species-to-population grouping, trophy labels, fur-name decoding and the graphical radar are **not implemented yet**.

## Build

Requirements:

- Windows 10/11
- .NET 10 SDK
- 64-bit build

```powershell
dotnet build src/CotwLiveTracker/CotwLiveTracker.csproj -c Release
```

## Self-test

The self-test uses synthetic memory and does not require the game:

```powershell
dotnet run --project src/CotwLiveTracker/CotwLiveTracker.csproj -c Release -- --selftest
```

## Run one snapshot

Start theHunter: Call of the Wild, load into a reserve, then:

```powershell
dotnet run --project src/CotwLiveTracker/CotwLiveTracker.csproj -c Release
```

## Live watch

```powershell
dotnet run --project src/CotwLiveTracker/CotwLiveTracker.csproj -c Release -- --watch
```

Optional species filter:

```powershell
dotnet run --project src/CotwLiveTracker/CotwLiveTracker.csproj -c Release -- --watch --species "whitetail deer"
```

Optional refresh interval (100-10000 ms):

```powershell
dotnet run --project src/CotwLiveTracker/CotwLiveTracker.csproj -c Release -- --watch --interval-ms 500
```

## Population correlation diagnostic

For Layton Lake (population index 1), let the tracker find the active save automatically:

```powershell
dotnet run --project src/CotwLiveTracker/CotwLiveTracker.csproj -c Release -- --population-reserve 1
```

Or provide a population file explicitly:

```powershell
dotnet run --project src/CotwLiveTracker/CotwLiveTracker.csproj -c Release -- --population-file "C:\\path\\to\\animal_population_1"
```

The diagnostic prints the nearest candidate population records for each currently loaded animal. `MAPΔ` is the horizontal distance between the record's stored `MapPosition` and the live animal's X/Z. Small, repeatable values would validate a practical bridge between full-reserve population metadata and live runtime entities.

Use `--population-top 1` through `10` to change how many candidates are shown per live animal. Population correlation is currently one-shot and cannot be combined with `--watch`.

## Offset profile status

The bundled Patch 9.3 profile originated from a public community reverse-engineering source dated September 16, 2026 and has now been validated against a real Steam Patch 9.3 session.

The verified executable SHA-256 is pinned in the profile. The tracker first rejects a hash mismatch, then performs runtime sanity checks:

1. camera coordinates are finite;
2. the animal-manager pointer chain is plausible;
3. the spawned-animal vector has a valid shape and safe count;
4. loaded animal samples decode to plausible positions and species.

If the executable changes or those checks fail, the tracker stops instead of trusting stale offsets.

## Development principles

- **Read-only:** never write to game memory or edit saves.
- **Version-aware:** keep build-specific offsets isolated from application logic.
- **Fail closed:** reject stale or implausible structures rather than reading arbitrary memory.
- **Incremental:** verify each field before building higher-level HM/radar features.

## Roadmap

1. Validate population-record correlation against the live Patch 9.3 session.
2. Build a structured full-reserve population reader with species/group identity.
3. Derive difficulty/level and trophy labels from verified population weight/score data, then decode fur from the visual seed.
4. Match loaded runtime entities to their full population records.
5. Add a cached high-frequency entity reader and 2D radar/map UI.
6. Add HM-focused filtering and kill/respawn history.

## Source attribution

The initial Patch 9.3 memory layout was transcribed from:

https://gist.github.com/Cvar1984/b8425fd7e1df271509db5af8273a013f

That source states its offsets are for the September 16, 2026 `theHunterCotW_F.exe` build and may move after a game patch.


Population-record field semantics and the active-save decompression layout were independently cross-checked against the MIT-licensed Animal Population Changer - Pure Winter Edition:

https://github.com/Pure-Winter-hue/apc-pw

The tracker remains read-only and does not use APC's population modification or live injection features.
