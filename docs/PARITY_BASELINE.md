# Phaser parity baseline

This document records the source baseline used by the migration. It is historical migration evidence, not product architecture.

## Source reference

- Repository: `C:\\ws\\solo_vs_mortal`
- Git revision: `f867a04f4ff2726fb55c8c25ba91cdd707e934f8`
- Runtime: TypeScript 5.6, Phaser 3.85, Vite 5.4
- Viewport: 960 × 540, fit and centered, pixel-art rendering
- Scene order: Boot, Preload, Arena
- Save key: `solo_vs_mortal_save_v1`
- Current save schema: version 5; versions 1–5 remain readable
- Inventory at baseline: 79 TypeScript/JSON source files, 36 Vitest files, 2,458 asset files

## Canonical behavior sources

Runtime code and canonical design documents take precedence over historical changelog entries. Deterministic parity must be captured for:

- Vector/rectangle math, UID formatting, Mulberry32 RNG, and simulation clock ordering.
- Rank, CP/stat rounding, movement speed, attack cadence, and progression.
- Player, monster, combat, damage, drops, and world collision outcomes.
- Soul acquisition/progression, banners, summons, devouring, Essence, Bloodline, and Possession.
- Capability aggregation and destructible world-object state.
- Save versions 1–5 and legacy banner aliases.

## Asset migration policy

The source manifest and JSON descriptors remain the logical-ID authority during parity work. Assets are copied per vertical slice rather than importing all 2,458 files immediately. Runtime state and saves reference stable logical IDs, never imported Godot paths.
