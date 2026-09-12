# Native readability contract v1 — P3.0

Applies to newly produced world raster assets. Preserve the accepted ash/soulfire palette and top-down three-quarter camera. Stardew Valley is a recognition benchmark only; do not copy its style or content. This contract supersedes the older 40–44px Player suggestion and enlarged-preview defaults, not gameplay authority.

## Brief before generation

Record the canonical AssetId, semantic identity, canvas, foot/base anchor, intended visible height, World Scale policy path/hash, and final raster scale. World Scale owns physical ratios; use the consuming project's actual policy (currently Player 55px). Do not independently choose the physical size in art scripts. Author near the final visible pixel count; architectural props need appropriately versioned larger canvases instead of generic 64px images magnified three times.

Record a family-wide source crop, scale, body landmarks and palette. Apply one uniform transform across actor directions/clips, anchored to an observed foot landmark. Do not independently bbox-fit each direction or use opaque bottom containing cloth/slash as the foot. Source cells require reviewed crop rectangles, not guessed equal subdivisions. Packing preserves pixels, canvas, anchor and authored timing; repeated poses do not become animation through duplication.

## Recognition requirements

Semantic silhouette and large masses precede medium structure and decoration. Player must read as a human warrior; head, torso, arms, two legs and any equipped weapon must separate. Do not invent equipment to compensate for missing sockets. Preserve actual weapon/body separation when the art brief includes a weapon. Dark armor needs connected material planes, not isolated shiny dots. Pillar: shaft/base/capital. Chest: lid/body/lock. Shrine: raised ritual platform/altar. Portal: substantial frame and open gate. Grave: unmistakable upright marker/base.

Readability does not excuse loss of the accepted visual style. Review material character and construction separately: retain selected wood/metal/stone structure, proportions and meaningful medium detail. The user rejected P3 chest revision 2 for poor appearance; extreme simplification into crude flat blocks is not an accepted direction. Preserve rejected sources and decisions, exclude them from integration/reference use, and revise the brief before further generation.

At final gameplay size, use at least **2px thickness** for identity-bearing limbs, weapon blades, horns, handles and architectural edges; major limb/armor/stone masses should normally be 3px or wider. Use at least 2px of negative space where a gap is the identity cue. A deliberate 1px exception needs a named feature and native evidence demonstrating survival, not a blanket exemption. Fine symbols may decorate; recognition must not depend on reading them. These are raster requirements, not collider or reach changes.

Use connected intentional clusters and a few material ramps. Reject scattered single-pixel highlights, dither-like texture, noisy anti-alias patterns and decoration that overwhelms the body. Palette count alone does not prove this. Review the reduced unquantized and palette-mapped images side by side; reject transformations that merge important surfaces. Select explicit whole palette ramps by material, never `palette[:maxColors]` across unrelated ramps.

S=front; N=true back (back of head/armor, no front face); W=left; E=right. Shared apparent height, head size, torso width, gear proportions and foot anchor must survive all four views. Identity and direction are separate review fields. Correct facing labels are not direction evidence.

Terrain must distinguish ground, path, ruin, graveyard, shrine floor and portal approach by broad material organization and edges, not random dark noise. Produce related tiles together; review repeated patches, connected paths, inside/outside transitions and adjacent families at native 32px. Do not certify a mask merely from its filename.

## Production sequence

1. Freeze source brief/contract and actual references. Generate artwork using ImageGen; keep raw output/prompt/hash. Large source output is permitted, but design it as a deliberately enlarged final-pixel sprite, not a detailed illustration to crush later.
2. Review crops, mask and source-grid density. Normalize with `scripts/normalize_native.py` and an explicit recipe. It rejects nonuniform scaling, clipping and missing alpha/mask. A source unable to survive reduction needs targeted regeneration; no broad PNG patching.
3. Run PNG technical QA. Produce `scripts/native_preview.py` outputs: exact 1x and 2x sheets, S/N/W/E rows, grayscale, silhouette, dark/busy-ground, prop/Player comparison, repeated terrain and mixed-family patch. 2x uses nearest only. View at 100% image size; browser zoom/device scaling can otherwise mislead.
4. Review native and final-gameplay size before packing/integration. Record `qa.readability` with version, asset hash, evidence paths, reviewer, and separate nativeSize, semanticSilhouette, largeForms, featureThickness, pixelClusters, bodySeparation, directionSemantics, crossDirection, terrainIdentity and motion decisions. Values are pass/fail/not_applicable/pending; N/A requires a reason. Agent diagnosis is allowed when requested, but user approval and gameplay acceptance remain distinct. Any required failure means needs_rework; missing evidence means pending.
5. Only promote integration_ready after technical QA, current-hash evidence, all applicable readability gates, frame isolation/motion for animation and a real user decision. Historical runtime trials remain historical authorization; they cannot bypass this gate for new outputs. Controlled P3.0 samples are review candidates, never release-ready by technical pass.
6. Compare equivalent-size raw PNG vs real Arena at 640x360. User reviews ~1-second identity and manual gameplay checklist. Record separate PLAYER_READABILITY_DEBT / PLAYER_DIRECTION_ASSET_DEBT and PHASE_4_ASSET_SKILL_BLOCKER. P3.1+ waits for user acceptance.

Do not auto-round existing world object scales, change camera zoom, brighten/saturate globally, add outlines/glow/shadows or alter simulation/timing. Integer raster assets at intended heights are preferred. Half-step sampling is only allowed with explicit grid-alignment evidence; nearest alone does not make it safe. A fractional target may require an explicitly documented subpixel physical-vs-raster tolerance; do not silently redefine ratios.
