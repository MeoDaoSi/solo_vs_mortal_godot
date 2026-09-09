# Verdant 00 — Free Sample [16x16]

**64 tiles. Free, commercial use permitted, no attribution required, no strings.**

A grass-and-dirt starter set with a **complete** 47-mask autotile transition, engine imports for
RPG Maker MZ, Tiled and Godot 4, and the same machine-checked proofs the paid packs ship with.

---

## What is in here

| | |
| --- | --- |
| **Grass fills** | 4 variants |
| **Dirt fills** | 4 variants |
| **Dirt-on-grass transition** | **47 masks — the complete blob set** |
| **Props** | 5 — tree (2×2), bush, rock, stump, red flowers |
| **Grounded props** | 4 — the tree, bush, rock and stump again, each on its own grass with a contact shadow |
| **Total** | **64 tiles** |

### Two ways to place a prop

Four of the five props ship twice, and the pair is the point:

- The **transparent** tile is an overlay. Drop it on any terrain you like — grass, sand, your own
  ground — and it composites over whatever is underneath.
- The **grounded** tile (`*_grounded`) is a drop-in that brings its own grass and a **contact
  shadow** with it, so the object reads as planted in the ground instead of floating on top of it.
  The shadow's colour is derived from that grass's own darkest tone rather than being a generic
  grey, which is why it sits in the picture instead of on it.

Use the grounded tile when the prop sits on plain grass and you want it to look settled; use the
transparent one anywhere else.

**The red flowers ship as an overlay only, deliberately.** A contact shadow belongs under a *mass*.
A clump of thin stems has no single contact patch, so a pooled shadow reads as a dark stain with the
blooms floating in it — six grounded flower variants were built for Verdant 01, rendered at 8× and
cut for exactly that reason. The flowers already ground themselves: each bloom sits on its own
darker stem pixel.

### Why the autotile set is complete

Because a 47-mask blob set missing three tiles is **broken in your map**, and you would find the
hole before we did. A crippled autotile set is not a smaller product, it is a defective one. So the
sample gives away one material transition *entirely*.

What the paid packs add is **breadth**, not the missing pieces:

