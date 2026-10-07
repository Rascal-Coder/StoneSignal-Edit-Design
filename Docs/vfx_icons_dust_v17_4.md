# v17.4 — VFX texture check, footprint value, reward-card icons, landing dust

Art round v17.4 on top of fc8221f (v18 Chinese UI). Files only; 程序开发 runs BatchImport + BatchWire.

## 1. VFX TEXTURES FAIL (18) — check fix
* Cause: `StylizedVFXBuilder.Root()` container roots (FX_Explosion_*, FX_Hit_*, FX_Muzzle_*, FX_Enemy_*) have emission OFF, renderer
  OFF and no material by design (verified in FX_Hit_Core.prefab: root `m_Enabled: 0`, `m_Materials: {fileID: 0}`). The v17.3
  `StylizedVfxTexturesV173.Checks()` counted every renderer, so it threw and the following checks
  (StylizedVFXBuilder.Checks, ThirdPartyArtIntegration.Checks, Stage2Validation.Run) never ran.
* Check (`StylizedVfxTexturesV173.Checks`): only renderers that can draw are judged.
  * skipped: disabled renderers; container systems (have child systems, nothing in their own emission module, not a sub-emitter,
    not referenced by a script field);
  * still FAIL: any enabled renderer whose system emits (rate/bursts, sub-emitter, or referenced by a MonoBehaviour for `Emit()`,
    e.g. EnemyGroundFxSystem.dust / PlacementGhost.dust) with a null material, and any enabled leaf system without a material;
  * `StoneSignal/FXAdditive` + `StoneSignal/FXAlpha` explicitly whitelisted as procedural.
* Builder (`StylizedVFXBuilder.Save`): re-asserts the rule — a root system that emits nothing gets its renderer disabled if it has
  no material (Root() already did this; now enforced for every saved FX prefab).
* Shared rules: `Assets/Game/Scripts/VFX/VfxMaterialRules.cs` (runtime, so GameplayShot can use it).
* The 8 "no texture" materials (M_FX_Add_Dot/Ring/Streak/Flash/Trail, M_FX_Alpha_Dot/Smoke/Scorch/Coin): shaders are procedural
  (`SS_FXCore.hlsl` → `FXShape(uv)` by `_Shape`, `_Softness`, `_RingWidth`; no texture property; colour = vertex × `_TintColor` ×
  `_Intensity`). Decision: **whitelist, do not assign T_FX_SoftDot** (a texture would be ignored by the shader).
* GameplayShot (dev file, not touched) — suggested change in the `-vfxcheck` material loop:
  ```csharp
  bool procedural = StoneSignal.VFX.VfxMaterialRules.IsProcedural(m);
  bool bad = (tex == null && !procedural) || surf == "OPAQUE";
  info += (bad ? "  PARTICLE MATERIAL PROBLEM " : "  particle material ") + m.name + " shader=" + m.shader.name
        + " tex=" + (tex ? tex.name : procedural ? "procedural" : "NONE") + ...
  ```

## 2. Footprint (single source of truth = builder)
`StylizedFxV14.FootprintColor = #24160C, alpha 0.62` → `M_VFX_Footprint._BaseColor`; particle start colour is white (alpha 1).
SS_GroundPrint alpha = tex.a × vertex.a × _BaseColor.a, so the material shows exactly what the game draws. Edit the constant,
not the .mat (BatchImport rewrites it). Reasoning from `footprint_tuned/`: dev #1E120A (×0.7 particle alpha) reads as near-black
stamps on the light island sand; v17.3 #3A2414 ×0.7 was too weak on the dark +x planks. 0.62 = the suggested a 0.85 on top of the
old particle alpha, folded into one number (luminance step ≈ −30 on dark planks vs −22 before, ≈ −85 on sand vs −99 dev tune).

## 3. Reward-card icons
15 × 256 px `Assets/Game/Art/Stylized/UI/ui_reward_<id>.png` (HUD.spriteatlas folder), painter
`ArtSource/Stylized/UI/reward_icons_v17_4.py`, mapping `Scripts/UI/RewardIconMap.cs` (+ `reward_icons_v17_4.json`).

