# COTW Live Tracker

A read-only Windows companion app for **theHunter: Call of the Wild**.

## Goal

Build a live tracker that can display currently spawned animals, their positions, species, distance from the camera, and useful herd-management information without modifying game memory or save data.

## Current status

### v0.9 promoted live identity fields

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
- parse the APEX ADF header, name table, type definitions, structures and arrays;
- walk the real `Populations -> Groups -> Animals` hierarchy;
- associate each population slot with the reserve's species order;
- decode each animal's gender, weight, saved score, Great One/scripted flags, visual-variation seed, ID and stored map position;
- summarize full-reserve totals and herd/group membership by species;
- derive species-specific difficulty labels from saved weight;
- classify saved trophy score as Bronze, Silver, Gold, Diamond or Great One;
- decode fur type from VisualVariationSeed;
- show each fur's configured probability and flag normal furs below 1% as Rare;
- filter full-reserve records by trophy, rare fur, Great One, difficulty, fur and sex;
- print every matching record with `--population-all`;
- run a targeted read-only identity probe against loaded animal objects and their immediate pointers;
- read verified live weight, score and visual seed directly from Patch 9.3 animal entities;
- join loaded entities to exact population records and enrich live rows with difficulty, trophy and fur.

The CLI deliberately does not invent an "Uncommon" tier because rarity labels in game data are inconsistent across species. The graphical radar is **not implemented yet**.

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

## Full-reserve population scan

For Layton Lake (population index 1), let the tracker find the active save automatically:

```powershell
dotnet run --project src/CotwLiveTracker/CotwLiveTracker.csproj -c Release -- --population-reserve 1
```

Or provide a population file explicitly together with its reserve index:

```powershell
dotnet run --project src/CotwLiveTracker/CotwLiveTracker.csproj -c Release -- --population-reserve 1 --population-file "C:\\path\\to\\animal_population_1"
```

The structured reader prints reserve-wide species totals, group counts, male/female counts, Great One/Diamond/rare-fur counts, maximum saved weight/score, and the top population records for each species. Detailed rows include difficulty, trophy medal, decoded fur and fur probability.

Use `--population-top 1` through `20` to change how many top matching records are shown per species. Use `--species "whitetail"` to narrow both the live list and population output.

Population filters:

```powershell
# Every rare Whitetail on Layton
dotnet run --project src/CotwLiveTracker/CotwLiveTracker.csproj -c Release -- --population-reserve 1 --species whitetail --rare --population-all

# Every Diamond Coyote
dotnet run --project src/CotwLiveTracker/CotwLiveTracker.csproj -c Release -- --population-reserve 1 --species coyote --trophy diamond --population-all

# Male Legendary animals with Albino fur
dotnet run --project src/CotwLiveTracker/CotwLiveTracker.csproj -c Release -- --population-reserve 1 --difficulty 9 --fur albino --sex male --population-all
```

Supported filters are `--trophy none|bronze|silver|gold|diamond|great-one`, `--rare`, `--great-one`, `--difficulty 1..10`, `--fur <name>`, `--sex male|female`, and `--population-all`. Filters can be combined.

Population reading is currently one-shot and cannot be combined with `--watch`.

## Experimental live-to-population identity probe

The game keeps runtime copies of population animal records. APC-PW's live-population logic validates these records using the stable first 20 bytes: gender, weight, saved score, Great One/scripted flags, and visual seed.

v0.8 adds a **read-only** diagnostic that checks only each loaded animal object plus immediate pointers for those signatures. It does not scan or write arbitrary process memory.

Example:

```powershell
dotnet run --project src/CotwLiveTracker/CotwLiveTracker.csproj -c Release -- --population-reserve 1 --species whitetail --population-top 1 --bridge-probe --bridge-limit 8
```

A bridge result is marked `RESOLVED` only when the probe finds a full stable-record signature, or at least two independent exact clues that agree on one population record. A single seed or weight/score clue is reported only as a candidate.

Real Patch 9.3 validation resolved 4/4 loaded Whitetails using exact weight+score at entity+0x19C and visual seed at entity+0x1B0. Those fields are now promoted into the Patch 9.3 offset profile. The bridge probe remains available as a diagnostic for future patches and additional validation.

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

1. Validate promoted live identity fields across more species/reserves.
2. Add the scanner/filter model to the desktop UI.
3. Add a cached high-frequency entity reader and 2D radar/map UI.
4. Add HM-focused filtering and kill/respawn history.

## Source attribution

The initial Patch 9.3 memory layout was transcribed from:

https://gist.github.com/Cvar1984/b8425fd7e1df271509db5af8273a013f

That source states its offsets are for the September 16, 2026 `theHunterCotW_F.exe` build and may move after a game patch.


Population-record field semantics and the active-save decompression layout were independently cross-checked against the MIT-licensed Animal Population Changer - Pure Winter Edition:

https://github.com/Pure-Winter-hue/apc-pw

The tracker remains read-only and does not use APC's population modification or live injection features. The ADF reader was implemented independently in C# from the documented structure semantics.
