# Offset profiles

COTW memory layouts can change when the game updates. Build-specific addresses and signatures belong here rather than in application code.

Do **not** commit guessed offsets.

A profile should only be added after it has been verified against the matching game executable.

Example:

```json
{
  "name": "example-only",
  "gameBuild": "unknown",
  "executableSha256": null,
  "offsets": {
    "playerManager": "0x0000000000000000",
    "animalManager": "0x0000000000000000"
  }
}
```

The zero values above are placeholders and must never be treated as valid offsets.
