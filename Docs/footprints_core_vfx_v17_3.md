# v17.3 — footprints, core enclosure, white-square VFX fix

The idle spawn portal is **unchanged**. The v17.2 look was approved, so this release ships no portal shader, texture or builder changes.

## 1. Footprints: how the height is resolved

`EnemyGroundFx` emits one print per `stepDistance` (0.45 m), alternating left and right at ±radius·0.32. The height of each print comes from `SurfaceY(at, feet)`, tried in this order:

1. **`StoneSignal.WalkSurface.TryGet(p)`.** Built once per board, with no runtime raycasts.
   - **Board cells.** `GridView.SetGround` records `tile.y + ArtCatalog.tileTop` for every cell. That includes the undulation offset (`BoardArt.HeightOffset`, ±0.03–0.06 on path cells) plus `WalkSurface.TileRoughness` (0.015), which allows for the rough tile top (±0.012). A plain path tile is therefore 0.80 + offset + 0.015.
   - **Bridges and islands.** `GridView.ProbeIslands` samples one height profile per spawn while the temporary dressing colliders still exist.
     - The profile runs from the board edge outward over the bridge onto the island, every 0.1 m, using 5 rays across the walk width (±0.36 m).
     - Rail hits are ignored: any hit above tile top + 0.25 is re-cast from just below that height.
     - Hits at or below 0.15 (water) are ignored.
     - Each sample takes the maximum over ±1 neighbour so the 3 cm plank gaps don't create holes.
     - The bridge plank deck reads 0.80. Island tops read about 0.55–0.65.
   - **Proximity rule.** A profile is used only within ±0.75 m of its centre line, and only if the stored height is within 0.35 m of the enemy's feet.
2. Otherwise, `Physics.Raycast(groundMask)` straight down from the feet (optional, same as v17.2).
3. Otherwise, the enemy's visual root height (the v17.2 behaviour).

Then `footprintLift` (0.02) is added.

**Rendering.** `M_VFX_Footprint` now uses the new shader `StoneSignal/SS_GroundPrint`:
- queue 2995: after opaque, before the path flow chevrons at 3000;
- ZTest LEqual, ZWrite Off, `Offset -1,-2`;
- colour #3A2414 from `_BaseColor`, particle alpha 0.7;
- size 0.40–0.64 m (was 0.34–0.56).

**Why v17.2 prints were weak:** they used one height for the whole enemy (feet + 0.045 ≈ 0.85–0.93), so they sat inside raised tiles and under the 0.89 chevrons. They were also a light, low-alpha brown.

