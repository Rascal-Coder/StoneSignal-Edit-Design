# 当前数值 / Stage 2

核对：2026-10-06，依据 ScriptableObject 与代码。继续工作先按 README → DESIGN → BALANCE → CHANGELOG 读取；Markdown 不参与运行时加载。

## 基础与 Block

初始 Gold 200，Core HP/MaxHP 30/30；Grid 16×10，Cell Size 1，Spawn (0,4)，Goal (15,4)。每 Build 基础抽牌 3，免费放置；剩余手牌不保留。初始牌组每形状一张，随机无放回，耗尽重洗。Combat 放牌开关 true，塔只能 Build 建造。

| 形状 | 未旋转坐标 |
|---|---|
| I | (0,0) (0,1) (0,2) (0,3) |
| O | (0,0) (1,0) (0,1) (1,1) |
| L | (0,0) (0,1) (0,2) (1,0) |
| T | (0,0) (1,0) (2,0) (1,1) |
| S | (0,0) (1,0) (1,1) (2,1) |

全为四格 Blocked，旋转 90° 归一化。L 平台奖励最多额外一格安全相邻墙平台，不可堵路。

## 塔（Towers/*.asset）

| 名称 | Cost | Damage | 攻击/s | Range | 弹速 | AOE | 定位 |
|---|---:|---:|---:|---:|---:|---:|---|
| Needle | 50 | 12 | 1.15 | 4.4 | 13 | 0 | 单体输出 |
| Pulse | 70 | 4 | 3.3 | 3.6 | 16 | 0.65 | 高频小范围清杂 |
| Seismic | 100 | 30 | 0.55 | 4.6 | 9 | 1.35 | 大范围重炮 |
| Chill | 80 | 3 | 1.4 | 4 | 12 | 0 | 减速 35%，2 战斗秒 |

AOE 内完整伤害，无衰减，主目标不重复结算。Chill 与路径减速相乘；命中先附加减速再伤害。更强减速优先，重复命中刷新，不加法叠加。

## 敌人（Enemies/*.asset）

| 类型/资产 | HP | Speed | 击杀 Gold | Core 伤害 | 外观 |
|---|---:|---:|---:|---:|---|
| Normal / Drifter | 32 | 1.25 | 12 | 2 | Slime，目标身高 1.0 |
| Fast / Skimmer | 22 | 2.3 | 16 | 3 | Bat，目标身高 0.9 |
| Tank / Bulwark | 110 | 0.75 | 25 | 5 | Skeleton，目标身高 1.4 |
| Splitter | 42 | 1.1 | 14 | 3 | Dragon，目标身高 1.15 |
| Shard（子怪） | 12 | 1.8 | 4 | 1 | 缩小版 Slime，目标身高 0.6 |

Splitter 仅击杀分裂两只 Shard，不递归分裂；子怪继承波次 HP/speed 倍率，生成于母体位置并沿当前移动目标格继续前进。逃脱不给 Gold。**HP、Speed、Gold、Core 伤害全部未改动**，只有外观模型换了。

## 波次（Waves/*.asset）

| 波 | Normal | Fast | Tank | Splitter | 初始总数 | 含全部分裂的最大总数 |
|---|---:|---:|---:|---:|---:|---:|
| 1 | 5 | 0 | 0 | 0 | 5 | 5 |
| 2 | 5 | 3 | 0 | 0 | 8 | 8 |
| 3 | 6 | 2 | 2 | 2 | 12 | 16 |

各组依序生成，间隔 0.7 秒，基础 hpScale/speedScale 均 1。第一只立即生成。第 4 波起复用第三波；额外 HP 倍率 `1 + max(0, W-3)*0.2`，线性增加，速度不成长。0.2 仍为现有 WaveManager 常量。

## 活跃奖励（13 项，等概率三选一）