| RewardEffect | 标题 | sprite | motif |
|---|---|---|---|
| AllDamage | 全塔伤害 | ui_reward_all_damage | sword + burst |
| AllAttackSpeed | 全塔攻速 | ui_reward_all_attack_speed | bolt + two arrows |
| AllRange | 全塔射程 | ui_reward_all_range | needle tower + gold ring, arrows out |
| ArrowRange | 针弩射程 | ui_reward_arrow_range | needle tower + orange ring + needle bolt |
| CannonRadius | 震岩半径 | ui_reward_cannon_radius | seismic shell blast + red ring |
| AddBlock | 石牌补给 | ui_reward_add_block | O tetromino card + plus |
| NextDraw | 下次抽牌 +N | ui_reward_next_draw | deck + up arrow + plus |
| PathSlow | 路径减速 | ui_reward_path_slow | snail with ice shell (cyan only here) |
| BonusSlot | 扶壁 | ui_reward_bonus_slot | L piece + gold extra slot |
| KillGold | 击杀金币 | ui_reward_kill_gold | coin stack + skull |
| WaveGold | 每波金币 | ui_reward_wave_gold | coin stack + red wave pennant |
| NextWaveGold | 下波金币 | ui_reward_next_wave_gold | coin stack + coin + » |
| TowerDiscount | 塔价折扣 | ui_reward_tower_discount | coin + % price tag |
| BaseHP | 核心加固 | ui_reward_base_hp | shield over core crystal + green repair plus |
| WaveHeal | 每波回血 | ui_reward_wave_heal | core crystal + green cross + loop arrow |
| runes 0..5 | 锋·赤 … 共鸣·白 | ui_rune_blade/swift/sight/frost/bounty/resonance (existing, `RuneArt.Icon`) | |

Hookup (GameUI is dev-owned — 3 lines): at the top of `GameUI.RewardIcon(RewardData r)` add
```csharp
var art17 = S(StoneSignal.UI.RewardIconMap.For(r.effect)); if (art17 != null) return art17;   // v17.4 reward icons
```
(the existing switch stays as fallback until BatchWire has registered the sprites). Rune options already use `RuneArt.Icon`.
`RewardPickUI` (art) now hides its soft IconShadow for these icons (`RewardIconMap.HasBakedShadow`) — they bake a hard shadow.
Sprites resolve via `ArtCatalog.UiSprite`, filled by **BatchWire** (`StylizedGameplayWiring.Wire`: imports PNGs as sprites, packs
HUD.spriteatlas, refreshes `ArtCatalog.uiSprites`). Preview: `previews/reward_icons_v17_4.png`.

## 4. Landing dust
* Why it was invisible: `BlockPlacementManager` calls `PlacementGhost.PlayDrop` once per cell in the same frame; `Drop()` moved the
  single dust system to that cell and called `Play()` — a no-op while already playing, so ONE 14-particle burst came out at the
  first cell, in ghost-local space (follows the pointer when the ghost moves), 0.12–0.26 m particles of `M_FX_Snow` (white, shared
  with snow) starting under the block, partly hidden in the tile.
* Now: `PlacementGhost` collects the cells, and ~0.1 s later (touch-down of the 0.35 m drop) emits one ring along the outer edges of
  the whole piece (inner edges skipped), outward + slightly up, `EmitParams`, max 12, 8 minimum (1 per outer edge, ≤12).
* `StylizedPlacementFX` FX_DropDust: world space, loop, emission module off, life 0.42–0.55 s, size 0.34–0.5 m growing 0.55→1.4,
  drag 3.2, alpha 0.95 → 0.85 @55 % → 0, colour #E9C9A0 (spec FX_Build_Dust), 2×2 random puff frame,
  new material `M_VFX_LandingDust` (URP Particles/Unlit, alpha, `T_Portal_DustPuff_2x2`, shared by both ghost prefabs).
  Pooled: one system per ghost, no Instantiate, no material instances. 1 DC while puffs are alive.
* DC 69 → 159 → 69: not the dust. `GameplayShot.Capture()` renders the camera twice into an RT (`cam.Render()` ×2). PNG encode +
  write takes longer than the 0.1 s gap, so the t=0.16 sample reads `draws.LastValue` of the frame that contained the t=0.06
  capture: 69 + 2 × 45 (scene without UI) = 159. No per-placement Instantiate of FX, no `renderer.material` in Scripts, walls use
  shared materials until `MergeWalls` (0.9 s later). Fix for the tool: `yield return null; yield return null;` before reading
  `draws.LastValue`.
