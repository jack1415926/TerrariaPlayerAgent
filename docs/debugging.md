# 人工构建、演示与调试

更新：2026-10-21。编译、打包、游戏加载已验证；角色动作的游戏内结果见 validation.md，未测项不视为通过。

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

修改代码后，最简单可靠的流程：在测试游戏中保存退出，关闭该游戏窗口 → 保存源码 → 重新运行 build.ps1 → play.ps1。不要在旧游戏仍运行时反复启动新窗口，也不要以旧进程画面判断新代码是否生效。

## 3. 两分钟演示

进入测试世界，在平整地表按 Enter 打开聊天框，输入一条命令后按 Enter 执行，完成一项再做下一项：

| 命令 | 预期 |
| --- | --- |
| `/tpa spawn` | 身边生成蓝色上衣角色 Agent Demo；重复生成只提示已存在 |
| `/tpa status` | AI 与 human 槽位不同，sameObject=False；正常 updates/tick=1 |
| `/tpa right 2` | 向右移动约 2 秒模拟时间后松开输入 |
| `/tpa left 2` | 向左移动，遇到墙不会穿过 |
| `/tpa jump` | 原地跳跃后落地，不持续连跳；站在地面时执行 |
| `/tpa right 10`，随后 `/tpa stop` | 立即取消持续输入，允许正常惯性和下落 |
| `/tpa remove` | 移除角色；再次移除安全，再次 spawn 可重建 |

left/right 不填时长默认 2 秒；允许大于 0、最多 10 秒，小数使用英文点。新动作替换旧动作，不排队。跳跃会替换移动。无角色时提示先生成；死亡角色本批不负责复活，需要 remove 后重新 spawn。

确认输入法处于英文状态。普通方向键/WASD和空格仍然只控制你自己的角色。AI 不会跟随你，离开镜头后需要回去查看，或 remove 后重新 spawn。

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

**AI 死后不会自动重生。** 当前只完成死亡流程；执行 `/tpa remove` → `/tpa spawn` 重建，需重新 equip。死后移动、跳跃和攻击命令会提示没有存活角色。

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
| AI 死后不重生 | 当前预期行为，remove 后 spawn 重建；尚未实现自动复活 |
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
