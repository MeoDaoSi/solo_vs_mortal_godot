# Runtime and export acceptance budget

These are initial measurable runtime and export gates for the game. They may be tightened with representative-content profiling; changes require recorded evidence.

## Reference scenario

- Desktop build at 1280 × 720 using the production renderer.
- One arena, player, representative HUD, 20 active monsters, 5 allies, Soul drops, combat VFX, and periodic save enabled.
- Measure after shader/import warm-up, outside editor-only tooling overhead.

## Initial gates

- Sustain 60 updates/frames per second on the project reference machine, with a 30 FPS fallback target documented for lower-end hardware.
- Simulation uses a stable fixed step and remains deterministic when rendering cadence varies.
- No unbounded growth in nodes, subscriptions, runtime state collections, or managed allocations during a 15-minute arena soak.
- Scene changes and save/load do not retain gameplay instances from the previous session.
- Texture import settings preserve pixel-art edges and remain within a measured memory budget established after the first complete asset slice.
- Desktop export launches without editor dependencies and reports no unhandled errors during the playable parity route.

## Evidence to record

Record engine version, export preset, hardware, representative content counts, profiler captures, average/worst frame time, managed allocation trend, texture memory, and any accepted exceptions.
