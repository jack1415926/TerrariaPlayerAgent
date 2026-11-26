# TerrariaPlayerAgent

在 Terraria／tModLoader 中加入一个由 AI 操作的独立玩家队友。目标是让一名真人与一个 AI 合作游玩，AI 听懂短目标，并沿用 Terraria 的移动、伤害、装备与资源规则。

当前 Demo 在 Host & Play 实测了真实 DeepSeek 一次靠近固定傀儡、用默认铜短剑命中并停手。服务端记录原生命中，聊天显示结果，每个目标生成一份本地报告。

## 运行 Demo

需要 Terraria、tModLoader、PowerShell 7、.NET 8 SDK 和 Python 3.12。实测组合为 Terraria 1.4.4.9／tModLoader 2026.9.3.1。游戏路径、独立测试存档和 DeepSeek 配置见 [新机准备](docs/new-machine-setup.md)。

在项目目录打包并启动游戏。脚本默认加载项目模组、Cheat Sheet 和 HERO's Mod。

```powershell
.\scripts\build.ps1
.\scripts\play.ps1 -MultiplayerAgent
```

在另一个 PowerShell 7 窗口启动常驻宿主。

```powershell
.\scripts\run-agent.ps1 -HostMode -AgentMode multiplayer_server -MaxTurns 6
```

通过「开服并游玩」进入独立测试世界，准备一个同层的固定傀儡，让它与 AI 隔开几格。真人靠近可使傀儡激活。在游戏聊天输入以下两条命令。

```text
/tpamp spawn
/tpamp goal 靠近傀儡，用手上的铜短剑打中一次，然后停下告诉我结果
```

下一步由 DeepSeek 选择。需取消当前任务时输入 `/tpamp stop`。详细报告在 `.local/test-results/goal-<目标ID>.json`。常驻宿主完成目标后回到待机，单次验收才加 `-MaxGoals 1`。

## 当前范围

- 多人已验收经典起始装备、固定傀儡的自主铜短剑任务、手动铜短剑／铁阔剑、自然接触受伤／软核重生及一次掉落拾取。
- Demo 的 Agent 攻击限定为使用已持有的铜短剑打固定傀儡。铁阔剑仍是手动测试夹具，背包换槽、活动敌怪任务、跟随和持久化在后续范围。
- Buff 到期清理已通过本机运行时检查，Host & Play 场景待复测。远程独立服务器和第二真人也有独立待验项。

完整证据与范围见 [集中实测清单](docs/acceptance-checklist.md)。测试操作与离线检查见 [测试入口](docs/testing.md)。

## 代码与文档

| 路径 | 职责 |
| --- | --- |
| `Common/`、`Commands/` | 服务器 AI、原生战斗与交互、镜像、手动命令与目标桥 |
| `agent/run.py`、`agent/prompts/` | Python 宿主、模型工具与提示词 |
| `scripts/`、`tools/` | 构建、启动、验收、离线自检 |
| [Agent 运行说明](docs/agent-setup.md) · [多人命令与协议](docs/multiplayer-probe.md) | 操作、模式、邮箱和反馈状态 |
| [技术设计](docs/technical-design.md) · [游戏机制](docs/game-mechanics.md) | C#／Python 职责与原生规则 |
| [TODO](TODO.md) · [验证记录](docs/validation.md) · [开发经验](docs/development-experience.md) | 后续工作、可追溯证据与复盘 |

本项目新增代码采用 [MIT License](LICENSE)。游戏文件、API 凭据、本地存档、报告及打包产物保留在本地。