- **[Verdant 01 — Overworld](https://csaf.itch.io/verdant-01-overworld)** — 239 tiles. Sand, stone
  and water each get their own complete 47-mask set (three more transitions), 19 more fills
  including flowered grass, and 17 props: three trees, three bushes, three rocks, stumps, logs and
  flower clumps in three colours — plus eleven of those props again as drop-in tiles that bring
  their own ground and a contact shadow with them.
- **[Verdant 02 — Elevation](https://csaf.itch.io/verdant-02-elevation)** — 149 tiles. Top-down
  elevation: a 47-mask directional plateau step, a four-band cliff stack with cast shadows, a
  second blob set for rocky shelves, and rock props.
- **[Verdant 03 — Worlds: Snow, Desert & Volcanic](https://csaf.itch.io/verdant-03-worlds)** —
  330 tiles across three more biomes, and the pack that adds **animated tiles**: five looping
  sequences of four frames each — water surface, lava flow, melting ice, a torch and a banner.
- **[Verdant Forge — Tileset Generator](https://csaf.itch.io/verdant-forge-tileset-generator)** —
  not a pack but the engine that makes them. One HTML file, opened in a browser, offline: it emits
  a complete palette-locked, autotile-complete tileset — sheet, individual tiles, tile→role map and
  engine importers — for outdoor terrain *and* interiors, on this same grid.

---

## These are literally the paid pack's tiles

Every tile in this sample is **byte-for-byte identical** to the same tile in Verdant 01. That is
checked mechanically before this pack is built — each generated tile's SHA-256 is compared against
the file Verdant 01 actually ships, and **the build refuses to run if any tile differs**.

Two consequences worth knowing:

1. **What you see here is exactly what you get if you buy.** The sample is not a downgraded render,
   a watermarked version, or a different pass of the generator.
2. **If you buy Verdant 01 later, these files are already part of it.** Same filenames, same bytes.
   Nothing to reconcile, nothing to replace, no visual shift halfway through your project.

---

## Files

```
pack.json                 cell size, palette, tile count
tiles.json                every tile: role, tiling mode, sheet slot, autotile mask
tiles/16x16/*.png         one PNG per tile — 64 files
sheets/16x16/*.png        grouped atlases: terrain, autotile_dirt, props
proof/*.png               the 4×4 repetition fields, shipped so you can check us
importers/RPGMakerMZ/     tileset image at the engine's exact dimensions + slot map
importers/Tiled/          .tsx files with their sheets beside them
importers/Godot/          a Godot 4 TileSet .tres
sample_maps/              a finished 24×14 map, as a PNG *and* as editable tile-id data
```

## The sample map — a finished map, as data

`sample_maps/sample_map_01.png` is a **24×14 map built from this pack and nothing else**: a dirt
path winding into a rounded clearing, with a grass island inside it. It uses **30 of the 64 tiles**
and places **14 props**.

The island is not decoration — it is the only shape that exercises **all four inner-corner masks at
once**, so the map doubles as a visual check that the 47-mask set really is complete.

Beside it, `sample_map_01.json` is the same map **as tile ids**:

```
ground   [row][col] — a tile id per cell, top-left origin
props    [{id, x, y}] IN DRAW ORDER
legend   only the tiles this map uses, with role, size and file
```

Paint `ground` row by row, then place `props` in order, and you have this map — in any engine.

⚠️ **Verified, not asserted.** The JSON is generated by rebuilding the picture *from the JSON alone*
and requiring **byte-identical** output. A missing or mislabelled cell means the file is not written.

### Importing

- **RPG Maker MZ** — **`verdant-00-sample_A2.png` is a real A2 autotile sheet.** Assign it as your
  tileset's **A2** slot and MZ's own autotile brush paints dirt-on-grass, complete 47-mask, no
  arrangement left unhandled. Every one of the 48 shapes MZ can request was composed out of this
  sheet using the engine's own autotile table and checked **byte for byte** against the individual
  tiles in this pack — **48/48 exact**. *(That proves the sheet composes correctly against MZ's own
  table; we do not have MZ installed, so we are not claiming we painted with it in the editor.)*
  Also drop `verdant-00-sample_B.png` in for the props and non-autotile terrain, and assign it as a
  B tileset. `verdant-00-sample.slots.json` maps every tile to its slot.
- **Tiled** — open any `.tsx` in `importers/Tiled/`. The sheet sits beside it, so no relinking.
  Or open **`sample_maps/sample_map_01.tmx`**, the finished 24×14 map with every tileset already
  wired up — verified by rendering it in Tiled and comparing the result to `sample_map_01.png`
  pixel-for-pixel.
- **Godot 4** — `importers/Godot/verdant-00-sample.tres` with its three atlas PNGs.
  ✅ **Loaded and verified in Godot 4.7.2.** We opened this exact file and had Godot report back what it
  built: 3 atlas sources, all 64 tiles present, every texture resolved, every footprint correct.
  What that means precisely: the resource loads, every atlas source builds, every texture resolves, every declared tile is present and every multi-cell tile keeps its true footprint. What we did **not** test: the editor GUI, painting on a `TileMapLayer`, or a running game.

---

## What is proven, and what is not

**Machine-checked before release. You can re-run all of it against the files in this zip.**

- Every cell exactly 16×16; every sheet an exact whole multiple of the cell. **74 files checked.**
- Every opaque pixel inside the declared 25-colour palette. **217,100 pixels checked.**
- Alpha strictly binary — no anti-aliasing, no halos, no soft upscale. **678,400 pixels checked.**
- **47 of 47** autotile mask slots filled. No missing inner corner waiting to break your map.
- Every tile carries a declared role and lands inside its sheet slot. **128 records checked.**

**Measured, and deliberately NOT called proof: seams.** Every tile's wrap boundary is measured
against its own interior, and every 4×4 repetition is rendered and reviewed by eye — the renders are
in `proof/` so you can look too. We will not write *"mechanically proven seamless"*, because our own measurement says the automated check catches the commonest real defect only rarely, and the
rendered fields are the control that actually works.

---

## Licence

**Commercial use permitted, including in paid games. No attribution required.** You may ship a
released commercial game built on nothing but this free pack. You may not resell or redistribute the
tiles *as* an asset pack. Full terms in `LICENSE.txt`.

## AI disclosure

**Graphics: yes — generated programmatically.** Every tile is produced by a procedural pixel engine
written for this series: it places each pixel on an integer lattice and **refuses any colour outside
the declared palette at write time**. There is no diffusion model, no image generator and no
upscaler anywhere in the pipeline — which is exactly why the grid, palette and anti-aliasing claims
above are measurements rather than hopes.

Sounds: none. Text and dialog: none.

---

*Core Systems Asset Factory*
