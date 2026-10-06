# 岩光防线 / StoneSignal

团结引擎 1.10.4 / 2022.3.62t16，URP、C#、原生 UGUI。当前为 **第二阶段：玩法成型版，A–F 已实现**，已接入首轮 Blender 原创低多边形模型、共享 URP 材质和模型图标。

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

## 职责与检查

原有 Grid、A*、PlacementValidator、动态改路、EnemyManager、Tower/Projectile、WaveManager 和状态机继续使用。新增 `BlockDeckManager` 管牌组、`BlockHandManager` 管手牌；RewardData 执行效果，RunModifiers 保存本局修正，不修改基础资产。

`Assets/Game/Settings/GameConfig.asset` 引用形状、塔、波次、奖励与 Palette。`Assets/Game/ScriptableObjects/` 存基础数据；场景由 GameBootstrap 组装，运行时实例化美术 Prefab；缺失模型时保留 Primitive 回退。

编辑器菜单 `StoneSignal > Run stage 2 checks` 验证核心逻辑、资产引用、手牌消耗、奖励、分裂与减速。`Run core checks` 保留第一版检查。`Create or open game scene` 只创建缺失资产，不覆盖已有配置。`Migrate stage 2 asset references` 是显式迁移入口，会重设第二阶段引用与波次组，仅迁移时使用。

开发构建支持 `-stonesignal-smoke -seed 37 -captureDir <目录>` 自动试玩。第二阶段验证记录在 `Verification/Stage2/`，编译/构建日志在 `stage2-build.log`；最终核心/第二阶段检查及 PC 开发构建通过；自动试玩 43 项通过，截图已检查，详细结果见 CHANGELOG。

## 限制与下一步

UI 英文，未做长期平衡与人工手感验收；美术为首轮低多边形模块，无骨骼动画；没有存档、联网、SDK、商店或广告。无限波继续复用第三波，基础 HP 线性成长。奖励可能重复，地图空间有限；额外平台无安全落点时不生成。优先人工试玩前十波，调手牌预算、塔组合和经济。

## 首轮美术 / Blender MCP

参照用户提供的遗迹塔防图片，Blender MCP 制作 18 个原创模块：石台 2、墙、悬崖、植被、遗迹柱、火盆、传送门、核心、四塔、五种敌人外观。原模型集合共 9,552 三角形；晶体/金属/石块等 23 种共享 URP 材质，另有背景材质。塔与方块共 9 张透明渲染图标。

- `ArtSource/StoneSignal_Art.blend`：独立 Blender 源文件，保留原先打开的其他项目文件。
- `ArtSource/create_stonesignal.py` / `art_manifest.json`：可复现的 Blender 建模、FBX/图标输出及材质/面数清单。
- `Assets/Game/Art/Models`、`Art/Icons`：FBX 与透明 PNG。
- `Assets/Game/Prefabs/Art`、`Materials/Art`：Unity 模型 Prefab 和 URP 材质。
- `Assets/Game/Settings/ArtCatalog.asset`：环境美术；塔/敌人的数据资产持有模型引用，塔/Block 持有图标。

编辑器菜单 `StoneSignal > Import Blender art pack` 从清单重新导入和接入，并设置等距镜头、暖光、Bloom/ACES；会更新美术材质、Prefab 和场景表现。`Check art pack` 检查单位尺度、引用、URP 和无碰撞器。模型坐标校正放在 Prefab 子节点，逻辑根节点保持单位缩放；墙上塔只抬高视觉/发射位置，不改变射程判定坐标。

路径增加方向箭头。受击闪白适配模型所有 Renderer，死亡缩小保留。环境装饰不占网格，不使用玩法随机数。数值及波次沿用第二阶段。

验证：引擎美术引用/尺度检查、第二阶段逻辑检查、PC 开发构建及实际自动试玩 49 项通过。截图在 `Verification/Art/`。第三方参考与授权页见 `ArtSource/REFERENCES.md`。下一步优先人工检查各格预览、墙上建塔和颜色可读性，再增加少量环境变体。
