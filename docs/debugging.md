# 人工构建、演示与调试

2026-11-13 范围说明：下方为单人历史调试入口及旧机版本记录。当前多人短目标使用 `scripts/play.ps1 -MultiplayerAgent`，默认加载项目模组、Cheat Sheet、HERO's Mod；目标替换／停止优先运行 [自动验收入口](testing.md)，不要求真人抢时序。当前版本、多人命令和边界见 [新机说明](new-machine-setup.md) 与 [多人协议](multiplayer-probe.md)，不能把下方单人测试发物资／攻击命令用于多人产品验收。

更新：2026-10-22。当前基础闭环结论见 [阶段性验收](milestone-acceptance.md)，历史证据见validation.md；以下提供手动调试入口，未测项不视为通过。

## 1. 从哪里开始

- 项目：`F:\codex_project\TerrariaPlayerAgent`。
- 本机 tModLoader：`F:\SteamLibrary\steamapps\common\tModLoader`，本轮使用版本 2026.08.3.0 / Terraria 1.4.4.9。
- 本机 .NET SDK：8.0.426，仓库 global.json 选用 .NET 8。
- 测试配置：项目下 `.local\tModLoader`。此目录被 Git 忽略，包含本项目模组、测试角色和世界。

源码始终以 F 盘 Git 仓库为准，不再复制到游戏的 ModSources。现在使用以下脚本构建，不使用游戏菜单对另一份源码构建。

## 2. 构建和启动

打开 PowerShell 7，执行：

```powershell
Set-Location 'F:\codex_project\TerrariaPlayerAgent'
.\scripts\build.ps1
.\scripts\play.ps1
```

build.ps1 编译并打包，看到“0 个错误”和 Packaging 完成才算构建成功。产物为 `.local\tModLoader\Mods\TerrariaPlayerAgent.tmod`。

play.ps1 只把本项目写入独立配置的 enabled.json，再启动游戏。不会替换原有本地配置里的模组列表。首次启动需要选择语言，新建测试角色和普通小世界。

若换电脑或游戏安装位置，两个脚本均支持指定路径：

```powershell
.\scripts\build.ps1 -TmlPath 'D:\SteamLibrary\steamapps\common\tModLoader'
.\scripts\play.ps1 -TmlPath 'D:\SteamLibrary\steamapps\common\tModLoader'
```

Steam 云存档仍可能在列表中显示，测试时只使用新建的本地测试角色和世界，不导入或选择主力存档。退出开发配置后，从 Steam 正常启动会回到原有本地配置。

首次实测创建了本地角色 `t` 和普通小世界“放松苔原”，种子 `2118482098`，均属于项目独立配置。

源码修改需要重新编译并让运行中的游戏加载新代码，但**不代表每次都必须退出整个游戏**。当前 build.ps1 是外部编译／打包，play.ps1 是启动器，不负责给已经加载的模组热更新；前几批完整重启是当时采用的操作方式。

