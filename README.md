# COTW Live Tracker

A read-only Windows companion app for **theHunter: Call of the Wild**.

## Goal

Build a live tracker that can display currently spawned animals, their positions, species, distance from the camera, and useful herd-management information without modifying game memory or save data.

## Current status

### v0.11 calibrated Layton map overlay

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
- join loaded entities to exact population records and enrich live rows with difficulty, trophy and fur;
- launch a native WPF desktop app with dashboard, live radar, population scanner, filters, and patch/profile status;
- overlay live animals and the player on a calibrated Layton Lake map exported from the user's own COTW installation.

The CLI deliberately does not invent an "Uncommon" tier because rarity labels in game data are inconsistent across species.

## Desktop app

The first native Windows desktop shell lives in `src/CotwLiveTracker.Desktop`. It references the existing verified reader instead of duplicating memory or population logic.

Run it with:

```powershell
dotnet run --project src/CotwLiveTracker.Desktop/CotwLiveTracker.Desktop.csproj -c Release
```

Current desktop features:

- attach/reload against the verified Patch 9.3 profile;
- choose a reserve and load its active population file;
- one-second live refresh;
- dashboard counts for loaded/resolved/Diamond/Rare/Great One animals;
- calibrated Layton Lake actual-map overlay using absolute game world X/Z coordinates;
- automatic fallback to the north-up relative radar when no compatible map image is installed;
- clickable map/radar markers and selected-animal detail panel;
- live animal table with difficulty, trophy, weight, score and fur;
- full-reserve population scanner with species/trophy/difficulty/fur/sex/Rare/Great One filters;
- runtime profile, SHA-256 and read-only access status.

### Layton actual-map setup

v0.11 includes a verified Layton world-to-map calibration, but deliberately does **not** redistribute the game's map artwork. Import the map image exported from your own COTW installation:

1. Download a Windows DECA release from `kk49/deca`, unzip it, and run `deca_gui.exe`.
2. In DECA use **File -> New Project**, select your `theHunterCotW_F.exe`, and let the first archive index complete.
3. In the DIR view navigate to `textures/ui/map_reserve_1/zoom3/`.
4. Select a tile in that map set, enable **Export Map**, choose **Full**, then click **EXTRACT**.
5. DECA's map exporter stitches the highest-resolution map tiles into `deca.map/full.png`.
6. In COTW Live Tracker open **Live Radar -> Load actual map** and select that `full.png`.

The compatible Layton full image is the game's square 16x16 zoom-3 tile set (tiles 0-255 in row-major order). Cropped screenshots or web-guide maps are not calibration-compatible.

The imported image is copied only to the user's local application-data folder and is never added to this repository.

#### Layton calibration

The transform maps game world `(X, Z)` directly to normalized image `(u, v)`:

```text
u = 0.000114888268968409 * X
  - 0.0000000459197071221501 * Z
  - 0.589623002016760

v = 0.000000306147534157835 * X
  + 0.000114541294189928 * Z
  - 0.412792441489212
```

It was fit against ten Layton outpost correspondences and independently checked against the live camera location near Roonachee. The tiny cross-axis coefficients are retained so the calibration does not assume zero rotation.

DECA and an independently implemented COTW map viewer were also used to verify that the full Layton map uses a square 16x16 highest-zoom tile grid with row-major tile indexing, matching DECA's `export_map` stitch order.

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

1. Validate the Layton actual-map overlay against the user's DECA-exported `full.png`.
2. Add verified calibration profiles for the remaining reserves.
3. Add cached/high-frequency refresh and marker filtering/highlighting controls.
4. Add HM-focused herd tools and kill/respawn history.
5. Package the desktop app as a normal Windows release.

## Source attribution

The initial Patch 9.3 memory layout was transcribed from:

https://gist.github.com/Cvar1984/b8425fd7e1df271509db5af8273a013f

That source states its offsets are for the September 16, 2026 `theHunterCotW_F.exe` build and may move after a game patch.


Population-record field semantics and the active-save decompression layout were independently cross-checked against the MIT-licensed Animal Population Changer - Pure Winter Edition:

https://github.com/Pure-Winter-hue/apc-pw

The tracker remains read-only and does not use APC's population modification or live injection features. The ADF reader was implemented independently in C# from the documented structure semantics.


### Map calibration research

The full-map stitch order and COTW map asset paths were cross-checked against the MIT-licensed DECA project:

https://github.com/kk49/deca

Normalized Layton map-location geometry was independently cross-checked against the GPL-licensed COTW Companion project. No COTW Companion source code or map artwork is copied into this repository:

https://github.com/janstehno/cotw-companion
