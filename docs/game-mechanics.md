# 游戏机制笔记

适用：本机 Terraria 1.4.4.9 / tModLoader 2026.08.3.0。记录日期：2026-10-21。

这是随开发按需补充的小型资料笔记。玩法优先查 [Official Terraria Wiki](https://terraria.wiki.gg/)，代码接入以本机版本和项目实际行为为准；不凭印象填写数值，不预先抓取全站或建设数据库。Wiki 新版本规则不直接当成本机旧版本规则。

## 初始武器与测试武器

| 名称 | 本机 ItemID | 项目实际用途与状态 |
| --- | --- | --- |
| 铜短剑 Copper Shortsword | `CopperShortsword = 3507` | 普通新角色初始武器；当前 AI 尚未接入其发放及攻击支持 |
| 铜阔剑 Copper Broadsword | `CopperBroadsword = 3508` | 与铜短剑是不同物品；本项目未验证 |
| 铁阔剑 Iron Broadsword | `IronBroadsword = 4` | `/tpa equip iron` 额外发放的测试装备，已实测命中史莱姆和僵尸 |
| 木剑 Wooden Sword | `WoodenSword = 24` | `/tpa equip wood` 额外发放的测试装备，已实测命中恶魔眼 |

本机依据：安装目录 `F:\SteamLibrary\steamapps\common\tModLoader\tModLoader.dll` 中 `Terraria.ID.ItemID` 常量；`UICharacterCreation.SetupPlayerStatsAndInventoryBasedOnDifficulty` 的普通初始武器发放使用 3507。`Player` 构造函数初始化背包物品对象，项目 `DemoPlayerSystem.Spawn` 没有执行上述建角发装备流程；`Equip` 才另行向 slot 0 发放测试剑。

项目证据：`scripts/smoke-test.ps1 -Combat` 显式执行 equip；`.local/test-results/smoke-20261021-215122.json` 中 AI slot=1 的命中事件记录 item=4 或 item=24。不能把这些结果写成“默认铜短剑已经通过”。

Wiki 查阅入口：[Copper Shortsword](https://terraria.wiki.gg/wiki/Copper_Shortsword)、[Iron Broadsword](https://terraria.wiki.gg/wiki/Iron_Broadsword)。本次全文访问返回 403，以上物品 ID 和建角流程已由本机程序集确认；不声称已经读取这些条目的全文。后续实现铜短剑时再查需要的攻击机制，不重复调查已经确认的物品身份。

## 死亡与自动重生

玩家死亡后自动重生是队友持续游玩的必要行为，本批已接入。Wiki 入口：[Death](https://terraria.wiki.gg/wiki/Death)、[Spawn](https://terraria.wiki.gg/wiki/Spawn)、[Expert Mode](https://terraria.wiki.gg/wiki/Expert_Mode)。Wiki 的重生位置说明为有效床出生点或世界出生点；专家模式说明普通计时从 10 秒增加至 15 秒。本项目使用本机原生计时，不硬编码这些秒数。

本机依据：`GetRespawnTime` 在普通单人世界给出 600 ticks；`UpdateDead` 对额外槽位也递减 respawnTimer，但实际 Spawn 分支限于本地玩家。本项目在原生 UpdateDead 后为 AI 补调重生，保留同一 Player 和背包。`Spawn(ReviveFromDeath)` 原生恢复生命、位置和出生保护，配合 FindSpawn / CheckSpawn 使用有效出生点，否则回世界出生点。当前 AI 没有设置床的入口，因此只验收世界出生点；中核／硬核和金币惩罚尚不在本批范围。remove → spawn 仍是创建新对象的开发操作，不等同于原角色复活。

## 维护方式

每次只补当前功能用到的结论和来源；遇到误解或 bug 就更正对应条目，不增加重复核对流程。玩法规则、项目实现、已经实测的结果分别写清楚。