优先尝试较短流程：保存退出世界 → 在模组列表禁用本模组并重载，释放旧 tmod → 运行仓库 build.ps1 → 重新启用并重载模组 → 返回测试世界。若不能正常释放或重载，再关闭游戏、构建、重新启动。该较短流程有 [tML 官方开发文档](https://github.com/tModLoader/tModLoader/wiki/Developing-with-Visual-Studio-Code#common-build-issues) 支持，但本项目尚未实测走通；下一次必要构建时试一次即可，不单独反复重启做验证。

官方还支持配置调试器后的 [Hot Reload](https://github.com/tModLoader/tModLoader/wiki/Developing-with-Visual-Studio-Code#enabling-hot-reload)。本项目目前未配置，不能宣称编辑保存后就能在世界内立即生效；也不能把普通方法体更新与 IL 钩子注册、模组加载阶段变更一概视为可热更新。文档修改和外部 PowerShell 测试脚本修改无需重启游戏。不要反复启动额外窗口，也不要以旧代码的画面判断新功能。

## 3. 两分钟演示

进入测试世界，在平整地表按 Enter 打开聊天框，输入一条命令后按 Enter 执行，完成一项再做下一项：

| 命令 | 预期 |
| --- | --- |
| `/tpa spawn` | 身边生成蓝色上衣角色 Agent Demo；重复生成只提示已存在 |
| `/tpa status` | AI 与 human 槽位不同，sameObject=False；正常 updates/tick=1 |
| `/tpa right 2` | 向右移动约 2 秒模拟时间后松开输入 |
| `/tpa left 2` | 向左移动，遇到墙不会穿过 |
| `/tpa jump` | 原地跳跃后落地，不持续连跳；站在地面时执行 |
| `/tpa jump left 1` | 边走边跳，保持水平输入 |
| `/tpa range 25`、`range off` | 在当前AI的X设置左右25格范围／home，或解除；设置会取消旧任务 |
| `/tpa goto 33624`、`home` | 按水平世界像素到位／主动返回home；示例X须换成当前附近可达点 |
| `/tpa right 10`，随后 `/tpa stop` | 立即取消持续输入，允许正常惯性和下落 |
| `/tpa remove` | 移除角色；再次移除安全，再次 spawn 可重建 |

left/right不填时长默认2秒，允许大于0、最多10秒，小数用英文点。手动新动作通常替换旧动作、不排队，但普通jump保留已有水平移动；Agent宿主会逐条等待动作完成。goto／home默认最多10秒，水平8像素内、落地低速才arrived；home先需设置range。设置范围后旧左右输入也受边界约束，普通stop不清范围。无角色时先生成，死亡角色等待原生倒计时重生。

移动指令期间会自动尝试跨小坎：角色着地、前方有实心障碍、上方路径可通过时，保持方向并施加原生跳跃输入。最多检查上移1–5格的空间，持续无进展时最多两次恢复跳；高墙、低顶、复杂平台、斜坡和绕路不作完整寻路保证。没有移动意图时不自行跳跃，stop取消剩余跳跃输入；autoJumpCount与`[TPA] AutoJump`日志记录触发，不单凭计数证明已越过。

确认输入法处于英文状态。方向键/WASD和空格仍只控制真人，AI不会持续跟随。已设活动范围时可用home尝试主动返回既定起点；没有设置时人工走近查看。remove重建会丢失当前AI背包和状态，不作为常规找回办法。

停止后再次 status 应显示 moveTicks=0、jumpTicks=0。按 manual-tests.md 完成其余检查。

## 4. 日志和常见问题

### 双向战斗演示

通过 play.ps1 进入专用世界，在平整地表依次执行：

```text
/tpa spawn
/tpa equip iron
/tpa attack right 10
/tpa enemy slime
/tpa status
/tpa stop
```

攻击命令发出后，在 10 秒内生成敌人。AI 原地朝指定方向挥剑，不会自主追敌；怪物被击退或跑远后，需要调整位置或重新生成场景。新动作替换旧动作。stop 取消后续挥剑输入，已经开始的原生挥剑会完成，不会立刻冻结动画。

`equip wood` 换木剑；`enemy zombie` / `enemy eye` 分别生成僵尸、恶魔眼。测试生成的怪物使用原生生命、伤害和 AI，也可能攻击真人。装备和生成敌人命令仅在 `-tpatest` 开发启动下可用；不扣测试资源。`clear-enemies` 清理本入口生成的敌人；remove 也会清理它们，不清理自然刷新的怪物。

观察双方伤害：先让 AI 挥剑命中，再 stop，等史莱姆返回接触 AI。status 的 hits、hurts 应增加，AI life 下降；具体 NPC 生命、武器和双方槽位见 `[TPA] Combat` 日志。自然刷怪、向导攻击和地形可能干扰，以同一 NPC 槽位的命中和受伤事件为准。要演示死亡，可保持 AI 不攻击，重复 `enemy zombie`，让原生接触伤害消耗生命。

**AI 死后会自动重生，无需输入命令。** 当前普通世界通常等待 600 个模拟更新（约 10 秒，暂停不计时），回到世界出生点，保留已有武器；死前动作不会继续。死亡等待期间，移动、跳跃和攻击命令提示等待自动重生；status 可见 respawnTicks 倒计时和 respawns 累计次数。重生不等于重新 spawn，死亡和命中计数不会清零。remove 会取消角色及其待重生状态。

自动重生默认开启，不依赖 -tpatest，也没有需要手动打开的开关。脚本末尾的 remove 只是移除测试角色；如果没有角色，先 spawn 一次即可，之后死亡无需手动重建。

单独检查本批闭环运行 `.\scripts\smoke-test.ps1 -Respawn`。它会让 AI 自然受伤死亡、等自动重生、检查武器保留和恢复行动，再检查死亡等待中移除不会自行重生；结束时清理角色和测试敌人。无需重跑整套战斗检查。AI 当前没有设置床的操作入口，所以未验收床重生。

批量执行移动与战斗测试：

```powershell
.\scripts\smoke-test.ps1 -Combat
```

脚本仍预留 3 秒切回游戏，结束会移除 AI 和测试生成的敌人。报告在 `.local/test-results/smoke-日期时间.json`；不要同时手动发动作命令。

本机日志目录：`F:\SteamLibrary\steamapps\common\tModLoader\tModLoader-Logs`。运行参数隔离了存档配置，但日志仍在这个安装目录；独立配置中的 Logs.lnk 也可用于定位。

优先查看 client.log，搜索 `[TPA]`、`TerrariaPlayerAgent`、`Error`、`Exception`。生成和移除会记录槽位及累计更新数。每次查询 status 的完整结果也显示在游戏聊天里，可截图保留。

```powershell
Get-Content -LiteralPath 'F:\SteamLibrary\steamapps\common\tModLoader\tModLoader-Logs\client.log' -Tail 100
```

启动失败时另看 Launch.log 和 environment-client.log。它们可能被下一次启动覆盖，复现后及时在仓库外复制保留。

| 现象 | 首先检查 |
| --- | --- |
| dotnet 命令不存在或 SDK 版本不匹配 | PowerShell 执行 `dotnet --list-sdks`，确认已装 .NET 8 SDK；只有运行时不能编译 |
| 找不到 tModLoader | 给脚本传入正确 -TmlPath，不把 Terraria 原版目录当 tML 目录 |
| 游戏提示未知命令 | 是否用 play.ps1 启动，是否加载 Terraria Player Agent - Demo |
| 角色不出现 | 记录 spawn 的回复；移到开阔地重试；status 检查对象是否存在 |
| 角色不移动 | 检查剩余 ticks、是否撞墙或已死亡；连续两次 status 比较位置与 total |
| AI 死后不重生 | status 查看 respawnTicks 是否递减；保持游戏前台。归零仍 dead=True 时，保留状态和日志作为 bug 反馈 |
| 有挥剑但没打中 | 看 hits 和日志中 item-hit 的 NPC 槽位及 targetLifeAfter；检查朝向、距离、障碍，不以动画认定命中 |
| updates/tick 不是 1 | 保留状态与日志，说明更新接入有问题，不增加额外手动 Update 来掩盖 |
| 无异常但动作错误 | 仍按失败反馈，日志安静不是通过依据 |

## 5. 怎么反馈一个可定位的问题

```text
代码版本：git rev-parse --short HEAD
本地改动：git status --short（没有输出填“无”）
tModLoader 版本、启用模组：
测试编号：D01–D07
执行了什么命令、间隔多久：
预期：
实际：
复现次数：例如 3 次中 2 次
/tpa status：完整回复或截图
client.log：复现后的相关片段，保留完整错误栈
```

一次只改一个条件。卡住时先 stop、remove，再退出游戏；不删除存档或使用 git reset --hard 来尝试修复。

## 6. 可选进阶：断点

先掌握上述操作即可。阅读 technical-design.md 的代码职责后，可以在 Spawn、PreUpdatePlayers、Remove 设置断点，用支持 C# 的 IDE 附加到测试用 dotnet 游戏进程。当前没有交付经过实测的 IDE 调试配置，不把它写成已验证操作。更新函数断点会频繁暂停游戏，定位后及时取消。

## 0.1.1：批量测试入口

现在可以使用 scripts/smoke-test.ps1 自动检查角色控制；具体操作与原生 Enter 对照见 testing.md。脚本预留 3 秒切回游戏。测试游戏须保持前台；单人失焦会原生暂停，不能把后台超时当作角色失败。普通角色测试不再依赖聊天框或 Computer Use 按键。
