# Changelog

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
