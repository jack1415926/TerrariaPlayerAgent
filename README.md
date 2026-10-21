# TerrariaPlayerAgent

在 Terraria / tModLoader 中开发具有独立生命、物品和装备的 AI 队友，最终尝试合作推进至月亮领主。

当前版本 **0.1.1：独立玩家移动、本地测试与双向战斗验证**。C# 12 / .NET 8，独立 tModLoader 模组，无第三方伙伴模组依赖。已通过外部 Python Agent 接入 DeepSeek API，首轮自主武器测试部分完成。

## 会话交接：自动重生与移动跨坎

战斗代码基线为 `aecef3f`。本批为普通／软核 AI 增加原生倒计时后的自动重生，保留原对象和已有武器；下一批接入普通新角色初始装备与铜短剑攻击，具体见 TODO。

后续修复：left/right 移动时，着地、前方有低矮实心障碍且头顶空间足够，会自动触发原生跳跃；自然地形实测已通过。status 的 autoJumps 和日志 `[TPA] AutoJump` 可查看触发情况。这是局部跨坎，不是寻路。

API Agent 首轮：`scripts/run-agent.ps1` 已复用现有 JSON 入口控制运行中的 AI，不需要重启。DeepSeek 自行发出 6 批动作，木剑对史莱姆命中已确认，铁阔剑没有命中证据；模型曾误报后者成功，已记录并补充任务结果判定。下次优先处理该具体问题，见 [API Agent 说明](docs/agent-setup.md)。

| 能力 | 已完成与验证 | 当前边界 |
| --- | --- | --- |
| 独立角色 | 单客户端中的独立 Player，生成、移动、跳跃、停止、移除；人工确认退出重进清理正常 | 单人、单角色，无持久化 |
| 双向战斗 | 铁阔剑命中史莱姆、僵尸，木剑命中恶魔眼；同一史莱姆与 AI 互伤；NPC 接触伤害可致死 | 只适配两把剑；没有自主追敌或完整装备兼容 |
| 自动重生 | 普通／软核 AI 死亡后按原生倒计时重生，保留装备、清除旧动作 | 当前 AI 没有设置床的入口，默认回世界出生点；不扩展中核／硬核角色 |
| 自动测试 | 沿用本地 JSON 入口，`smoke-test.ps1 -Combat` 的 25 项检查通过 | 单人失焦会暂停，测试游戏须保持前台 |
| 原生聊天测试 | 11 个字符经本游戏窗口 WM_CHAR 进入 IME；原生 ChatCommandCaller 提交 `/tpa status` 恰好一次 | 适配器只支持固定只读状态命令；不是任意文字/命令输入工具 |
| 桌面输入调查 | 自动 type_text 的字符入口计数为 0，人工 abc 为 3 | Computer Use 通用按键和文字注入未修复；现有替代路径足够继续角色开发 |

下一聊天先读本节、TODO.md、docs/technical-design.md，按需要查看 docs/testing.md。docs/validation.md 是追加式历史记录，以较新的验收条目为准，不把早期“未测”描述当作当前状态。

实现入口：Common/DemoPlayerSystem.cs 管角色、控制、NPC 接触伤害及战斗日志，Common/NativeCombat.cs 适配三个原生战斗归属检查，Commands/DemoCommand.cs 管命令，Common/LocalTestBridge.cs 管开发测试请求。角色依靠玩家槽位的原生循环更新，没有重复调用 Player.Update，也没有切换 Main.myPlayer。适配针对本机 tML 2026.08.3.0，升级游戏后需要重新检查这些分支。

开发与测试约定：专用新角色和新世界可直接用作可破坏测试环境。允许发测试装备、生成敌人、反复死亡和重建、连续及异常操作；必要的本地测试操作自主完成，不逐项请求确认。优先复用脚本和结构化结果，按变更做必要验证，避免堆叠测试设施。窗口操作围绕 tModLoader，Mod Organizer 与本项目无关。

战斗批次的移动与战斗检查为 25/25 通过。本批用 `smoke-test.ps1 -Respawn` 单独检查自动重生闭环，不重复运行未受影响的整套检查；结果见 [验证记录](docs/validation.md)。AI 死后自动等待重生，无需 remove → spawn；remove 会取消这个角色，之后不会自动出现。

**自动重生默认开启，没有被测试开关关闭。** 测试脚本结束会移除它创建的角色，这是清理测试对象，不是关闭功能。手动 spawn 出来的 AI 死后会自动重生；明确 remove 的角色则不会回来。

装备说明：原版普通新角色初始武器是**铜短剑**；本项目当前直接创建 Player，没有接入原版建角发装备流程。上批铁阔剑和木剑是测试脚本通过 equip 额外发放的装备，不能据此宣称铜短剑或初始装备已支持。

