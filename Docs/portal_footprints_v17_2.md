# v17.2: portal idle/flare, dust, flyer lift, footprints, post-kill smudge

Previews: `/workspace/previews/portal_v17_2.png` and `/workspace/previews/footprints_v17_2.png`. Both were made by porting the
SS_SpawnPortal fragment shader and the particle settings 1:1 to numpy and compositing the result onto the dev screenshots. No engine was used.

## Spawn portal (calm idle)
| | Before (v16.2) | After (v17.2) |
|---|---|---|
| **Scorch** | Near-black (RGB ~40/30/30, alpha 0.9), radius ~1.0 m | Light warm sun-baked brown (150/92/50 → 112/66/36), alpha ≤ 0.46 × 0.85, radius ~0.75 m. Faint warm cracks. |
| **Ring** | Thin hand-painted orange line; blends into orange sand | 44 px band (at 1024) with notches, ticks and outside studs, so the slow spin (0.15 rad/s) reads. Gold core → ember edge. Dark warm rim underneath for a toon edge. Soft halo. |
| **Pulse** | Brightness breathe | Gentle ~4 s emissive pulse on both ring and halo (`_PulseSpeed` 1.6) |
| **Glyph** | Same intensity as the ring | Faint (26%), counter-rotates −0.05 rad/s, lights up on spawn |

- `_RuneTex` is now a **linear mask texture**: R = ring, G = glyph, B = halo, A = rim. `StylizedPortalV161.Build` imports it with sRGB off.
- Still 1 quad, 1 draw call, instanced. All colours and speeds are material properties, so they can be tuned without repainting.

## Spawn flare (0–0.3 s clearly visible)
- **Envelope:** `SpawnPortal` holds the envelope at 1 for 0.14 s, then fades it to 0 by 0.55 s. Before, it decayed linearly from frame 0, which is why it was almost gone at t = 0.15. `_FlareT` (normalised 0..1) drives a new expanding ring wave.
- **Ground shader:** an ember flash disc (`_FlashColor` 1.7/0.7/0.2 HDR) plus a ring wave running from r 0.12 to 0.95 (`_WaveColor`). Ring and glyph brighten. Cracks glow.
- **New `Flash` billboard:** alpha-blended HDR ember flash (T_Portal_FlashSoft), 0.3 s, grows ×0.55 → ×1.25. Alpha blending keeps it orange on bright sand; additive washed out to white.
- **Flare 4x4:** repainted with a fuller core, slower fade and vignetted cells. Additive with HDR tint 1.4/0.75/0.3, size 2.4 m, local y 0.55.
- **Dust (dev issue 1, white squares):** `M_VFX_PortalDust` had no texture. It now uses `T_Portal_DustPuff_2x2` (painted soft puffs, random cell, random rotation, warm sand tint, grows ×0.55 → ×1.8, fades over 0.8 s).

## Flyer extra shadow / 1.8 m lift (dev issue 3)
- **Cause:** every enemy had **two** EnemyGroundFx.
  - One is baked into `PF_Enemy_*` as child `GroundFx` by `StylizedFxV14.BuildEnemyGround`.
  - One is added at runtime by `EnemyManager` from `ArtCatalog.enemyGround`.
- **What that did to flyers:**
  - Both copies got `SetFlying(true)`. The baked one added a second FlyingMotion to the model child inside the visual, and the runtime one added a FlyingMotion to the visual root. The two lifts stacked.
  - The baked GroundFx sits inside the lifted visual, so its blob rode up with the model. That is the grey disc at ~2.05 m.
  - The rest pivots are fine: `SM_Enemy_Skimmer_01` z 0..0.66 and `SM_Enemy_Flyer_01` z 0.08..1.01, both with the armature root at 0, so the FBXs do not need re-pivoting. The "~1.8 m above pivot" was the inner FlyingMotion lift.
  - Walkers had the same double: two blobs (both at 0.90 in the step-1 log) and duplicate footprints.
- **Fix:**
  - `EnemyGroundFx.ClaimEnemy(root, fx)`, called by EnemyManager when it creates the runtime ground fx, deactivates the baked copies. It also removes any FlyingMotion that is not on the visual root.
  - `FlyingMotion` disables itself if a parent already has one.
  - `EnsureFlyingMotion` never stacks.
  - Expected after this: the model sits at the visual base + 1.2 ± 0.12 m, with one shadow on the ground (flyingBlobScale 0.55). This applies to the Skimmer and the bird (`PF_Enemy_Flyer`) alike.
  - The baked GroundFx stays in the prefabs for editor previews and the V14 check, and is inert at runtime.

## Footprints (dev issue 4)
- **New look:** painted paw print `FX/Ground/T_FX_Footprint.png` with `M_VFX_Footprint` (URP Particles/Unlit, alpha, queue 2995).
  - Colour #3D2110, alpha 0.55.
  - Lifetime 1.5 s: holds for 55%, then fades.
  - Size 0.34–0.56 m, scaled by enemy radius.
  - Prints alternate left/right (±0.32 r) and are aligned to the heading through `EmitParams.rotation`. If they point backwards, set `EnemyGroundFxSystem.footprintYawOffset` = 180; if diagonals turn the wrong way, set `footprintYawSign` = −1.
- **Height:** sampled per step at the walker's feet, which is the visual root's world y. That is the height the model actually stands at, tiles or bridge planks, plus `footprintLift` 0.045.
- **Optional:** if ground colliders are ever added, set `EnemyGroundFx.groundMask` and a downward raycast refines the height. The art has no colliders today.

## Post-kill dark smudge (dev issue 5, game_portal_clean.png ~x790,y215)
- **Ours, but not the death poof.** The coins in the frame mean it is ≥ 0.96 s after the kill (coins drop when the skull fades), and the poof lives only 0.55 s.
- **It is the Seismic/cannon `FX_Explosion_HE` from StylizedVFXBuilder:**
  - Its Smoke lived up to 1.7 s, grew to ~3 m and faded **to** the dark element colour #4A3A48.
  - Its Scorch was a near-black (#241A24) flat decal, 1.9 m, 2.2 s.
- **Fix (`Explosion()`, all elements):**
  - Smoke is light/warm (65% towards #E8DCD0), alpha 0.42, lives 0.55–0.9 s and fades to a light tint.
  - Scorch is a small warm-brown mark (dark scorches lerped 60% to #6A4A34), 0.85 × scale, alpha 0.2, gone after 1.1 s. Ice keeps its light-blue scorch.
- The small grey dots seen around explosions are that FX's Debris (unchanged, ≤ 1 s).
