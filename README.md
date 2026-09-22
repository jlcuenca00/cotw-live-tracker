# COTW Live Tracker

A read-only Windows companion app for **theHunter: Call of the Wild**.

## Goal

Build a live tracker that can display currently spawned animals, their positions, species, distance from the camera, and useful herd-management information without modifying game memory or save data.

## Current status

### v0.2 live animal probe

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
- continuously refresh in console watch mode.

Trophy score, difficulty/level, fur and the graphical radar are **not implemented yet**.

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

## Offset profile status

The bundled Patch 9.3 profile comes from a public community reverse-engineering source dated September 16, 2026. It is intentionally marked **community-derived** and currently has no executable SHA-256.

The tracker does not blindly trust it. Before listing animals it checks:

1. camera coordinates are finite;
2. the animal-manager pointer chain is plausible;
3. the spawned-animal vector has a valid shape and safe count;
4. loaded animal samples decode to plausible positions and species.

If those checks fail, the tracker stops.

After the profile is confirmed on a local executable, we can pin its SHA-256 for a stronger build check.

## Development principles

- **Read-only:** never write to game memory or edit saves.
- **Version-aware:** keep build-specific offsets isolated from application logic.
- **Fail closed:** reject stale or implausible structures rather than reading arbitrary memory.
- **Incremental:** verify each field before building higher-level HM/radar features.

## Roadmap

1. Verify Patch 9.3 offsets on a real local game process.
2. Add difficulty/level, trophy estimate and fur after their fields are independently verified.
3. Add a cached high-frequency entity reader.
4. Add a 2D radar/map UI.
5. Add HM-focused filtering and kill/respawn history.

## Source attribution

The initial Patch 9.3 memory layout was transcribed from:

https://gist.github.com/Cvar1984/b8425fd7e1df271509db5af8273a013f

That source states its offsets are for the September 16, 2026 `theHunterCotW_F.exe` build and may move after a game patch.
