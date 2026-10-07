# 岩光防线 / StoneSignal

团结引擎 1.10.4 / 2022.3.62t16，URP、C#、原生 UGUI。当前为 **第二阶段：玩法成型版，A–F 已实现**，视觉层已全量替换为两个 CC0 第三方素材包：Kenney Tower Defense Kit（棋盘、墙块、塔）和 Quaternius Animated Monsters（带骨骼动画的敌人）。

继续开发固定顺序：完整读取 README → DESIGN → BALANCE → CHANGELOG，再检查代码、场景、Packages 和资产，详见 AGENTS.md。ScriptableObject 是基础配置真实数据源，Markdown 不参与运行时加载。

## Play 与操作

1. 打开 `Assets/Game/Scenes/Game.unity`，Play。
2. Build 抽取三张手牌；点击下方卡牌切换，鼠标预览，左键放置，R / 右键旋转。成功消耗选中牌，失败不消耗。
3. 1–4 选择 Needle / Pulse / Seismic / Chill，点击合法空地或墙块建塔；B 返回方块工具。右侧显示当前价格、伤害、攻速、定位，鼠标预览范围。
4. Space / Start Wave 开波。手牌耗尽提示可开始下一波；允许提前开波，避免无合法落点卡住。
5. 波次结束进入 Reward，三选一立即生效，返回 Build 重抽；基地归零 GameOver，可重开。

初始 200 Gold、30 HP。保留现有 `allowCombatBlocks=true` 原型例外：Combat 可消耗剩余手牌，塔仅限 Build。

## 实现状态

- A：I/O/L/T/S 实体牌组、随机无放回抽牌、抽空重洗、可选手牌、高亮、消耗和空手提示。
- B：13 项数据奖励，包含全塔、抽牌/牌组、路径减速、额外平台、经济和基地强化。
- C：Normal / Fast / Tank / Splitter；分裂出两只 Shard，Remaining 包含待生成单位和分裂子怪。
- D：四座塔；Needle 单体、Pulse 小范围高频、Seismic 重炮 AOE、Chill 减速组合。
- E：加粗路径、Spawn/Core 标识、红绿预览、选中高亮、范围圈；保留闪白、缩小死亡、粒子、命中反馈和浮动伤害。
- F：四份文档同步，资产数值及公式见 BALANCE。
- 美术：棋盘、墙块、四塔、地标、装饰、敌人全部来自 CC0 素材包；敌人带行走与死亡骨骼动画；道路随实时路径动态切换。

## 职责与检查

原有 Grid、A*、PlacementValidator、动态改路、EnemyManager、Tower/Projectile、WaveManager 和状态机继续使用。新增 `BlockDeckManager` 管牌组、`BlockHandManager` 管手牌；RewardData 执行效果，RunModifiers 保存本局修正，不修改基础资产。

`Assets/Game/Settings/GameConfig.asset` 引用形状、塔、波次、奖励与 Palette。`Assets/Game/ScriptableObjects/` 存基础数据；场景由 GameBootstrap 组装，运行时实例化美术 Prefab；缺失模型时保留 Primitive 回退。

编辑器菜单 `StoneSignal > Run stage 2 checks` 验证核心逻辑、资产引用、手牌消耗、奖励、分裂与减速。`Run core checks` 保留第一版检查。`Create or open game scene` 只创建缺失资产，不覆盖已有配置。`Migrate stage 2 asset references` 是显式迁移入口，仅迁移时使用。

开发构建支持 `-stonesignal-smoke -seed 37 -captureDir <目录>` 自动试玩。第二阶段验证记录在 `Verification/Stage2/`，本次第三方美术验证记录在 `Verification/Art2/`，构建日志在 `art-import.log` / `art-build.log`。

## 限制与下一步

UI 英文，未做长期平衡与人工手感验收；没有存档、联网、SDK、商店或广告。无限波继续复用第三波，基础 HP 线性增长。奖励可能重复，地图空间有限；额外平台无安全落点时不生成。优先人工试玩前十波，调手牌预算、塔组合和经济。

## 第三方美术 / 导入工具

素材来源、下载直链、CC0 依据和实际使用的模型清单见 `ArtSource/REFERENCES.md`。两个原始压缩包和解压内容都保留在 `ArtSource/ThirdParty/`，导入可复现。

- `Assets/Game/Art/ThirdParty/Kenney`：19 个 FBX + `colormap.png` 图集。
- `Assets/Game/Art/ThirdParty/Monsters`：Slime / Bat / Skeleton / Dragon 四个带动画的 FBX。
- `Assets/Game/Materials/Art`：`KenneyAtlas` 图集材质、四种塔色调变体、按怪物 `.mtl` 解析出的纯色材质、底板材质。
- `Assets/Game/Prefabs/Art`：棋盘件、装饰件、四座塔的组合 Prefab、五个敌人 Prefab。
- `Assets/Game/Animation/Monsters`：每只怪物一套 `SS_Move` / `SS_Death` 片段和 AnimatorController。
- `Assets/Game/Settings/ArtCatalog.asset`：环境与塔部件引用，以及 `tileTop` / `blockTop` 实测尺寸。

编辑器菜单 `StoneSignal > Import third-party art pack` 从 `ArtSource/ThirdParty/` 重新导入并接入：清空旧导入、复制模型、配置导入器、把 FBX 内嵌材质重映射到 URP 材质、拼装塔、生成动画控制器与图标、写入 ArtCatalog 与数据资产，并设置明亮日照、Neutral 色调映射与 Bloom。每次运行都是全量重建，结果幂等。`Check art pack` 检查单位尺度、引用、URP 材质、无碰撞器和敌人动画契约。

模型坐标校正放在 Prefab 的 Rig 子节点，逻辑根节点保持单位缩放；墙上塔抬高量改为读 `ArtCatalog.blockTop - tileTop`（0.50 Unit），不再硬编码。范围判定仍使用原逻辑位置。

第一轮 Blender 原创美术已退出运行时：`.blend` 与生成脚本保留在 `ArtSource/` 留档，原 `Import Blender art pack` 工具已删除。

验证：引擎美术引用/尺度/URP 检查、第二阶段逻辑检查、Windows 开发构建通过；带新美术的自动试玩 51 项通过（种子 37，1600×900），含新增的「敌人 Prefab 携带行走与死亡动画」检查，覆盖三波战斗、四塔建造、分裂、奖励、改路与 GameOver。截图在 `Verification/Art2/`。人工手感、性能压力和长期平衡未验收。