| 资产 / Effect | amount | 效果 |
|---|---:|---|
| AllDamage | 0.10 | 全塔伤害 ×1.10 |
| AllAttackSpeed | 0.12 | 全塔攻速 ×1.12 |
| AllRange | 0.15 | 全塔范围 ×1.15 |
| CannonRadius | 0.20 | Seismic 半径 ×1.20 |
| AddBlock | 1 | 本局加入一张 O，每 Build 抽牌永久 +1 |
| NextDraw | 1 | 下一次抽牌 +1，使用后清零 |
| PathSlow | 0.08 | 所有敌人路径速度 ×0.92 |
| BonusSlot | 1 | 之后放置 L 时最多增加一格安全平台 |
| KillGold | 0.20 | 本局击杀 Gold ×1.20 |
| WaveGold | 30 | 每波开始 Gold +30 |
| TowerDiscount | 0.10 | 本局塔价 ×0.90 |
| BaseHP | 10 | 当前和最大 HP 各 +10 |
| WaveHeal | 2 | 每波结束恢复 2 HP，最多 MaxHP |

倍率乘法、整数加法叠加。旧 ArrowRange 和 NextWaveGold 保留兼容代码/资产，未列入活跃奖励池。平台奖励不回溯已放 L；无安全格不生成。

## 实际公式

```text
抽牌数 = blocksPerBuild + ExtraDraw + NextDraw；随后 NextDraw = 0
Damage = data.damage * Damage倍率
AttackRate = data.attacksPerSecond * AttackSpeed倍率
AttackInterval = 1 / AttackRate
Range = data.range * AllRange（Needle 另乘兼容旧 ArrowRange）
Splash = data.splashRadius（Seismic 另乘 CannonRadius）
Cost = max(1, RoundToInt(data.cost * TowerCost))
EnemyHP = data.hp * wave.hpScale * extraScale
EnemySpeed = data.moveSpeed * wave.speedScale * EnemySpeed倍率 * 当前减速倍率
KillGold = RoundToInt(data.reward * KillGold倍率 * CurrentWaveGold)
```

兼容旧 NextWaveGold 开波转入 CurrentWaveGold 并清为 1，下次开波恢复；活跃 KillGold 为本局持续倍率。整数使用 Unity RoundToInt（.5 取最近偶数）。新塔继承本局强化，已建塔属性实时读取；弹丸发射时记录伤害/半径。

表现常量：无目标 0.1 秒重试，命中容差 0.1、超时 6 秒，闪白 0.09 秒、死亡缩小 0.22 秒、伤害数字 0.7 秒。**敌人死亡动画先播放，播完再进入 0.22 秒缩小**；总时长 = 死亡片段长度 + 0.22 秒。无护甲、暴击或抗性。修改资产后同步本文及规则，不能只改生成模板。

## 美术数值核对 / 2026-10-06（第三方素材包）

本次只换视觉层，**塔、敌人、牌组、奖励、波次和全部基础数值沿用上表**。以下是从 Kenney / Quaternius 模型实测得到的美术尺寸，写在 `ArtCatalog` 里供代码读取，不参与任何玩法判定。

| 量 | 值 | 来源 |
|---|---:|---|
| Kenney 格子宽 / 高 | 1.00 / 0.20 | `tile` 包围盒 |
| `ArtCatalog.tileTop` | 0.20 | 所有棋盘件 Rig 抬升值 |
| `wood-structure` 高 | 0.50 | 玩家墙块包围盒 |
| `ArtCatalog.blockTop` | 0.70 | tileTop + 墙块高 |
| 墙上建塔视觉抬高 | 0.50 | `blockTop - tileTop`；此前为硬编码 0.62 |
| 塔部件堆叠 | 底座 0–0.21，中段 0.21–0.81，武器 0.81 起 | 逐件包围盒 |
| 敌人缩放 | 按目标身高 / 实测高度，水平居中 | 见上表「外观」列 |

怪物原始高度：Slime 1.95、Bat 4.68、Skeleton 5.00、Dragon 3.50。Kenney `tile-wide-*` 路网件实测为 2 格长，与 1×1 网格不匹配，未使用。

塔图标和方块图标改由引擎内离屏渲染从新模型生成（256×256 透明 PNG），不再是 Blender 外部渲染。方块图标按 `BlockShapeData.cells` 摆放四块墙块，每块缩到 0.86 以留出缝隙。
