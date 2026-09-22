# COTW Live Tracker

A read-only Windows companion app for **theHunter: Call of the Wild**.

## Goal

Build a live tracker that can eventually display currently loaded animals, their positions, species, distance from the player, and useful herd-management information without modifying game memory or save data.

## Development principles

- **Read-only:** never write to game memory or edit saves.
- **Version-aware:** keep build-specific offsets isolated from application logic.
- **Incremental:** prove process detection and memory reads before building the radar/UI.
- **Safe by default:** fail closed when the game build or offsets are unknown.

## v0.1 milestone

1. Detect the running COTW process.
2. Attach with read-only process permissions.
3. Report process/module information.
4. Add a versioned offset configuration system.
5. Then implement player position and loaded-animal enumeration once offsets are verified.

## Status

Early development.