**Known limit:** on the dark Trunk planks (#6D3646) and across the plank gaps, a print reads weaker than on Wood planks or sand.

**Tunables:**
- `EnemyGroundFx.footprintLift`
- `EnemyGroundFxSystem.footprintSize`
- `M_VFX_Footprint._BaseColor` (set by StylizedFxV14)
- `WalkSurface.TileRoughness` / `ProfileHalfWidth`

## 2. Core enclosure

**Source:** `ArtSource/Stylized/Core/core_enclosure_v17_3.py`. It exports `SM_Core_Enclosure_Intact` / `_Cracked` / `_Broken` (same file names, so the .meta GUIDs are kept).

**Each state is one merged mesh:**
- One object, one material slot, so 1 draw call.
- Palette-cell UVs on `T_Env_Palette_D`.
- Vertex colour: R = glow mask, G = AO, B = 1, A = 1.

**Size and placement:**
- Fits the 2×2 core footprint (|x|, |z| ≤ 1).
- The pivot is the tile top. `GridView` places the enclosure `ArtCatalog.tileTop` above the core pivot.
- The camera-side wall is the lowest, so the crystal stays readable.

**States:**

| State | Mesh | Tris | Glow (vertex R) | Effects |
|---|---|---|---|---|
| Intact (HP 1.0–0.70) | Intact | 1192 | rune grooves + obelisk slits 0.45 | — |
| Cracked (0.70–0.40) | Cracked | 2848 | runes 0.7, cracks 0.85, ground fissures 0.6 | — |
| Broken (0.40–0.15) | Broken | 3256 | 1.0 | blocks knocked out or leaning, one obelisk snapped, rubble; smoke |
| Critical (< 0.15) | Broken | 3256 | 1.0 | red core flicker (existing CoreDamageFx MPB), smoke, sparks |

**Glow:** `StoneSignalToonCore.hlsl` gains `_VColorEmission`, which adds `baseCol * vertex.r * _VColorEmission` with a slow breathe. It defaults to 0 and is declared in the Properties of both ToonLit and ToonLitOutline, so it stays SRP-batcher safe. Only `M_Core_Enclosure` sets it (2.2, with `_WindStrength` 0).

**Builder:** `StylizedCoreV173.Build()`, run from BatchImport, creates:
- `M_Core_Enclosure`: a copy of the M_Env_Palette settings plus the glow;
- `PF_VFX_CoreSmoke`: pooled, max 8, 2×2 painted puffs `T_FX_CoreSmoke_2x2`;
- `PF_VFX_CoreSparks`: pooled, max 8, additive `T_FX_CoreSpark`, stretched;
- `PF_Core_Enclosure`: MeshFilter/MeshRenderer, plus `CoreDamageFx` with meshes, smoke, sparks and thresholds 0.70 / 0.40 / 0.15, plus the nested smoke and spark prefabs.

It also sets `ArtCatalog.coreEnclosureFx` and the legacy `coreEnclosure*` fields. `StylizedCoreV173.Checks()` verifies:
- one submesh with vertex colour and UV per state, and the three states are distinct meshes;
- one ToonLit material;
- 16 particles or fewer in total;
- the thresholds.

**Runtime:** GridView instantiates `ArtCatalog.coreEnclosureFx` under the core and fills `CoreDamageFx.coreRenderers` with the core prop renderers. The old per-mesh path remains as a fallback.

**Preview:** `/workspace/previews/core_states_v17_3.png`, with the real `SM_Prop_Core_01`. Smoke and sparks are drawn as stand-ins in that render.

## 3. "White squares": root causes (checked in the v17.2 project files)

1. **The white blocks around the spawn ring (t = 0.35) are the 8 rubble chunks, not the Dust particles.**
   - `SM_Portal_Rubble.fbx` (portal_scene_v16_2.py) was a primitive ico sphere with Blender's automatic UVs.
   - All 20 faces sampled the unused palette cell (240,240,240) through the shared toon stone material, so the chunks rendered white.
   - **Fix:** re-exported with palette UVs (`ArtSource/Stylized/Portal/portal_rubble_v17_3.py`). Same file, so the GUID is kept.
2. **`M_FX_Snow`** (URP Particles/Unlit) had no `_BaseMap` and an opaque surface.
   - It is used by `FX_Weather_Snow` inside `PF_Env_LevelDressing_16x12`: the small hard white squares over the water and board in every shot.
   - It is also used by `FX_DropDust`, the block placement dust burst.
3. **`M_VFX_PortalEmber`** (all `PF_VFX_StatusFx` particles) had no `_BaseMap`.

**Fix for 2 and 3:** `StylizedVfxTexturesV173.Fix()` gives both materials `T_FX_SoftDot` plus a transparent setup. Materials are referenced by GUID, so every prefab using them is fixed.

**Checked and fine:** Dust (`T_Portal_DustPuff_2x2`, 2×2 sheet, transparent, queue 3000), Flare, Flash, the death puff and the footprint all have their `_BaseMap`.

**New loud check:** `StylizedVfxTexturesV173.Checks()` makes BatchImport fail when any of these is true:
- a particle renderer in Prefabs/Stylized or VFX/Stylized has no material;
- a URP Particles or SS_GroundPrint material has a null `_BaseMap` or an opaque surface;
- a `_2x2` / `_4x4` texture is used without the matching texture sheet animation.

## Run

**BatchImport alone is enough** for all of this, including footprints, core, VFX textures and rubble. Neither BatchWire nor a scene edit is needed. `StylizedCoreV173` writes the ArtCatalog fields itself, and BatchWire would set the same values.
