# 游戏机制笔记

适用：本机 Terraria 1.4.4.9 / tModLoader 2026.08.3.0。记录日期：2026-10-21，最后增补：2026-11-18。

这是随开发按需补充的小型资料笔记。玩法优先查 [Official Terraria Wiki](https://terraria.wiki.gg/)，代码接入以本机版本和项目实际行为为准；不凭印象填写数值，不预先抓取全站或建设数据库。Wiki 新版本规则不直接当成本机旧版本规则。

## 移动与局部地形（2026-10-22）

按键组合由原生 Player 物理处理，方向跑跳同时设置水平输入和跳跃，不直接改坐标。局部支撑与障碍复用 Collision.SolidCollision，脚下探测使用 acceptTopSurfaces=true 以包含平台；接口签名对照 [tML 2026.08 Collision 文档](https://docs.tmodloader.net/docs/stable/class_collision.html)，并由本机编译和地形实测确认接入。

[Official Terraria Wiki 的平台说明](https://terraria.wiki.gg/wiki/Platforms)记载站在平台上按 Down 可下落。本项目 down 接入原生 controlDown，默认 0.25 秒，不改碰撞或位置；目前尚未完成站在平台上的正向实测。向下输入不能解释成穿过实心地面。地形字符网格只是碰撞概览，液体种类和未知区域不作安全推断。

## 初始武器与测试武器

原失败点(2103,295)的现场检查为Grass，上方(2103,294)是Trees，isTreeTrunk=true、frameX=0、frameY=132，CanKillTile=false。本机WorldGen.CanKillTile的树干支撑分支返回false；Player.PickTile随后将本次挖掘伤害置0，因此会按冷却挥镐但既不变泥土也不移除。该规则也见 [Official Terraria Wiki 方块说明](https://terraria.wiki.gg/wiki/Block)，API见 [WorldGen文档](https://docs.tmodloader.net/docs/stable/class_world_gen.html)。修复遵守该规则，未强制移除树或支撑。

2026-10-22 新增 `/tpa equip pick`，向AI slot1发放测试铜镐（CopperPickaxe，item=3509，pick=35），保留slot0原有测试剑。这是单人开发物资；2026-11-16 多人服务器 Spawn 现已实测按普通经典三件初始化，单人历史流程不变。

普通挥动镐接入本机 `ItemCheck_Inner → ItemCheck_OwnerOnlyCode → ItemCheck_UseMiningTools → PickTile`；前两层跳过额外玩家槽位的归属检查作有限适配。工具目标 `Player.tileTargetX/Y` 是共享静态字段，只在本次原生范围检查和挖掘方法调用内替换，并用 finally 恢复；不切换 Main.myPlayer，不写真人鼠标位置。`IsTargetTileInItemRange` 和 `PickTile` 签名见 [tML Player 文档](https://docs.tmodloader.net/docs/stable/class_player.html)，本机IL摘录保存在被Git忽略的 `.local/research`。

本次原生铜镐实测同一草地方块先从type2变为type0泥土，15ticks后才真正移除。仅看到类型变化不能当作挖掉；拾取仍需背包证据。

2026-11-18 批次4核对：铜短剑的原生使用字段和命中链已整理到下方“原生武器机制研究”，本节表格只保留物品身份和当前支持状态。

| 名称 | 本机 ItemID | 项目实际用途与状态 |
| --- | --- | --- |
| 铜短剑 Copper Shortsword | `CopperShortsword = 3507` | 多人经典发放、默认选中、刺击剑身与固定傀儡原生命中已验；单人仍使用历史测试物资 |
| 铜阔剑 Copper Broadsword | `CopperBroadsword = 3508` | 与铜短剑是不同物品；本项目未验证 |
| 铁阔剑 Iron Broadsword | `IronBroadsword = 4` | 多人隔离夹具下确认史莱姆击杀和同一傀儡的重复挥砍；单人测试装备也已有实测 |
| 木剑 Wooden Sword | `WoodenSword = 24` | `/tpa equip wood` 额外发放的测试装备，已实测命中恶魔眼 |

本机依据：tML 2026.08.3.0 程序集（本次离线核对使用 `.local/tml/2026.08.3.0` 副本，原安装目录为 `F:\SteamLibrary\steamapps\common\tModLoader`）中 `Terraria.ID.ItemID` 常量；`UICharacterCreation.SetupPlayerStatsAndInventoryBasedOnDifficulty` 的普通初始武器发放使用 3507／3509／3506 并各调用 `Prefix(-1)`。`Player` 构造函数初始化背包物品对象，但不执行建角发装备流程；批次3的 `DemoPlayerSystem.Spawn` 只在多人服务器、发布 actor 之前调用 `StartingInventory`，只发放普通经典三件，失败不留下 AI；单人仍由 `Equip` 向 slot 0 发放测试剑。

项目证据：`scripts/smoke-test.ps1 -Combat` 显式执行 equip；`.local/test-results/smoke-20261021-215122.json` 中 AI slot=1 的命中事件记录 item=4 或 item=24。不能把这些结果写成“默认铜短剑已经通过”。

Wiki 查阅入口：[Copper Shortsword](https://terraria.wiki.gg/wiki/Copper_Shortsword)、[Iron Broadsword](https://terraria.wiki.gg/wiki/Iron_Broadsword)。本次全文访问返回 403，以上物品 ID、建角流程和短剑 useStyle／projectile 路径已由本机程序集确认；不声称已经读取这些条目的全文。

## 死亡与自动重生

玩家死亡后自动重生是队友持续游玩的必要行为，本批已接入。Wiki 入口：[Death](https://terraria.wiki.gg/wiki/Death)、[Spawn](https://terraria.wiki.gg/wiki/Spawn)、[Expert Mode](https://terraria.wiki.gg/wiki/Expert_Mode)。Wiki 的重生位置说明为有效床出生点或世界出生点；专家模式说明普通计时从 10 秒增加至 15 秒。本项目使用本机原生计时，不硬编码这些秒数。

本机依据：`GetRespawnTime` 在普通单人世界给出 600 ticks；`UpdateDead` 对额外槽位也递减 respawnTimer，但实际 Spawn 分支限于本地玩家。本项目在原生 UpdateDead 后为 AI 补调重生，保留同一 Player 和背包。`Spawn(ReviveFromDeath)` 原生恢复生命、位置和出生保护，配合 FindSpawn / CheckSpawn 使用有效出生点，否则回世界出生点。当前 AI 没有设置床的入口，因此只验收世界出生点；中核／硬核和金币惩罚尚不在本批范围。remove → spawn 仍是创建新对象的开发操作，不等同于原角色复活。

## 维护方式

每次只补当前功能用到的结论和来源；遇到误解或 bug 就更正对应条目，不增加重复核对流程。玩法规则、项目实现、已经实测的结果分别写清楚。

攻击支持按机制抽样，不逐把验收。点击或长按只是输入；具体可能走挥砍命中、刺击、发射弹丸、消耗弹药或持续引导等路径。同机制优先复用，新增路径选一个代表测通即可。Wiki 的 [Swords](https://terraria.wiki.gg/wiki/Sword) 区分短剑朝光标刺击与阔剑挥砍；这不等于它们必须按每种金属分别适配。批次4已选铜短剑刺击，批次5已选 Iron Broadsword 挥砍作为代表；枪械、弓箭、魔法、召唤和鞭子仍留到实际需要时接入。

## 原生武器机制研究（批次5，2026-11-18）

依据本机 tML 2026.08.3.0 程序集与 IL；未在游戏中执行。`useStyle`、`shoot`、`noMelee` 只用于辅助判断，不能单独证明两件武器走同一条命中链。

### 共用主流程

- `Player.Update` 调用 `ItemCheckWrapped`，后者对每个玩家槽调用 `Player.ItemCheck`；`ItemCheck` 经 `PlayerLoader.PreItemCheck` 进入 `ItemCheck_Inner`。这条外层流程没有按 `Main.myPlayer` 直接跳过整个虚拟槽。
- `ItemCheck_Inner` 开头的 `Main.myPlayer` 门控、`ItemCheck_OwnerOnlyCode` 的 `whoAmI == Main.myPlayer` 门控，以及 `ItemCheck_Shoot` 在非本地时的提前返回，是服务器 AI 需要逐项处理的客户端身份条件。
- `controlUseItem` / `releaseUseItem` 驱动使用输入；`itemAnimation`、`itemTime`、`itemAnimationMax` 和冷却由原生 `ItemCheck` 维护。`ItemCheck_ApplyUseStyle_Inner` 开头有 `Main.dedServ` 早退；专用服务器跳过使用样式的视觉位置／旋转。
- `ItemCheck_GetMeleeHitbox` 在 `Main.dedServ` 时跳过按手持物品帧重算矩形，改用 `itemLocation`（由服务器侧 `ItemCheck_ApplyHoldStyle_Inner` 设置）作为基础；因此服务器近战命中框比本地客户端简化，必须实机确认。
- 直接近战命中走 `ItemCheck_OwnerOnlyCode → ItemCheck_MeleeHitNPCs → ProcessHitAgainstNPC → NPC.StrikeNPC`；`ProcessHitAgainstNPC` 经 `CombinedHooks.OnPlayerHitNPCWithItem` 调用 `ItemLoader.OnHitNPC`、`NPCLoader.OnHitByItem`、`PlayerLoader.OnHitNPCWithItem`。IL 中这些调用没有额外的 `netMode` 早退；服务器模拟是否确实触发仍需实机。
- 射弹武器走 `ItemCheck_Shoot → Projectile.NewProjectile`，owner 由传入的 player 槽位决定；服务器权威 Actor 可以拥有 projectile。射弹命中由 `GlobalNPC.OnHitByProjectile`（服务端、按 owner）观察，不依赖客户端 `ModPlayer.OnHitNPCWithProj`。
- 瞄准：本地客户端主要使用 `Main.mouseX/Y`、`itemRotation` 和 `fullRotation`；专用服务器没有真实鼠标，`useStyle` 位置计算又会被 `dedServ` 跳过。当前 AI 只支持水平左右方向，不提供完整二维瞄准。
- 资源：弓箭的 `useAmmo` 路径在 `ItemCheck_Shoot` 中经 `PickAmmo` 选弹并消耗；魔法武器的魔力检查／消耗在原生物品使用流程内；召唤、鞭子和持续类武器还有各自的特殊分支。本批只研究，不统一抽象。

### 代表性机制

| 类型 | 代表物品 | 原生物品使用路径 | 关键输入／资源 | 关键归属或客户端条件 | 当前 AI 支持 |
| --- | --- | --- | --- | --- | --- |
| 普通挥砍 | Iron Broadsword（4） | `ItemCheck_Inner → ItemCheck_OwnerOnlyCode → ItemCheck_MeleeHitNPCs → ProcessHitAgainstNPC → NPC.StrikeNPC` | `controlUseItem`；无弹药；原生冷却 | `Main.myPlayer` 两处门控；dedServ 近战框使用 `itemLocation` | Host & Play 已验证（隔离测试物资） |
| 短剑刺击 | Copper Shortsword（3507） | `ItemCheck_Inner → ItemCheck_OwnerOnlyCode → ItemCheck_Shoot → CopperShortswordStab(938)` | `useStyle=13`、`shoot=938`、`noMelee=1`；无弹药；原生冷却 | `ItemCheck_Shoot` 非本地早退；服务器射弹 owner | Host & Play 已验证 |
| 弓箭 | Wooden Bow（39） | `ItemCheck_Shoot → PickAmmo → projectile` | 弓与箭 `useAmmo`；消耗箭 | `ItemCheck_Shoot`／瞄准；弹药库存 | 未实现，仅研究 |
| 魔法 | Wand of Sparking（3069） | 原生使用 → `ItemCheck_Shoot` → 魔法 projectile | 魔力检查／消耗；瞄准 | 客户端使用样式、魔力、射弹 owner | 未实现，仅研究 |
| 召唤 | Slime Staff（1309） | 原生使用 → minion 生成／召唤物所有权 | 魔力；召唤槽 | `ItemCheck_MinionAltFeatureUse` 等特殊分支 | 未实现，仅研究 |
| 鞭子 | Bone Whip（5074） | 原生使用 → 鞭击／特殊射弹或命中 | 原生冷却；非普通挥砍 | 特殊命中与客户端表现 | 未实现，仅研究 |

### 本批结论

- 普通挥砍与铜短剑可以共用：`IsAuthoritativeActor` 身份保护、`DemoPlayerSystem.Attack` 输入时长、Stop／Remove／死亡／目标替换清理、`Snapshot` 和 `combatEvents` 观察。
- 两者不能共用同一条命中适配：普通挥砍需要 `ItemCheck_OwnerOnlyCode` 的直接近战门控，铜短剑需要 `ItemCheck_Shoot` 的射弹门控；命中记录也分别是 `item-hit` 与 `projectile-hit`。
- 当前仍保留两个受限 ID：`IronBroadsword` 和 `CopperShortsword`。这是为避免把尚未验证的武器一次性开放，不是长期 registry；后续只有在同一机制实机通过后才考虑扩大范围。
- 专用服务器的铁阔剑复用原生挥砍姿势计算和 `UpdateMeleeHitCooldowns`，并避开专属客户端的乐器逻辑。同一个固定傀儡在多条攻击指令下重复触发原生 `item-hit`。
- 铜短剑的横向输入替代专用服务器没有有效操作者的鼠标瞄准。服务器跟踪发射该射弹的 `Player` 对象，放行 NPC 原生命中并广播 `SyncProjectile`。客户端采用原生刺击射弹绘制剑身。
- 原生 `UpdateBuffs` 倒计时和玩家更新中的到期清除限权威 AI 放行。本机程序集已验证。拾取复用 `GrabItems`／`PickupItem` 并广播地面堆叠变化；Host & Play 已确认墓碑入包。