## 开发节奏与资料来源

- 以实现可用功能为主，减少不必要的校验、重复验证和防御性设施。只做与本次改动直接相关的必要检查；文档修改不启动游戏或重跑战斗测试，已通过且未受改动影响的项目不反复重测。
- 出现具体 bug 时，简要记录现象和复现步骤，定位并修复，再确认该问题已解决。不为假设中的问题提前消耗超过修复成本的时间和 token；沿用已有测试入口，不把小任务扩成测试基础设施项目。
- 玩法资料优先参考 [Official Terraria Wiki](https://terraria.wiki.gg/)，机制实现按本机 Terraria 1.4.4.9 / tML 2026.08.3.0 对照。仅在当前功能需要时查资料，把必要结论、版本和来源增量记录到 [游戏机制笔记](docs/game-mechanics.md)，不引入数据库或全站抓取。

## 快速开始

本机 PowerShell 7：

```powershell
Set-Location 'F:\codex_project\TerrariaPlayerAgent'
.\scripts\build.ps1
.\scripts\play.ps1
```

需要 .NET 8 SDK（本机 8.0.426）和 tModLoader 2026.08.3.0 / Terraria 1.4.4.9。脚本默认本机游戏目录；其他位置传入 `-TmlPath '你的 tModLoader 安装目录'`。

在独立配置中新建本地角色和普通测试世界，按 Enter 输入：

```text
/tpa spawn
/tpa right 2
/tpa left 2
/tpa jump
/tpa stop
/tpa status
/tpa equip iron
/tpa attack right 10
/tpa enemy slime
/tpa stop
/tpa remove
```

一条一条执行。新动作替换旧动作；左右移动默认 2 秒，最多 10 秒模拟时间。status 可查看双方槽位、位置、生命及每次更新的调用次数。

所有本地开发数据位于 `.local/tModLoader`，被 Git 忽略。源码只在本仓库维护，不复制到 ModSources。现有 Steam 云存档仍可能显示，测试请只使用新建的本地存档。

## 自动测试

进入专用测试世界后运行 `.\scripts\smoke-test.ps1`；加 `-Combat` 同时执行双向战斗检查，包括生成敌人和让 AI 死亡。按提示在 3 秒内切回游戏，测试会输出 JSON 报告。AI 调用时可保持游戏前台并传入 `-FocusDelaySeconds 0`，无需逐步截图。

本批自动重生测试使用 `.\scripts\smoke-test.ps1 -Respawn`，只运行重生相关场景。

详见 [本地测试与 Enter 诊断](docs/testing.md)。历史命令回填小模组及测试辅助模组见 [待办](TODO.md)。

## 验证与限制

- 已验证：编译打包 0 警告、0 错误，模组成功加载；用户确认角色出现、移动、跳跃、提前停止、重复移除和退出重进后的清理正常。
- 其余首批细项逐项见 [验证记录](docs/validation.md) 和 [人工测试清单](docs/manual-tests.md)。
- 单人、一个演示角色；支持木剑和铁阔剑近战、NPC 接触伤害及普通／软核角色自动重生。没有完整装备交互、AI 存档、跟随或寻路；投射物、陷阱和环境伤害尚未逐项验证。测试物品可直接生成，AI 数据不持久化。
- 多人目标为 2–3 名真人加 1 个共享 AI，留待后续；当前多人命令会拒绝执行。

## 文档

- [API Agent 接入与运行](docs/agent-setup.md)：提示词、模型配置、启动命令及首轮测试边界。
- [游戏机制笔记](docs/game-mechanics.md)：Wiki 入口、初始装备与测试装备、当前待补机制。
- [人工构建、演示与调试](docs/debugging.md)：启动、命令、日志和问题反馈。
- [技术选型与实现](docs/technical-design.md)：角色生命周期、原生调度、代码职责与已知限制。
- [第一批范围](docs/first-demo.md)：验收条件与后续边界。
- [完整需求](docs/requirements-v0.1.md)：FR01–FR11、MVP 和长期目标。
- [前期可行性调查](docs/p0-feasibility.md)：参考项目及固定版本证据；其中“尚未编码”等描述为调查时状态，当前状态以上文为准。

项目新增代码使用 [MIT License](LICENSE)。未包含 Terraria 游戏源码、游戏二进制或第三方伙伴模组代码/资源。

## 原生聊天链路测试

运行 `.\scripts\test-chat.ps1`，按提示切回独立测试游戏。它通过本游戏窗口内的字符适配器输入并原生提交固定的 `/tpa status`；不修改角色控制逻辑。详见 [测试文档](docs/testing.md)。Computer Use 的通用自动文字输入仍未修复。
