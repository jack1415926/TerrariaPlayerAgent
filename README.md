# TerrariaPlayerAgent

在 Terraria / tModLoader 中开发具有独立生命、物品和装备的 AI 队友，最终尝试合作推进至月亮领主。

当前版本 **0.1.1：独立玩家移动演示与本地自动测试**。C# 12 / .NET 8，独立 tModLoader 模组，无第三方伙伴模组依赖。暂未接入 LLM。

## 会话交接：当前批次已收尾

功能代码基线：`08eef98`。下一聊天从 [TODO 的双向战斗验证](TODO.md#下一批双向战斗验证) 开始，不再继续扩展本批聊天输入调查。

| 能力 | 已完成与验证 | 当前边界 |
| --- | --- | --- |
| 独立角色 | 单客户端中的独立 Player，生成、移动、跳跃、停止、移除；人工确认退出重进清理正常 | 单人、单角色，完整装备、受伤、死亡复活尚未验证 |
| 自动测试 | 本地 JSON 入口，10 项角色检查通过，执行部分约 4 秒 | 单人失焦会暂停，测试游戏须保持前台 |
| 原生聊天测试 | 11 个字符经本游戏窗口 WM_CHAR 进入 IME；原生 ChatCommandCaller 提交 `/tpa status` 恰好一次 | 适配器只支持固定只读状态命令；不是任意文字/命令输入工具 |
| 桌面输入调查 | 自动 type_text 的字符入口计数为 0，人工 abc 为 3 | Computer Use 通用按键和文字注入未修复；现有替代路径足够继续角色开发 |

下一聊天先读本节、TODO.md、docs/technical-design.md，按需要查看 docs/testing.md。docs/validation.md 是追加式历史记录，以较新的验收条目为准，不把早期“未测”描述当作当前状态。

实现入口：Common/DemoPlayerSystem.cs 管角色和控制，Commands/DemoCommand.cs 管命令，Common/LocalTestBridge.cs 管开发测试请求。当前角色依靠玩家槽位的原生循环更新，没有手动重复调用 Player.Update，也没有切换 Main.myPlayer。战斗前应针对匹配版本核对原生代码中本地玩家专属的执行路径，不能由移动成功推定伤害机制可用。

开发与测试约定：专用新角色和新世界可直接用作可破坏测试环境。允许发测试装备、生成敌人、反复死亡和重建、连续及异常操作；必要的本地测试操作自主完成，不逐项请求确认。优先复用脚本和结构化结果，按变更做必要验证，避免堆叠测试设施。窗口操作围绕 tModLoader，Mod Organizer 与本项目无关。

下个聊天可直接使用：

> 阅读本仓库 README 的会话交接和 TODO 的“下一批：双向战斗验证”。开始实现原版近战武器攻击史莱姆、AI 正常受伤及死亡的最小验证。专用测试角色和世界可破坏，可以直接生成测试装备和敌人并做压力测试。沿用已有本地测试入口；同步更新人工调试步骤、验收结果并提交本地 Git，不继续扩大聊天输入调查。

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
/tpa remove
```

一条一条执行。新动作替换旧动作；左右移动默认 2 秒，最多 10 秒模拟时间。status 可查看双方槽位、位置、生命及每次更新的调用次数。

所有本地开发数据位于 `.local/tModLoader`，被 Git 忽略。源码只在本仓库维护，不复制到 ModSources。现有 Steam 云存档仍可能显示，测试请只使用新建的本地存档。

## 自动测试

进入专用测试世界后运行 `.\scripts\smoke-test.ps1`，按提示在 3 秒内切回游戏。测试会自动执行动作并输出 JSON 报告。AI 调用时可保持游戏前台并传入 `-FocusDelaySeconds 0`，无需逐步截图。

详见 [本地测试与 Enter 诊断](docs/testing.md)。历史命令回填小模组及测试辅助模组见 [待办](TODO.md)。

## 验证与限制

- 已验证：编译打包 0 警告、0 错误，模组成功加载；用户确认角色出现、移动、跳跃、提前停止、重复移除和退出重进后的清理正常。
- 其余首批细项逐项见 [验证记录](docs/validation.md) 和 [人工测试清单](docs/manual-tests.md)。
- 单人、一个演示角色；没有装备交互、战斗、死亡复活保证、AI 存档、跟随或寻路。测试物品可直接生成，AI 数据尚不持久化。
- 多人目标为 2–3 名真人加 1 个共享 AI，留待后续；当前多人命令会拒绝执行。

## 文档

- [人工构建、演示与调试](docs/debugging.md)：启动、命令、日志和问题反馈。
- [技术选型与实现](docs/technical-design.md)：角色生命周期、原生调度、代码职责与已知限制。
- [第一批范围](docs/first-demo.md)：验收条件与后续边界。
- [完整需求](docs/requirements-v0.1.md)：FR01–FR11、MVP 和长期目标。
- [前期可行性调查](docs/p0-feasibility.md)：参考项目及固定版本证据；其中“尚未编码”等描述为调查时状态，当前状态以上文为准。

项目新增代码使用 [MIT License](LICENSE)。未包含 Terraria 游戏源码、游戏二进制或第三方伙伴模组代码/资源。

## 原生聊天链路测试

运行 `.\scripts\test-chat.ps1`，按提示切回独立测试游戏。它通过本游戏窗口内的字符适配器输入并原生提交固定的 `/tpa status`；不修改角色控制逻辑。详见 [测试文档](docs/testing.md)。Computer Use 的通用自动文字输入仍未修复。
