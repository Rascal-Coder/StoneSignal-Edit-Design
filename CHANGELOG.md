# Changelog

## 2026-10-08 — v17.5 Core look and crystal, core damage flash, landing dust, plank footprints

- **Core cream-white/bloom, root cause:**
  - The enclosure vertex colours were correct (stone R = 0).
  - The cream body was the core prop `SM_Prop_Core_01` on `M_Tower_Cannon` under the warm 1.35 sun: MechWhite #FFF2E8, crystal #96FFFF, both at the bloom threshold.
- **Core prop:**
  - New `M_Core_Prop` (`StylizedCoreV173.BuildCorePropMaterial`): `_BaseColor` #D9DFF0 warm-light compensation, rim .2, no `_HiAmount`/`_VColorEmission`.
  - It is assigned to every `PF_Prop_Core` renderer. Towers are unchanged.
- **Enclosure:**
  - `M_Core_Enclosure` `_BaseColor` #D9DFF0, rim .2.
  - `_VColorEmission` 2.2 → 1.45, and ToonCore now blends toward the ember colour (`lerp`, not `+=`). Accents peak at about 1.0; only R ≥ .95 (the Broken crack glow) gets ×1.35, a slight bloom.
  - Obelisk ember caps re-exported at R .35/.45/.55 (geometry, UVs and normals identical).
- **Crystal:**
  - New palette cell 48 `CoreCrystal` #61A1D5 (sampled from the v17.3 mockup).
  - `StylizedModelPostprocessor` remaps the SlotIce UVs of `SM_Prop_Core_01` only, so frost towers keep #5FD0FF.
- **CoreDamageFx:**
  - Whole-object red through the existing per-instance `_StatusTint`/`_StatusRim` on the core renderers plus the enclosure (×.6). No keyword, no new material, DC unchanged; idle restores SRP batching.
  - PlayHit: #FF5A5A, a .65 → 0 over 0.12 s.
  - Critical: 1.5 Hz pulse #FF8087, a .20–.55, tint ×2.3 (mockup intensity).
- **Landing dust:**
  - #D9B48A a 1; .5–.8 m growing 1.6×; life .5–.6 s.
  - Spawned at the block base along the outer edges, with outward + upward drift.
  - New denser `T_FX_LandingDust_2x2` (the portal keeps its own sheet). Max 12, pooled.
- **Plank footprints:**
  - `WalkSurface` records the surface kind from the palette UV (Trunk/Wood = plank).
  - `EnemyGroundFx` passes it to `EnemyGroundFxSystem.Footprint(..., plank)`.
  - `SS_GroundPrint` uses `_PlankColor` #C9A57A a .55 there. Sand/tile prints are unchanged.
- **Checks:** fail on `_VColorEmission` > 2, PF_Prop_Core not on M_Core_Prop, zero crystal remap, or stale v17.4 CoreDamageFx colours.
- **Documentation:** `Docs/core_dust_v17_5.md`. Preview script: `ArtSource/Stylized/Core/core_preview_v17_5.py`.

## 2026-10-08 — v17.4 VFX texture check fix, footprint value in the builder, reward-card icons, landing dust

- **VFX TEXTURES FAIL (18) fixed:**
  - The 18 roots (FX_Explosion_*, FX_Hit_*, FX_Muzzle_*, FX_Enemy_*) are `StylizedVFXBuilder.Root()` containers: emission off, renderer off, no material.
  - `StylizedVfxTexturesV173.Checks()` now skips disabled renderers and non-emitting container systems.
  - An emitting renderer (rate/bursts, sub-emitter, or script-driven `Emit()`) with no material still fails, and so does any enabled leaf system with no material.
  - `StylizedVFXBuilder.Save()` re-asserts "root emits nothing → renderer off".
  - New shared rules in `Scripts/VFX/VfxMaterialRules.cs`.
  - `StoneSignal/FXAdditive` and `FXAlpha` are procedural (`SS_FXCore.hlsl`, no texture property). They are whitelisted, not given T_FX_SoftDot.
