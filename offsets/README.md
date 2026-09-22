# Offset profiles

COTW memory layouts can change when the game updates. Build-specific addresses and structure offsets live here rather than in application code.

## Safety rules

- Do **not** commit guessed offsets.
- Prefer profiles tied to a known executable SHA-256.
- A community-derived profile without a hash is treated as a candidate and must pass runtime structure validation.
- The tracker is read-only. Offset profiles must never introduce write behavior.

## Current candidate

`patch-9.3-2026-09-16-community.json` is transcribed from the public September 16, 2026 COTW DMA tracker by Cvar1984:

https://gist.github.com/Cvar1984/b8425fd7e1df271509db5af8273a013f

The source identifies these offsets as belonging to the `theHunterCotW_F.exe` build of 2026-09-16 and explicitly warns that a game patch can move them.

The application therefore validates the camera, animal-manager pointer chain, vector shape, count bounds, and sample animal records before accepting the profile.

Once we verify the profile on a local executable, its SHA-256 should be recorded in the profile.
