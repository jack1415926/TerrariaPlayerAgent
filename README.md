# TerrariaPlayerAgent

在 Terraria / tModLoader 中开发由 AI 操控的独立玩家队友，长期目标是合作探索并推进游戏。第一版面向“一名真人＋一个 AI”，听令执行短任务、及时反馈，**必须包含战斗，装备和资源遵循正常玩家成长规则**。

当前包版本 **0.1.1**，C# 12 / .NET 8。当前虚拟 Player 还不具备全部真实玩家语义，产品目标不等于已完成能力。公开仓库不包含游戏、API 凭据、构建产物或存档；本地原始报告 `.local/` 被 Git 忽略。

## 当前状态（2026-11-27）

| 范围 | 已有证据 | 边界 |
| --- | --- | --- |
| 多人基本角色 | 一名真人场景生成、持续更新、移动／遇坎跳跃、停止、红队显示、移除重建、关服重进清理通过 | AI 不是独立网络客户端；第二真人、存活服务器断线清理和真实占槽冲突未实测 |
| 显示同步 | 用户确认当前移动平滑、停止及时 | 断流／瞬移恢复和第二客户端画面未实测，无性能测量 |
| 多人受伤与重生 | 自然怪物接触受伤、死亡、软核重生后再次受伤有日志；用户复测选敌看起来正常 | 复用原生选敌，没有强制仇恨；未定位此前选敌现象的独立根因，不宣称完整战斗通过 |
| 多人短目标 | 中文聊天下单、接单／移动／结束反馈、真实模型左右短移动通过 | 当前移动每次最多3秒；Python 攻击和多人挖掘未交付 |
| 多人初始装备与显示 | Host & Play 生成铜短剑、铜镐、铜斧各一件，默认选中铜短剑，玩家确认刺击剑身可见 | 客户端只镜像选中物品；真人新角色逐项对照和目标替换后库存待验 |
| 多人手动攻击 | 铜短剑对傀儡的原生刺击与剑身显示通过；铁阔剑击杀史莱姆，并在同一傀儡上重复命中 | 攻击最长1秒；铁阔剑通过显式测试夹具取得；Python 攻击和主动追敌未开放 |
| 多人拾取与 Buff | 服务器快照确认拾取后的墓碑入包；本机原生检查确认 Buff 递减并到期清理 | Buff 到期待 Host & Play 实测；采掘→拾取、一般物品换装和保存待开发 |
| 目标生命周期 | 只读桥、移除／重建／世界身份变化、真实 A→B 替换与移动中停止通过 | 完整恢复、真实重连、长时间稳定性仍待验 |
| 单人历史基线 | 原生木剑／铁阔剑攻击、铜镐挖掘、goto／home 有阶段性实测 | 不能直接当作多人能力；单人拾取入包、合成和保存未交付 |

最近一轮在 tML 2026.9.3.1／Terraria 1.4.4.9 的 Host & Play 中由一名真人与一个 AI 验收。仓库 71 项 Python 检查和 207 项 C# 自检有通过记录，最新版构建 0 警告、0 错误。本机程序集动态验证与游戏内验收的范围见 [集中清单](docs/acceptance-checklist.md) 和 [验证记录](docs/validation.md)。

## 项目结构

```text
TerrariaPlayerAgent/
├─ TerrariaPlayerAgent.csproj / .cs   模组工程与 Mod 入口
├─ build.txt / description.txt        tModLoader 打包与展示信息
├─ Commands/                          /tpa 单人命令、/tpamp 多人命令
├─ Common/                            C# 核心：AI 生命周期、移动/挖掘/战斗、
│                                     多人桥接、显示同步、初始装备与探针
├─ Localization/                      英文文本
├─ agent/                             Python 手写宿主：模型循环、提示词、测试
├─ scripts/                           build / play / run-agent / smoke / 验收脚本
├─ tools/                             离线自检：DisplaySync、InventorySync、Combat
├─ docs/                              设计、运行、协议、验收与历史记录
├─ .env.example                       API 环境变量模板（不含凭据）
├─ TODO.md                            当前计划与下一步边界
└─ README.md
```

