# TerrariaPlayerAgent

在 Terraria / tModLoader 中开发由 AI 操控的独立玩家队友，长期目标是合作探索并推进游戏。第一版面向“一名真人＋一个 AI”，听令执行短任务、及时反馈，**必须包含战斗，装备和资源遵循正常玩家成长规则**。

当前包版本 **0.1.1**，C# 12 / .NET 8。当前虚拟 Player 还不具备全部真实玩家语义，产品目标不等于已完成能力。公开仓库不包含游戏、API 凭据、构建产物或存档；本地原始报告 `.local/` 被 Git 忽略。

## 当前状态（2026-11-13）

| 范围 | 已有证据 | 边界 |
| --- | --- | --- |
| 多人基本角色 | 一名真人场景生成、持续更新、移动／遇坎跳跃、停止、红队显示、移除重建、关服重进清理通过 | AI 不是独立网络客户端；第二真人、存活服务器断线清理和真实占槽冲突未实测 |
| 显示同步 | 用户确认当前移动平滑、停止及时 | 断流／瞬移恢复和第二客户端画面未实测，无性能测量 |
| 多人受伤与重生 | 自然怪物接触受伤、死亡、软核重生后再次受伤有日志；用户复测选敌看起来正常 | 复用原生选敌，没有强制仇恨；未定位此前选敌现象的独立根因，不宣称完整战斗通过 |
| 多人短目标 | 中文聊天下单、接单／移动／结束反馈、真实模型左右短移动通过 | 当前仅左右移动和停止，每次移动最多3秒；主动攻击、装备来源、挖掘未交付 |
| 目标生命周期 | 只读桥、移除／重建／世界身份变化、真实 A→B 替换与移动中停止通过 | 完整恢复、真实重连、长时间稳定性仍待验 |
| 单人历史基线 | 原生木剑／铁阔剑攻击、铜镐挖掘、goto／home 有阶段性实测 | 不能直接当作多人能力；拾取入包、合成和保存未交付 |

最近一轮：71项 Python 检查通过、43项本机临时 DLL 检查通过、构建0警告0错误；真实宿主完成2个短目标并中断2个自动验收目标。自动停止后连续60tick输入未恢复。43项 DLL 检查不是仓库内可直接复跑的 CI 套件，范围与报告见 [集中清单](docs/acceptance-checklist.md) 和 [验证记录](docs/validation.md)。

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
/tpamp stop
```

`spawn` 只是创建当前测试 AI，不发测试武器。目标由生成者控制，新目标替换旧目标；停止取消主动输入，允许原生惯性和重力。`-MaxGoals 4` 指宿主处理4个目标后退出，示例中每目标最多3轮；待机不请求模型。当前仍需外部 Python 宿主，不支持多人攻击／挖掘／跟随。

本机11月13日验收环境为 .NET SDK 8.0.428、tML 2026.9.3.1／Terraria 1.4.4.9。新机用 `-TmlPath` 指定自己的安装位置，不照搬本机路径。只使用独立配置 `.local/tModLoader` 下的测试角色 `t`／世界“放松苔原”，不操作正式／云存档；新机无需同名存档。

## 自动验收与只读检查

AI 已生成、无活动目标、多人宿主至少还可接2个目标时运行：

```powershell
.\scripts\test-multiplayer-goals.ps1
```

脚本按真实接单／输入事件自动执行替换和停止，无需真人抢输入时机；输出 `.local/test-results/acceptance-<id>.json`。失败先查报告，不盲目重放。单独读取服务器状态可用 `run-agent.ps1 -Probe -AgentMode multiplayer_server`；只希望读状态时用 `play.ps1 -MultiplayerBridge`，不开放动作。

## 架构与下一步

当前仍是手写 Agent loop：Python 请求模型并协调任务，C# 服务器执行受控输入，Terraria 处理原生物理／伤害，客户端镜像只负责显示。单宿主、单在途模型请求和每目标身份校验保护替换／停止。迟到结果不执行，但已发 API 请求不保证取消或免计费。

**下一小批：正常新角色初始装备与可观察同步。** 先核对并复用原生起步流程，保证不在任务切换／死亡重生时重复发物品；然后再接多人原生攻击。完整交付范围和验收条件统一见 [TODO](TODO.md)。暂不迁移 LangGraph、不扩第二真人或持久化。

## 文档入口

- [TODO](TODO.md)：当前待办与下一批验收，已完成过程不重复维护。
- [集中实测清单](docs/acceptance-checklist.md)、[多人运行与协议](docs/multiplayer-probe.md)：当前证据、操作与边界。
- [技术设计](docs/technical-design.md)、[Agent运行](docs/agent-setup.md)、[测试入口](docs/testing.md)、[调试](docs/debugging.md)。
- [开发反思](docs/development-experience.md)：本轮绕路、原因与下次做法。
- [验证记录](docs/validation.md)：追加保留成功、失败与修复，较新结论优先。
- [单人阶段报告](docs/milestone-acceptance.md)、[原始需求](docs/requirements-v0.1.md)、[前期可行性](docs/p0-feasibility.md)：历史范围，不能替代当前状态。

本项目新增代码使用 [MIT License](LICENSE)。不提交游戏源码、游戏二进制、凭据或本地存档。