- **Footprints:**
  - `StylizedFxV14.FootprintColor` (#24160C, alpha 0.62) is now the single source for `M_VFX_Footprint._BaseColor`.
  - The particle start colour is white.
- **Reward-card icons:**
  - 15 new `ui_reward_<id>.png` (256 px, chunky toon HUD, baked hard shadow) in `Art/Stylized/UI`.
  - Mapping in `Scripts/UI/RewardIconMap.cs` and `ArtSource/Stylized/UI/reward_icons_v17_4.json`; painter `reward_icons_v17_4.py`.
  - `RewardPickUI` hides the soft IconShadow for icons that bake their own.
  - GameUI hookup is a one-liner, see the doc.
- **Landing dust:**
  - `PlacementGhost` collects the cells placed in a frame and emits one soft tan puff ring along the outer edges of the piece at touch-down (max 12, pooled, world space).
  - New `M_VFX_LandingDust` (T_Portal_DustPuff_2x2, #E9C9A0). It no longer shares `M_FX_Snow`.
  - The 69→159 DC jump is a `GameplayShot` capture artifact (two `cam.Render()` calls in the sampled frame), not the dust.
- **Documentation:** `Docs/vfx_icons_dust_v17_4.md`.
- **Preview:** `reward_icons_v17_4.png`.

## 2026-10-08 — v17.3 Footprints on the real walk surface, merged core enclosure + smoke/sparks, white-square VFX fix

- **Spawn portal:** not changed. The v17.2 idle look is approved.
- **Footprints:**
  - Each print now sits on the actual walk surface, resolved by `StoneSignal.WalkSurface` + `EnemyGroundFx.SurfaceY`:
    - tiles: placed tile top, including undulation, + 1.5 cm;
    - bridges and islands: a per-entry height profile sampled from the dressing meshes in `GridView.ProbeIslands`;
    - then a raycast, then the feet height as fallback;
    - plus a 0.02 lift.
  - New `SS_GroundPrint` shader (queue 2995, ZTest LEqual, Offset -1,-2).
  - #3A2414 at alpha 0.7, 0.40–0.64 m.
- **Core enclosure:**
  - `SM_Core_Enclosure_Intact` / `_Cracked` / `_Broken` re-exported, each as ONE merged palette-UV mesh (1 DC) with a vertex-R crack glow.
  - New `_VColorEmission` in ToonCore, used only by `M_Core_Enclosure`.
  - New `StylizedCoreV173` (run from BatchImport) builds `PF_Core_Enclosure`: CoreDamageFx with thresholds 0.70/0.40/0.15, plus `PF_VFX_CoreSmoke` (8) and `PF_VFX_CoreSparks` (8).
  - It also sets `ArtCatalog.coreEnclosureFx`. GridView instantiates the enclosure at tile top.
  - Source: `ArtSource/Stylized/Core/core_enclosure_v17_3.py`.
- **White squares (root causes):**
  - Spawn rubble chunks: `SM_Portal_Rubble.fbx` had auto-UVs that sampled the unused (240,240,240) palette cells. Re-exported with palette UVs (`portal_rubble_v17_3.py`).
  - `M_FX_Snow` (dressing snow + block drop dust) had no texture and an opaque surface.
  - `M_VFX_PortalEmber` (status FX) had no texture.
  - `StylizedVfxTexturesV173.Fix()` gives both materials `T_FX_SoftDot` and a transparent setup. Its `Checks()` fails BatchImport on any particle material with a null `_BaseMap`.
  - Dust, Flare, Flash, death puff and footprint were already textured.
- **Documentation:** `Docs/footprints_core_vfx_v17_3.md`.
- **Previews:** `footprints_v17_3.png`, `core_states_v17_3.png`, `vfx_white_squares_v17_3.png`.

## 2026-10-08 — v17.2 Spawn portal polish, dust texture, flyer double lift, footprints, explosion smudge

- **Spawn portal idle:**
  - SS_SpawnPortal was rewritten. The scorch is now a light warm brown (no black) and smaller, about 0.75 m.
  - The rune ring is a thick, notched ember band. It rotates slowly (0.15 rad/s), pulses gently over ~4 s with a halo, and has a dark warm rim. The inner glyph is faint and counter-rotates.
  - `_RuneTex` is now a linear mask texture (R ring / G glyph / B halo / A rim). Still 1 draw call.
- **Spawn flare:** the envelope stays full for 0.14 s and fades out by 0.55 s. Added a ground flash and an expanding ring wave (`_FlareT`), a new alpha-blended HDR `Flash` billboard, and a repainted, brighter 4x4 flare.
- **Portal dust:** `M_VFX_PortalDust` now has the painted `T_Portal_DustPuff_2x2` texture, so the white squares are gone.
- **Flyer / double ground fx:**
  - Enemies carried both a baked `GroundFx` and a runtime ground fx. Flyers therefore had two FlyingMotions (lift ~2.4 m) and a second shadow riding the model.
  - `EnemyGroundFx.ClaimEnemy` (called from EnemyManager) keeps only the runtime one, and `FlyingMotion` no longer stacks.
  - The FBX pivots were checked and are at 0 (Skimmer and bird).
- **Footprints:** painted paw prints (`T_FX_Footprint` / `M_VFX_Footprint`), dark warm brown at alpha 0.55, 1.5 s, 0.34–0.56 m. They alternate L/R, align to the heading, and are placed per step at the walker's feet (planks/tiles). An optional raycast mask is available.
- **Post-kill dark smudge:** it came from the `FX_Explosion_*` smoke (1.7 s, ending on a dark colour) and the near-black scorch (2.2 s). The smoke is now light and lasts at most 0.9 s; the scorch is warm brown, alpha 0.2, 1.1 s.
- **Documentation:** `Docs/portal_footprints_v17_2.md`.
- **Previews:** `portal_v17_2.png`, `footprints_v17_2.png`.

## 2026-10-08 — v17.1 出生门清场 + 中文 TMP 字体

- 出生门（PF_VFX_SpawnPortal）下方黄橙斑块的原因：不是 Leaves 道具，而是 `SM_Env_Island_Cliff_4x4_01` 模型自带的 16 个落叶/草丘（最高 +0.2 m）和 40 片落叶碎片，岛顶边缘也被抖动抬高了最多 +0.12 m。新增变体 `SM_Env_Island_Cliff_4x4_Portal_01`：岛的轮廓和随机种子不变，岛顶是平的，半径 1.45 m 内（门贴花半径 1.3 m + 探测余量）没有草丘和碎叶。`level_layout.json` 中 3 个入口岛（item 248/260/272）换成这个变体；东、西两门旁的 4 棵树和营火挪到门后方，任何门 2.6 m 内都没有 `SM_Env_Leaves_01`。`StylizedArtIntegration.Assets` 新增 `PF_Env_Island_Cliff_4x4_Portal`。下次 BatchImport 会重建 LevelDressing。符文石开关（ArtCatalog.portalShowRunestones）和门 prefab 都没改。预览：portal_clear_v17_1.png。
- 中文字体：`Assets/Game/Art/Fonts/StoneSignalRoundedCN-Heavy.ttf`（1,461 KB，4,188 字），是 Resource Han Rounded CN Heavy 0.990 的子集，按 SIL OFL 1.1 授权，已改名以避开保留字体名 “Source”。字符集为 `StoneSignal_CJK_Charset.txt`，许可证文件为 `StoneSignalRoundedCN - OFL.txt`。TMP SDF 的生成、回退链和描边材质设置见 `Docs/font_cjk_tmp_spec.md`。

## 2026-10-07 — 敌人朝向、塔占地、关卡装饰槽、游戏截图钩子

- 敌人朝向：根节点 +Z 以 `Quaternion.RotateTowards` 平滑转向水平移动方向（仅绕 Y），转速 `EnemyData.turnSpeed`（默认 540°/s）；出生/分裂子体从出生点直接朝向首段路径。无任何按模型的 yaw/180° 偏移（美术统一 +Z、根旋转为零）。
- 塔占地：`TowerData.footprint`（默认 1x1，Seismic/Mortar 2x2）+ `footprintRotates`（1x2 类塔按 R 旋转 90°）。放置要求所有覆盖格在界内、均为墙块顶面且无塔；占用/拆除（`TowerManager.Remove`）作用于所有格；幽灵与射程圈吸附占地中心，模型居中于占地中心，射程从中心计算。烟测改为先砌墙再建塔。编辑器测试 `StoneSignal/Tests/Tower footprints`（`TowerFootprintTests.RunBatch`：边界、部分重叠、紧邻核心等 10 例）。
- 关卡装饰槽：`BoardLayoutData.levelDressing`（+ offset −0.55、`levelDressingReplacesCliff`），接线工具在 `PF_Env_LevelDressing_16x12` 存在时自动赋值，否则保留悬崖 + 水面。不再手工拼岛/树；仅从 level_layout.json 取桥板与码头桩（W4/E4/N3 段，Check 校验）。修复 `entryBridge` 赋值被注释吞掉的问题。悬崖偏移核对：演示中悬崖顶 0.55、格顶 0.80 → 游戏 −0.55。
- `StoneSignal.EditorTools.GameScreenshot.Capture`：渲染 `Verification/Screenshots/game.png`，并写 `game.txt`（渲染器数、总/可见三角形、估算 DrawCall、预算 <150 DC / <150k tris）。


## 2026-10-07 — 第三方 CC0 美术全量替换 + 敌人骨骼动画

用户选定**全量替换**（放弃第一轮 Blender 原创环境）并**引入骨骼动画**。素材为两个 CC0 包：Kenney Tower Defense Kit（`kenney.nl` 直链，5.4 MB）与 Quaternius Animated Monster Pack（OpenGameArt 镜像，1.4 MB）。下载直链、授权依据与实际使用的模型清单写入 `ArtSource/REFERENCES.md`，原始压缩包保留在 `ArtSource/ThirdParty/`。

Added:

- `StoneSignal/Import third-party art pack`：从 `ArtSource/ThirdParty/` 全量重建视觉层——清旧导入、复制 19 个 Kenney FBX 与 4 个怪物 FBX、配置导入器、把 FBX 内嵌材质重映射到 URP/Lit、按包内 `.mtl` 解析怪物颜色、拼装四座塔、生成 AnimatorController、离屏渲染图标、写入 ArtCatalog 与数据资产、配置场景光照与色调映射。幂等。
- `Packages/manifest.json` 启用 `com.unity.modules.animation`（此前工程未引用动画模块）。
- 敌人骨骼动画：每只怪物一套 `SS_Move` / `SS_Death` 片段，AnimatorController 默认播循环移动、`Die` 触发器切死亡；`EnemyVisualContract` 固定片段与状态命名，运行时不依赖 FBX 内部的 Blender 动作名。
- 动态道路：`GridView` 按 `PathfindingManager.PathChanged` 在 `tile`（草地）与 `tile-dirt`（土路）之间局部切换格子，路径每次变化只重建状态改变的格子。
- `BoardEnvironment`（取代 `RuinEnvironment`）：棋盘外圈草地 + 确定性树/石/晶/土块装饰 + 草甸底板。
- 塔图标和方块图标改为引擎内离屏渲染（256×256 透明 PNG），方块图标按 `BlockShapeData.cells` 摆四块墙块，缩到 0.86 留缝。
- `ArtCatalog` 改为 Kenney 结构，并持有实测尺寸 `tileTop` / `blockTop`；新增 `EnemyVisualContract`。

Changed:

- 棋盘、地块、墙块、四塔、Spawn/Core 地标、环境装饰、全部敌人模型换成 Kenney / Quaternius。四座塔改为模块化拼装（底座 0–0.21、中段 0.21–0.81、武器 0.81 起），Needle/Pulse/Seismic 靠武器色调区分，Chill 用紫色晶簇无武器。
- 敌人映射：Drifter = Slime、Skimmer = Bat、Bulwark = Skeleton、Splitter = Dragon、Shard = 缩小版 Slime；按目标身高统一缩放并水平居中。
- 视觉风格整体改为明亮日照 + Neutral 色调映射 + 弱 Bloom，背景改为天蓝与草甸绿。
- 敌人存活时按 `CanMove()` 调节 `Animator.speed`；死亡先播死亡片段再走原有 0.22 秒缩小，击杀时序不变；逃脱不播死亡动画。
- 墙上建塔视觉抬高由硬编码 0.62 改为 `blockTop - tileTop` = 0.50；L 奖励的额外平台改用同一墙块模型，不再是圆柱。
- 路径折线抬到路面之上并收窄到 0.07，保留方向箭头；终点与起点文字标签改为深色以适配亮场景。
- 首轮 Blender 美术退出运行时：删除 `Art/Models`、`Prefabs/Art` 旧 Prefab、`SS_*` 材质和 `ArtIntegration.cs`；`ArtSource/` 下的 `.blend` 与生成脚本保留留档。
- DESIGN 明确记录本次对「不引入动画」约束的显式修改及其边界（只做移动循环与死亡一次性片段）。

Fixed:

- Kenney 贴图取错：包内 `Models/Textures/variation-a.png` 是配色样板（去掉了中间的绿色带），不是模型贴图，会让整块棋盘变橙。正确贴图是 `Models/FBX format/Textures/colormap.png`，已在工具中改正并写进 DESIGN 备注。

Validation：引擎编译通过；美术引用/尺度/URP/无碰撞器检查通过；第二阶段逻辑检查通过；Windows 开发构建通过；带新美术的自动试玩 51 项全通过（种子 37，1600×900），新增「敌人 Prefab 携带行走与死亡动画」检查，覆盖三波真实战斗、四塔建造、分裂精确计数、三选一奖励、动态改路与 GameOver。程序截图在 `Verification/Art2/`，已目视确认草地/土路、动态绕路、四塔配色、墙块、方块图标、敌人与血条。人工手感、性能压力和长期平衡未验收。

## 2026-10-06 — 项目文档与协作规则

Added:

- DESIGN.md：核心规则、状态边界、当前原型例外与开发约束。
- BALANCE.md：从当前 ScriptableObject 与代码核对的塔、敌人、波次、奖励和公式。
- 项目及工作入口 AGENTS.md：固定先读 README → DESIGN → BALANCE → CHANGELOG，再检查代码和资产；禁止凭历史对话记忆修改项目。

Changed:

- README 增加开发阶段、文档入口、目录、完成状态和已知限制。
- 明确战斗放墙默认开启是现有原型验证例外，记录第三波之后的实际线性 HP 成长和单波金币奖励规则。
- 本次仅补齐文档与协作规则，未调整玩法代码、场景或数值资产；此前运行验证仍对应 Prototype 0.1。

## 2026-10-06 — Prototype 0.1 / Milestone 1–8

Added:

- 团结引擎 1.10.4 / 2022.3.62t16 的 URP 3D 工程与自动初始化 Game 场景。
- 16×10 网格、四方向 A*、受保护的 Spawn/Goal 与路线显示。
- I/O/L/T/S 数据形状、90° 旋转、红绿预览、无副作用放置验证与最后道路保护。
- 敌人移动、连续动态改路与移动路段保护。
- 三种原创塔、剩余路径优先目标、追踪弹丸、单体/AOE 伤害。
- 三波敌人、事件结算、金币、基地生命、Build/Combat/Reward/GameOver 和重新开始。
- 六种本局升级与三选一奖励；单波金币奖励不跨下一波继续累积。
- UGUI、血条、受击闪白、死亡缩小、命中粒子与浮动伤害。
- ScriptableObject 配置、编辑器核心检查、场景引用检查与开发构建自动试玩入口。

Validation:

- PC 开发构建成功；核心检查和场景引用检查通过。
- 1600×900 / 种子 37 与 1280×720 / 种子 91，实际自动试玩各 43 项检查通过，验证三波自动战斗、奖励返回 Build 与 GameOver；完成运行画面检查。
- 微信小游戏导出、真机性能和人工操作体验尚未验收。

## 2026-10-06 — Stage 2 / A–F

- A：新增实体牌组/手牌管理，三张抽牌、选择高亮、成功消耗；取消固定首张 L。
- B：扩展为 13 项活跃奖励，攻速改 +12%，增加全塔射程、牌组/抽牌、路径减速、L 平台、经济、恢复。旧射程/单波金币奖励移出活跃池。
- C：Tank、Splitter 与两只 Shard；分裂预登记 Remaining，继承移动路段与波次倍率。第二波改 5 Normal +3 Fast；第三波改 6 Normal +2 Fast +2 Tank +2 Splitter。
- D：新增 Chill 35%/2 秒减速；Pulse 增加 0.65 AOE，原三塔其他基础值保留。四塔 UI、动态价格与定位、弹丸颜色区分。
- E：加粗路径、Spawn/Core 标识、手牌/塔高亮，保留已有反馈和范围预览。
- F：重整四份文档，明确代码/资产来源、公式、兼容奖励及当前例外。

Validation：引擎核心检查、场景配置与第二阶段牌组/奖励/分裂/减速检查通过；PC 开发构建成功。最终 PC 开发构建自动试玩 43 项通过（种子 37），包含四塔建造、三波真实战斗、分裂精确生成计数、三选一返回 Build、动态改路与 GameOver；程序截图已检查手牌形状、选择、四塔说明与奖励卡。人工手感、长期平衡与真机未验收。

## 2026-10-06 — 首轮 Blender 美术接入

- Blender MCP 原创 18 个模型，FBX + 18 个 Unity Prefab，23 种共享 URP 材质与背景材质，9 张透明模型图标；独立 .blend 源文件及可复现脚本。
- 新增 ArtCatalog、ArtVisual、RuinEnvironment 和 ArtIntegration；TowerData/EnemyData 持有视觉 Prefab，TowerData/BlockShapeData 持有图标。
- 替换地砖、墙、入口、核心、四塔与敌人占位外观；浮岛悬崖、植被、遗迹柱/火盆、青色符文、暖光、Bloom/ACES 与等距镜头。
- 塔/手牌图标、按钮描边、方向箭头；模型受击闪白适配多 Renderer，墙上塔视觉抬高。FBX 单位/坐标转换隔离在子节点，不影响逻辑根节点。
- 四份文档同步；记录用户提供的三项 CC0 参考资源。既有玩法、经济与波次数值保持。

Validation：引擎编译、美术引用/尺度/URP/无碰撞器检查、第二阶段逻辑检查、Windows 开发构建通过；带美术自动试玩 49 项通过（种子 37，1600×900），覆盖三波、四塔、分裂、奖励、改路与 GameOver。程序截图检查模型、图标、手牌、塔说明、建造区域与路径。人工手感、性能压力和长期平衡未验收。

## Stylized art - skeletal enemies + combat VFX (2026-10-07)
- Enemies Drifter/Skimmer/Bulwark/Splitter/Flyer/Boss are now skinned rigs (Blender armature) with SS_Move (loop), SS_Hit, SS_Death takes; Shard uses shader vertex wobble (M_Enemy_Shard).
- AC_Enemy_*.controller: Locomotion (default, speed = MoveSpeed float, default 1), Hit (trigger Hit), Death (AnyState, trigger Die).
- Toon shaders gained _Dissolve/_DissolveEdge/_DissolveColor and _Wobble/_WobbleFreq.
- New visual-only runtime scripts in Assets/Game/Scripts/VFX (namespace StoneSignal.VFX): EnemyHitFeedback, DamageNumbers/DamageNumber (TextMeshPro), CameraShake, HitStop, TeslaArc, StylizedVfx. No gameplay values changed; Game.unity/ArtCatalog untouched.
- 26 VFX prefabs in Assets/Game/VFX/Stylized (+ Resources/StylizedVFX/PF_FX_DamageNumber); showcase scene Assets/Game/Scenes/StylizedVFXShowcase.unity.
- Added com.unity.textmeshpro 3.0.9 + TMP Essential Resources (Assets/TextMesh Pro).

## Stylized art - level quality pass (2026-10-07)
- Dense clustered maple canopies with tint variants (M_Foliage_Warm/Gold/Deep); island cliff rebuilt (grass/leaf-litter top, mounds, 5 rock strata, uneven rim).
- New StoneSignal/Water shader (depth gradient, caustics, depth foam, sparkle) on M_Env_Water_Flat; warmer earthy ground tiles, lighter lavender walls, base AO (_BaseAO) on blocks/tiles/towers.
- StylizedArtDemo: warm key light, global post volume (Assets/Game/Settings/Stylized/VP_StylizedDemo.asset: bloom, warm WB, saturation, vignette); captures with Game.unity ortho camera. Game.unity/ArtCatalog untouched.

## Stylized art - level pass v4 (2026-10-07)
- StylizedArtDemo board enlarged to 16x12 (layout only), islands/spawn pushed out, wooden dock walkways + posts at board edges; old foam ring instances removed from the layout (SM_Env_FoamRing_01/PF kept).
- Water: more teal shallows, deeper range, visible toon ripple lines. Snow smaller/sparser.
- Glowing tower-slot rings and core glow rings (M_FX_SlotGlow, visual only). Game.unity/ArtCatalog/URP renderer untouched.

## 2026-10-07 — Stylized 美术接入玩法（塔 / 敌人 / 核心 / 战斗特效）

Added:

- `StoneSignal/Stylized art/Wire gameplay (towers, enemies, core, VFX)`（`Assets/Game/Editor/StylizedGameplayWiring.cs`）：幂等地把 Stylized 预制体与 VFX 写入 Towers/Enemies/ArtCatalog 资产；`Check gameplay wiring` 校验塔头、EnemyHitFeedback、Animator 与伤害数字预制体。注意：`Import third-party art pack` 会把这些字段改回 Kenney/Quaternius，之后需重跑本工具。
- `TowerData` 表现字段：`headName`（空 = 自动找 `*_Head`）、`muzzleHeight/Forward`、`lobbedShot/lobHeight`、`damageKind`、`muzzleVfx/projectileVfx/hitVfx/explosionVfx`、`explosionOnEveryHit/OnCrit/OnKill`、`impactShake`、`impactHitStop`；玩法字段 `critChance/critMultiplier`。
- `EnemyData` 表现字段：`deathVfx`、`coinVfx`、`splitVfx`、`splitChildScale`；`ArtCatalog.coreHitVfx`。

Changed:

- 映射：Needle→PF_Tower_Gatling_1x1、Pulse→PF_Tower_Tesla_1x1、Seismic→PF_Tower_Mortar_2x2、Chill→PF_Tower_Frost_1x1；Drifter/Skimmer/Bulwark/Splitter/Shard→PF_Enemy_*；核心→PF_Prop_Core；墙块→PF_Env_Rock_1x1，地块→PF_Env_Tile_Stone_A/B、路面→PF_Env_Tile_Dirt。Cannon/Flamer/Flyer/Boss 未接入。
- 度量改为 `tileTop` 0.25 / `blockTop` 0.6：塔预制体以底面为轴心，站在地面 0.25 或墙顶 0.6；敌人视觉站在地面 0.25。
- 瞄准时只旋转塔头（Tesla 无塔头不转）；Seismic 炮口取塔头包围盒顶部并沿 60° 上仰，炮弹走抛物线（仅视觉，命中时机不变）；Pulse 用 TeslaArc 瞬间电弧，逻辑弹体不可见。
- 敌人受击走 `EnemyHitFeedback.OnHit()`，移动速度驱动 `MoveSpeed`；击杀后注册表立即释放（波次/金币时序不变），视觉在 `PlayDeath` 回调后才销毁；死亡播放 DeathPuff + CoinPop，Splitter 额外 SplitBurst，Shard 用自身预制体不再缩放 0.65。
- 伤害数字改为 `DamageNumbers.Spawn`（按塔伤害类型着色，暴击放大）；敌人抵达核心播放 FX_Hit_Core + Heavy 震屏；Seismic 爆炸 Light 震屏；Needle 暴击 0.03s HitStop；普通子弹不震屏。`Time.timeScale` 改动前先 `HitStop.Cancel()`。

Validation：Tuanjie 2022.3.62t16 batchmode 编译通过；`StylizedGameplayWiring.BatchWire` problems=0；`Stage2Validation.Run` 全部 MILESTONE 与 SCENE REFERENCES PASS。Play 模式 VFX 尺寸未目检。

## Stylized art - level pass v5 (2026-10-07)
- StylizedArtDemo layout: serpentine dirt path through 22 tetromino walls (auto-placed, path-adjacent), 17 turrets (mortar on O, flamer on I, mixed 1x1), 20 enemies along the path. Layout only; no gameplay data changed.
- Square slot plates (FX shape 8: gold border, fill, diamond icon; M_FX_SlotPlate) under every turret. Contact sheet save now retries.

## Stylized art v6 (2026-10-07)
- New SM_Env_Board_Cliff_16x12_01 / PF_Env_Board_Cliff_16x12: cliff footprint 0.12-0.3 m beyond the 16x12 grid (VALIDATE tiles-on-terrain).
- Bridges fitted from the board edge to the measured island edge, both ends on land (VALIDATE bridge W/E/N).
- Water: smooth depth foam band and soft animated shore wave lines, no hard steps. Island and board have underwater shelves.
- Towers split into PF_Tower_X > Rig > SM_Tower_X_Base (static) + SM_Tower_X_Head (yaw) > SM_Tower_X_Barrel (pitch, Cannon/Mortar) > SM_Tower_X_Muzzle (+Z = fire direction).
- Walls rebuilt from per-cell pillowy weathered stones; toon _Mottle world noise. Rough, uneven tiles (visual only).
- Demo layout: 2x2 core at the centre, 3 routes (W/E/N) from spawn islands. Previews: level_gamecam_v6, level_wide_v6, shore_closeup_v6, blocks_v6.
- Preview capture falls back to *_new.png if the file is locked.


## Stylized art v7 (2026-10-07)
- Water: kept our own SS_Water (orthographic-safe, no Opaque Texture or renderer change). It now has fine ripples (scale 2.4), fainter caustics and a thin soft shore foam. Evaluated: mozankatip/InteractiveStylizedWater (MIT, Shader Graph, needs Opaque Texture) and tojynick/bmjoy Stylized-Water-Shader-Unity-URP (MIT; depth foam broken with an ortho camera). Rejected: LazyToonShader (GPL-3).
- Framing: 4 corner decoration islands only; demo game-cam ortho 6.4 (recommended for Game.unity, not applied).
- Walls: removed the brown moss top faces. Tiles: closed the open top n-gons that read as dark holes.
- Turrets: no gold slot plates; plinths are stone with metal trim.
- Docs/ArtDirection.md: value hierarchy and palette retune (darker low-contrast ground, mid walls, desaturated foliage).


## 2026-10-07 — 美术 v6 适配：塔骨架瞄准 + 中央 2×2 核心 + 多入口

Changed:

- 塔瞄准按 v6 层级 `Rig > *_Base / *_Head(yaw) > [*_Barrel(pitch)] > *_Muzzle(+Z)`：Head 偏航使 Muzzle 水平朝向对准目标（Tesla 线圈也会转），直射 Barrel 俯仰（±30°/次，仅 Cannon，未接入），抛射 Barrel 保持 28° 作者角；炮口特效、弹体、Tesla 电弧均从 Muzzle 位置/朝向发出。运行时不再读取渲染包围盒（删除 Tower 中 MortarBarrelTip 副本；编辑器 `StylizedVFXBuilder.MortarBarrelTip` 优先用 Muzzle）。
- 设计变更：Ember 核心（PF_Prop_Core）位于棋盘中央，占 2×2；敌人从多个边缘入口进入。新增 `BoardLayoutData`（`ScriptableObjects/Board/StylizedBoard.asset`，由 `GameConfig.layout` 引用）：16×12，入口 (15,3)/(0,8)/(7,0)（对应 level_layout.json 的 Blender W/E/N 栈桥），核心 (7,5) 起 2×2。
- `GridManager` 支持多 Spawn 与多格 Goal（`Spawns`/`CoreCells`，`spawn`/`goal` 保留为第一个以兼容）；A* 以任一核心格为终点；放置校验要求**每个入口**都能到达核心，堵死任一入口即拒绝。`PathfindingManager.CurrentPaths` 每入口一条，GridView 绘制全部路线与土路。
- 波次：`EnemyGroup.spawnIndex`（-1 = 轮流各入口，现有三波均为 -1）；Splitter 子怪从母体位置续行，不占轮转。
- 棋盘装饰：`ArtCatalog.boardCliff` = PF_Env_Board_Cliff_16x12，按网格缩放置于棋盘中心下方 0.55；每个边缘入口外放 4 块 PF_Env_Bridge_Plank；有悬崖时不再生成 Kenney 外圈装饰；`spawnPortal` 置空。
- Game.unity（由接线工具写入）：Grid 改 16×12、位置 (-8,0,-6)（中心仍在原点），正交尺寸 9.2→6.4（`GameCamOrthoV7`，见 Docs/ArtDirection.md）。
- `ThirdPartyArtIntegration.Checks` 的 "Block and landmarks assigned" 改为接受 entryBridge 代替 spawnPortal（多入口用栈桥标记）。
- 地块 ±0.03 m 起伏仅为视觉；玩法高度固定 ground 0.25 / wall 0.6，运行时无任何逻辑读取网格包围盒。
- Smoke test 改为与布局无关（每入口有路、不能封死核心、自动找合法绕路与塔位）。

## Stylized art v8 (2026-10-07)
- Water: depth gradient only, with a very soft shore fade. Removed ripples, caustics, wave lines and sparkles.
- VFX: scorch is smaller and fainter, smoke lighter. This removes the dark smudges on the board.
- Walls: desaturated lavender. Tiles: variants A-E, chosen per cell by a seeded hash with random 90-degree rotation and +-0.03-0.06 m height jitter (mean top 0.80). Tile B inset recoloured (it read as a hole).
- New PF_Env_LevelDressing_16x12 (water, board cliff, islands, trees, bridges W4/E4/N3, snow; static-batched), PF_Env_Tile_Stone_D/E.
- Placement feedback: StoneSignal/PlaceFX shader; PF_UI_PlaceGhost_Block/Tower, PF_UI_RangeRing, PF_UI_SlotHighlight, PF_Path_FlowSegment + M_Path_Flow; runtime PlacementGhost / RangeRing.
- Enemy facing check: all 7 enemies face +Z (Blender and Unity checks).


## Stylized art v9 (2026-10-07)
- Water: uniform deep blue, slightly darker far away, plus a thin soft light band (~0.2 m) hugging shores. No noise, voronoi, foam, ripples or textures; stale material properties are stripped.
- Island and board: sloped underwater skirts replace the flat shelves (no teal shelf polygons).
- Enemies: imported models faced -Z in Unity. The model child under Rig is now rotated 180 degrees (root and Rig stay identity). Facing check added.
- WebGL budget: trees about 1.2-1.4k tris (was 2.3-2.7k), fewer dressing trees, tile top subdivision 1. PF_Env_LevelDressing_16x12 is 64.8k tris / 70 renderers.


## Stylized art v10 (2026-10-07)
- Water: narrow glowing shore line (HDR _GlowColor, smooth falloff, slow 0.12 pulse, pure ALU).
- Dressing recomposed: TL/TR mid islets (maples, rocks, small dock), BL dark big-tree foreground, BR islet with stone ruin-lighthouse, sparse leaves/reefs. 58.6k tris (limit 90k).
- Game camera proposal: pitch 40, yaw 10, ortho 6.6, look-at (0,0.8,-2.2) (StylizedArtIntegration.GameCam*V10).
- UI sprites in Assets/Game/Art/Stylized/UI (orb, tower card frames, blueprint card, tower icons rendered from the models); ArtSource/Stylized/render_ui_icons.py.


## Stylized art v11 (2026-10-07)
- Water shore glow narrowed: _DepthRange 0.28 -> 0.08 with tighter falloff. The line is about 0.08 m wide (was about 0.35 m); glow and pulse kept.
- UI: Emberward-style crimson tower cards (white stroke, cost, hotkey badge, size tag for non-1x1). Sprites: ui_card_tower_frame, ui_badge_size_2x2, ui_badge_size_1x2, ui_badge_hotkey.


## v10 UI (ui_mockup_v3)
- TMP HUD rebuilt: core orb HP, gold pill, WAVE x/y banner, speed II/x1/x2/x3, TARGET cycle (EnemyManager.TargetMode), fanned tower cards with hotkey/size badges, blueprint block cards, stacked DRAW deck + BATTLE, hint pill.
- HUD sprites imported as Sprites + HUD.spriteatlas; ArtCatalog ui* fields; TowerData.icon wired.
- GameplayShot (-stonesignal-shot <png>): 1920x1080 in-game capture with real UI.


## Stylized art v12 (2026-10-07)
- PF_Env_LevelDressing_16x12: meshes merged per region x material (board + 4 quadrants, env/foliage) into Generated/MSH_Dressing_*.asset; shadows off for env/rock/skirt chunks and water, foliage keeps shadows; particle FX/lights preserved under FX.
- Foreground trees: removed front edge trees, BL islet pushed out and trees scaled down, TR islet moved below top-right UI.
- Foam ring geometry recolored to WaterDeep (thick white rims), only shader glow line remains.
- New 9-slice UI sprites ui9_* (navy button normal/pressed/selected, orange BATTLE, red wave banner, gold pill, panel) + ui_draw_pile, borders set in .meta.


## Stylized art v13 UI cards (2026-10-07)
- ui_card_tower_frame: opaque crimson #C8283C, white stroke, dark edge, baked cost strip; 9-slice L28 B64 R28 T28.
- ui_badge_size_1x2/2x2 cream pills with dark-red text (+ blank), ui_badge_hotkey 32px; ui_card_blueprint 128x128; ui_icon_block_T/L/J/S/Z/O/I at uniform 22px cells. Existing guids preserved.
- BL foreground islet pushed further out of hand UI zone.


## Stylized art v14 (2026-10-07)
- Front (N) spawn island: trees -> stump/log, lantern removed (block-card UI zone clear).
- Towers: no visible base mesh; SM_Tower_X_Base kept as empty pivot, heads/barrels/muzzles lowered onto wall top.
- New PF_VFX_CoinDrop + CoinDropFx (pooled coins, bounce, sparkle, fly-to-UI trail, pickup flash, counter punch).
- New PF_VFX_EnemyGround + EnemyGroundFx (blob shadow + distance-emitted dust, 2 s fade), added as child GroundFx to all PF_Enemy_*.
- Editor: StylizedFxV14.cs (builder, previews, checks) called from BatchImport.


## Stylized art v15 (2026-10-07)
- Fix: v13 UI .meta GUIDs were truncated (Tuanjie base64 GUIDs) -> assets ignored -> flat pink cards. Restored original GUIDs for ui_card_tower_frame, ui_card_blueprint, ui_badge_hotkey, ui_badge_size_1x2/2x2; batch now rewrites full importer settings.
- Ghost invalid red #E5484D (SS_PlaceFX ghost ignores mesh vertex colours); path flow alpha .45->.9; slot highlight lifted to 5 cm, queue 3110.
- Towers: low collar base plate per footprint (SM_Tower_X_Base mesh), heads re-seated. PlacementGhost.SetFootprint/SetCellValid; WallHighlight (shader _HiAmount/_HiColor MPB).
- EnemyGroundFx: blob only (winding fix, lift .07); global EnemyGroundFxSystem dust (cap 64).
- RewardFlyFx/RewardType/CoinDropFx (pooled, <=8 particles, Reset), RewardCounterUI; PF_VFX_RewardFly_Gold/Shard/Gem.
- Runes: 6 icons, glyph atlas, RuneInlay (shader MPB), TowerBuffIcons, PF_UI_ResonanceAura; block stack / draw / reward sprites; HUD atlas includes UI folder.
- Top-right islet moved to the right edge; one far edge tree removed.



## Stylized art v16 DRAW pile (2026-10-07)
- Rule: 2 draws/wave (1st FREE, 2nd rewarded video placeholder), no gold draws; hand persists, max 7.
- Sprites (original ember/rune look, no price on card): ui_draw_pile (top card back, navy + gold inlay + ember-rune emblem, 168x216 fixed), ui_draw_pile_layer (plain back for stack layers), ui9_draw_bubble (ivory status pill 176x72, 9-slice L30 B28 R30 T28), ui_draw_bubble_tail (26x15 notch), ui_icon_free (ember star 56). Reuses ui_badge_video_ad. ui9_draw_price_tag no longer used.
- DrawPileUI (StoneSignal.VFX): SetState(Free|Ad|Used|Full), SetStackCount(3-5), PlayDrawPulse(), SetHandCount(n,7); pill bobs +-4px/1.6s.
- Spec Docs/draw_pile_v16_spec.md + mockup Docs/draw_pile_v16.png; StylizedFxV14.BuildAtlas pins v16 borders.

## v16.1 / v16.2 (art, uncommitted, 2026-10-08)
- v16.1: toon wall/enemy props in UNITY_INSTANCING_BUFFER (_RuneIdx/_HiAmount/_HiColor/_GroundClip/_StatusTint/_StatusRim), RuneInlay resonance radius 1, DrawPileUI option B default (showTail=false) + Full state + HandCount + HLG icon/label fix, HUD-zone props removed from PF_Env_LevelDressing_16x12, EnemyStatusFx(+System) + ui_status_* icons. Measured 119 DC.
- v16.2: art-led SpawnPortal (painted rune circle, scorch/crack decals, Blender runestones+rubble, 4x4 flare). EnemyDeathFx (T_FX_DeathPuff_4x4 + ui_fx_skull, PF_VFX_EnemyDeath via BatchImport). DirectionIndicator sprites ui_dir_ring/ui_dir_arrow/ui_dir_arrow_pressed/ui_dir_pulse in HUD atlas folder.
- GridView.DrawFlow change dropped (dev fixed flow arrows in b65d8fb/bc8e310).
- v16.2 (cont.): level_layout.json source: 7 HUD-zone props removed (RockPile, 3 grey Rock blocks, Rock_Small, Stump, Log); SS_Water subtle shore ripples + SpawnRipple.Play (0 extra DC); core enclosure SM_Core_Enclosure_Intact/Cracked/Broken + CoreDamageFx; RewardPickUI + ui9_reward_frame_* / gems / ribbon / sheen / flame edge (borders pinned in StylizedFxV14).
- v17: SM_Enemy_Flyer_01 remade as cartoon bird (build_stylized_batch1.py flyer(), same rig/clip names), FlyingMotion (heightOffset 1.2, bobAmp 0.12, bankMax 28) auto-added by EnemyGroundFx.SetFlying(true), flyer shadow 0.55 + optional flyingBlobMaterial; RewardPickUI v17 chunky toon cards (ui9_reward_frame_*/band_*/tier_pill), old ornate reward sprites removed (Docs/reward_pick_v17.md).