主要 C# 文件：`Common/DemoPlayerSystem.cs` 负责 AI 槽位、生命周期与状态快照；`Common/MultiplayerProbe.cs` 负责真人控制归属、命令包与客户端镜像；`Common/MultiplayerAgentBridge.cs` 负责服务器目标邮箱与身份校验；`Common/DisplaySync.cs` 负责显示平滑；`Common/StartingKit.cs` 与 `Common/StartingInventory.cs` 负责多人服务器初始装备条件与发放；`Common/NativeCombat.cs`、`Common/MultiplayerAttackPolicy.cs`、`Common/CombatHitProbe.cs` 与 `Common/MultiplayerTestEquipPolicy.cs` 负责受限多人短攻击的原生适配、身份校验、命中记录和隔离测试物资门控；`Common/NativePlayerInteractions.cs` 补齐服务器 AI 的原生 Buff 计时与掉落拾取。Python 入口是 `agent/run.py`，离线测试是 `agent/test_run.py`；`scripts/play.ps1 -MultiplayerAgent` 启动多人开发环境，`scripts/run-agent.ps1 -HostMode -AgentMode multiplayer_server` 启动宿主。

`.local/` 是本地测试配置、构建产物、报告与临时文件，已被 Git 忽略，不属于仓库结构。

## 开始体验多人短目标

先按 [新电脑接续](docs/new-machine-setup.md) 准备游戏、.NET 和自己的 `.env`；API 配置见 [Agent说明](docs/agent-setup.md)。PowerShell 7：

```powershell
.\scripts\build.ps1
.\scripts\play.ps1 -MultiplayerAgent
.\scripts\run-agent.ps1 -HostMode -AgentMode multiplayer_server -MaxTurns 3 -MaxGoals 4
```

脚本默认加载项目模组、Cheat Sheet 和 HERO's Mod，后两者须自行安装／订阅。沿用该入口，不为测试临时改成只加载项目模组。辅助设置按实际记录，不能把辅助环境算作正常战斗／成长验收。

通过“开服并游玩”进入专用测试世界，再输入：

```text
/tpamp spawn
/tpamp goal 向右走一秒，然后停止，完成后告诉我
/tpamp attack left 1
/tpamp stop
```

`spawn` 通过原生物品初始化入口给多人 AI 发放普通经典三件。生成者可用 `/tpamp attack left/right [1]` 手动触发铜短剑刺击或铁阔剑挥砍，最多1秒并自动结束。铁阔剑测试夹具仅在 `play.ps1 -MultiplayerAgent -MultiplayerTestEquip` 启动时启用。常规攻击回归用固定傀儡，另行测试中途取消时才需要 `stop`。`-MaxGoals 4` 指 Python 宿主处理4个短目标后退出，示例中每目标最多3轮；待机不请求模型。

本轮在 .NET SDK 8.0.428、tML 2026.9.3.1／Terraria 1.4.4.9 上实测。使用独立配置 `.local/tModLoader` 下的测试角色与世界；新机用 `-TmlPath` 指定本机安装位置。

## 自动验收与只读检查

AI 已生成、无活动目标、多人宿主至少还可接2个目标时运行：

```powershell
.\scripts\test-multiplayer-goals.ps1
```

脚本按真实接单／输入事件自动执行替换和停止，无需真人抢输入时机；输出 `.local/test-results/acceptance-<id>.json`。失败先查报告，不盲目重放。单独读取服务器状态可用 `run-agent.ps1 -Probe -AgentMode multiplayer_server`；只希望读状态时用 `play.ps1 -MultiplayerBridge`，不开放动作。

## 架构与下一步

当前仍是手写 Agent loop：Python 请求模型并协调任务，C# 服务器执行受控输入，Terraria 处理原生物理／伤害，客户端镜像只负责显示。单宿主、单在途模型请求和每目标身份校验保护替换／停止。迟到结果不执行，但已发 API 请求不保证取消或免计费。

**当前阶段**已验证多人原生攻击与单一掉落拾取。下一步补限时 Buff 到期实机检查，再按原生玩家规则验证拾取后的快捷栏选择与正常装备成长。范围见 [TODO](TODO.md)。

## 文档入口

- [TODO](TODO.md)：当前待办与集中实机验收，已完成过程不重复维护。
- [集中实测清单](docs/acceptance-checklist.md)、[多人运行与协议](docs/multiplayer-probe.md)：当前证据、操作与边界。
- [技术设计](docs/technical-design.md)、[Agent运行](docs/agent-setup.md)、[测试入口](docs/testing.md)、[调试](docs/debugging.md)。
- [开发反思](docs/development-experience.md)：本轮绕路、原因与下次做法。
- [验证记录](docs/validation.md)：追加保留成功、失败与修复，较新结论优先。
- [单人阶段报告](docs/milestone-acceptance.md)、[原始需求](docs/requirements-v0.1.md)、[前期可行性](docs/p0-feasibility.md)：历史范围，不能替代当前状态。

本项目新增代码使用 [MIT License](LICENSE)。不提交游戏源码、游戏二进制、凭据或本地存档。
