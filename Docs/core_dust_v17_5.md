# v17.5 — core look, crystal, core damage flash, landing dust, plank footprints

Art round for 程序开发, built on art 4292184 / code ec952aa.
Preview: `core_v17_5.png` (mockup v17.3 | v17.4 | v17.5, 4 states + hit flash) and `dust_v17_5.png`.

## 1. Why the core read as cream-white and bloomed (root cause)

- The enclosure FBXs were fine. Parsing all three showed stone R = 0 and only the rune/crack accents with R > 0.
- The cream-white, blooming body was the **core prop** `SM_Prop_Core_01` (plinth, pillars, crystal) inside the enclosure:
  - it used `M_Tower_Cannon`;
  - under Game.unity's warm sun (1.35 × (1, .96, .88)) plus flat ambient (.56, .62, .66), the MechWhite top lands at #FFF2E8 and the crystal (SlotIce) at #96FFFF;
  - both are at or above the bloom threshold of 1.1;
  - the v17.4 `_HiAmount` ring added more on top.
- The enclosure accents added emission on top of an already lit colour (`c += baseCol × 2.2`), so lit orange caps reached about 1.5 and bloomed too.

## 2. What changed

| Item | v17.4 | v17.5 |
|---|---|---|
| Core prop material | `M_Tower_Cannon` | `M_Core_Prop` (ToonLitOutline, copy of M_Tower_Cannon, `_BaseColor` #D9DFF0, rim .2, `_VColorEmission`/`_HiAmount`/`_WindStrength` 0, instancing on) |
| #D9DFF0 | – | 1 / (light + ambient × .35) = the warm-light compensation, so lit palette colours read as the palette (the mockup look) |
| Enclosure `_BaseColor` | white | #D9DFF0 (same compensation), `_RimIntensity` .2 |
| `_VColorEmission` | 2.2, added | 1.45, **blended**: `c = lerp(c, baseCol × 1.45 × breathe, R)`. An accent peaks at about 1.0 (no bloom). Only R ≥ .95 (the Broken crack/fissure glow) gets ×1.35, a slight bloom. Stone R = 0 stays untouched. |
| Obelisk ember caps (vertex R) | 1.0 | .35 / .45 / .55 (Intact / Cracked / Broken). Geometry, UVs and normals are identical; only those 64 loops changed. |
| Crystal | SlotIce cell 35 (#96FFFF in game) | New palette cell 48 `CoreCrystal` #61A1D5 (sampled from the mockup), applied by a UV remap in `StylizedModelPostprocessor` for `Towers/SM_Prop_Core_01.fbx` only. Frost towers keep SlotIce. Lit #62A1D5, mid band ≈ #5087BA, shadow ≈ #2C4884; well away from frost #5FD0FF. |
| Hit flash | white `_HiAmount` ring on top faces | whole-object `_StatusTint`/`_StatusRim`, #FF5A5A, a .65 → 0 over 0.12 s (linear) |
| Critical | `_HiAmount` flicker | 1.5 Hz pulse, #FF8087, a .20 ↔ .55, tint ×2.3 (the mockup's lighter pink body: #A696B5 → #F7A4BD), rim ×1.2 |

## 3. CoreDamageFx (Scripts/VFX/CoreDamageFx.cs)

- Writes to `coreRenderers` (GridView already fills these with the `SM_Prop_Core_01` renderers; plinth, pillars and crystal are one renderer) **and** the enclosure renderer at ×0.6.
- Values:
  - `_StatusTint` = (colour.linear × tintBoost, a);
  - `_StatusRim` = (colour.linear × rimBoost, a).
- These are the existing ToonCore per-instance props (SSWallProps instancing buffer, the same path as EnemyStatusFx): no keyword, no new material.
- When idle it calls `SetPropertyBlock(null)`. Draw calls are unchanged (1 renderer = 1 DC with or without the MPB).
- Fields: `hitColor`, `criticalColor`, `tintBoost` 2.3, `hitDuration` .12, `hitTint` .65, `criticalHz` 1.5, `criticalTint` (.20, .55), `rimBoost` 1.2, `enclosureFlash` .6.
- The builder creates `PF_Core_Enclosure` with AddComponent, so the new defaults are saved. `StylizedCoreV173.Checks` fails if the prefab still carries the v17.4 colours.

## 4. Landing dust (StylizedPlacementFX + PlacementGhost)

- Colour #D9B48A, a 1.
- Life .5–.6 s; start size .5–.8 m; size over life 1 → 1.38 (at 40 %) → 1.6.
- Alpha: 1 → .92 (at 50 %) → 0.
- Motion: gravity −.10, drag 2.6. Spawned at the block base along the outer edges, pushed out .02–.10 cells, outward speed .45–.8, sideways ±.12, upward .25–.45 m/s. Lift .06.
- New texture `Art/Stylized/FX/Placement/T_FX_LandingDust_2x2.png`:
  - the portal puff sheet with denser alpha (alpha^0.6 × 1.2, 128 px, 16 KB);
  - the portal keeps `T_Portal_DustPuff_2x2`;
  - the builder falls back to the portal sheet if the new texture is missing.
- Max 12 particles, pooled (unchanged).

## 5. Plank footprints

- `WalkSurface` records the surface kind (Tile / Ground / Plank) of each raycast profile sample. Plank = palette cell 6 (Trunk) or 7 (Wood), taken from `hit.textureCoord` of the top hit.
- `WalkSurface.TryGet(p, out y, out kind)` is a new overload. Board cells are always Tile.
- `EnemyGroundFx` → `EnemyGroundFxSystem.Footprint(pos, r, yaw, plank)`. Plank prints are flagged with particle colour R = 0.
- `SS_GroundPrint` blends to `_PlankColor` (#C9A57A a .55, set by StylizedFxV14) when R < .5. Sand and tile prints are unchanged (#24160C a .62).
- GridView needs no change. The kinds are captured during `SampleProfile` → `AddProfile`.
- Ground log suggestion: `WalkSurface.PlankSamples`.

## 6. Hookup / BatchImport (程序开发)

BatchImport alone is enough:
1. Import force-reimports `Assets/Game/Art/Stylized`, which picks up:
   - the new enclosure FBXs;
   - the palette;
   - the dust texture;
   - the crystal remap on SM_Prop_Core_01 (the count appears in the `CORE v17.3 PASS … crystal remap N` line; -1 = not reimported this session).
2. `StylizedPlacementFX.BuildAll` (dust material + ghosts).
3. `StylizedFxV14.BuildAll` (`_PlankColor` on M_VFX_Footprint).
4. `StylizedCoreV173.Build`: M_Core_Enclosure values, **M_Core_Prop**, assigns it to every renderer of `PF_Prop_Core`, rebuilds PF_Core_Enclosure.
5. Checks. These now fail if:
   - `_VColorEmission` > 2;
   - a PF_Prop_Core renderer is not on M_Core_Prop;
   - the crystal remap count is 0;
   - CoreDamageFx still has the v17.4 colours.

Note: rebuilding PF_Prop_Core alone (StylizedArtIntegration) puts it back on M_Tower_Cannon until StylizedCoreV173.Build runs. BatchImport's normal order handles this.

### GameplayShot snippet (dev-owned, not edited)

`_HiAmount` on the core now always reads 0. Log the status props instead:

```csharp
var mpb = new MaterialPropertyBlock(); r.GetPropertyBlock(mpb);
log.AppendLine($"core {r.name} tint {mpb.GetVector("_StatusTint")} rim {mpb.GetVector("_StatusRim")}");
log.AppendLine($"walk plank samples {StoneSignal.WalkSurface.PlankSamples}");
```

### Unrelated observation

In `v174/vfx/core_check.txt` the sparks report `isPlaying False` at Broken (expected: off) and at Critical after the stage reset. Worth a look in the capture order; not changed here.
