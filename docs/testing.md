# 本地自动测试与 Enter 诊断

包版本：0.1.1；文档更新：2026-10-22。当前目标移动／采掘闭环及前台限制见 [阶段性验收](milestone-acceptance.md)。下方保留各批工具与历史场景，不要求每次全量重跑；本次阶段总结未新增测试。

## 直接运行测试

继续使用现有独立测试角色和普通小世界。只有C#程序集修改才需要构建并加载新版；tmod占用时按debugging.md处理重载或关闭旧实例。文档、提示词和外部Agent修改复用运行中的游戏。需要构建／启动时使用：

```powershell
Set-Location 'F:\codex_project\TerrariaPlayerAgent'
.\scripts\build.ps1
.\scripts\play.ps1
```

进入测试世界后，不需要在聊天框输入测试命令。可以在 PowerShell 或由 AI 直接运行：

```powershell
.\scripts\smoke-test.ps1
.\scripts\invoke-test.ps1 -Command status | ConvertTo-Json -Depth 8
.\scripts\invoke-test.ps1 -Command '/tpa spawn'
.\scripts\invoke-test.ps1 -Command '/tpa right 2'
.\scripts\invoke-test.ps1 -Command '/tpa stop'
.\scripts\invoke-test.ps1 -Command '/tpa remove'
```

smoke-test.ps1 提示后有 3 秒切回游戏，再移除旧演示角色，生成新角色，依次检查独立槽位、重复生成、更新次数、移动超时、停止、跳跃、重复移除。失败会抛出异常；结束时清理测试角色。请勿在运行脚本时同时手动给 AI 发指令。AI 从后台工具调用且游戏已经有焦点时可用 `-FocusDelaySeconds 0`。

报告位于 `.local/test-results/smoke-日期时间.json`，记录每项布尔结果和位置、输入、更新计数等证据。失败可能表示角色控制错误，也可能是起点地形不适合测试，例如被墙挡住或站在空中；查看报告中的前后位置和速度，不将所有失败都归为同一个问题。

基础移动测试只使用已有平整地表，不修改世界方块、不生成测试物资。战斗选项会直接发测试武器和生成敌人。截图仍用于检查可见性和动画，人工仍用于体验验收。

## 双向战斗选项（2026-10-21）

运行 `.\scripts\smoke-test.ps1 -Combat`，沿用相同 JSON 协议和报告目录。先执行原有 10 项移动检查，再以铁阔剑攻击史莱姆、僵尸，以木剑攻击恶魔眼；检查原生命中事件的攻击者、武器、目标槽位及目标生命变化。随后让原生僵尸接触伤害消耗 AI 的 100 生命，检查死亡、死后拒绝动作及交错攻击／停止／移除后的重建。共 25 项通过，报告见 validation.md；不增加独立测试服务。

新增白名单命令为 `/tpa equip wood/iron`、`/tpa enemy slime/zombie/eye`、`/tpa clear-enemies`、`/tpa attack left/right [seconds]`。发物品、生成及清理测试敌人只在 -tpatest 下开放；攻击默认 2 秒，最多 10 秒模拟时间。

快照 actor 增加 item、itemAnimation、attackTicks、usingItem、hitCount、hurtCount、deathCount、enemies 和 combatEvents。每次 spawn 重置计数，最多保留最近 64 条事件；日志同时记录 `[TPA] Combat`。item-hit 中 targetLifeAfter 是命中钩子中的 NPC 生命；hurt 中 lifeBefore 是原生扣血前生命，致死结果由 death 和最终快照确认。自然怪物也会出现在事件中，按 NPC 槽位筛选，不能拿其他怪物的命中当作测试目标通过。

测试不直接调用 Hurt、KillMe 或扣血，不修改 NPC 属性、伤害倍率、刷怪、时间或暂停规则。原生怪物会移动、击退和攻击真人，测试需在近距离平地进行。AI 不会自主追敌；普通／软核 AI 现已支持自动重生。同一怪物的双向实测补充记录位于 `.local/test-results/combat-slime.json`。

## 自动重生选项（2026-10-21）

`.\scripts\smoke-test.ps1 -Respawn` 只运行本批相关场景，不重跑移动及三种敌人的完整检查。测试发放铁阔剑后，让 AI 走向原生僵尸以保持接触（白天僵尸会走开），待死亡后清理测试敌人，等原生倒计时结束自动重生；检查角色槽位和死亡记录延续、武器保留、旧动作清空、恢复移动及原生挥剑，再次死亡后 remove 并等待原计时结束，确认角色没有重新出现。`-Respawn` 与 `-Combat` 同时传入时优先执行重生场景。

快照增加 respawnTimer 和 respawnCount。death 事件记录原生初始倒计时，respawn 事件记录游戏 tick、槽位、恢复生命、保留武器、重生位置及世界出生点；本机普通世界实测计时和位置见 validation.md。报告仍在 `.local/test-results/smoke-日期时间.json`。

## 移动跨坎的小范围复测

沿用 invoke-test.ps1，在已有自然台阶附近 spawn，等角色落地后采集 status，执行 `/tpa left 6` 或 `/tpa right 6`，结束后再次 status。比较 autoJumpCount、位置、deathCount 和 moveTicks：应发生自动起跳并走过台阶，不能把死亡后返回出生点的位移算作跨坎。日志记录 `[TPA] AutoJump` 的触发位置。本次仅使用现有地形和入口，未修改世界方块、增加测试服务或重跑战斗与死亡整套测试。

自动重生逻辑始终默认开启；脚本清理测试角色不会修改该设置。本次跨坎测试后重新在真人旁生成了一个角色供继续观察。

## 测试入口的工作方式

针对挖掘目标可通过已有入口只读检查：`.\scripts\invoke-test.ps1 -Command 'tile 2103 295'`。reply是JSON，包含当前方块、CanKillTile和正上方物体／树干frame信息；只在独立开发入口开放，不执行任意代码或改变世界。用于本次原失败目标定位，不新增测试服务或通用调试平台。

- 只有通过 `-tpatest` 启动才启用。play.ps1 已添加该参数，普通 Steam 启动不启用入口。
- 脚本写入独立配置的 `TPA/request.json`；在 PostUpdateInput 中每三次输入更新检查一次，所有游戏操作在游戏线程执行。
- **测试游戏必须保持前台。** 本机单人失焦会暂停，并跳过输入及世界更新；后台请求可能超时，不宣称支持后台测试。普通状态/动作命令也可以先在 PowerShell 输入好，再切回游戏；批量脚本已提供 3 秒切换时间。AI 从后台工具调用时只需聚焦游戏一次，不需要逐步截图。
- 每条请求带唯一 id，响应写入 `response-id.json`，脚本读完移除。使用原子重命名避免半写入文件，每次只允许一条请求。
- 只接受现有 `/tpa` 演示动作，以及下文列出的输入诊断动作，不执行任意代码、外部程序或其他模组命令；不开网络端口。
- `/tpa` 请求复用现有 DemoCommand.Action 及角色控制层；没有复制一套行为实现。它不模拟聊天输入，也不测试 tModLoader 的聊天命令分发器。
- `ok=true` 代表请求已处理，不代表游戏行为已通过；行为通过需要状态断言或人工观察。
- 2026-10-22 起额外返回 accepted，区分游戏是否接受命令；新增 stopRevision 表示停止／移除版本。Agent 动作可携带 expectedStopRevision，过期时返回 interrupted=true 且不执行。旧脚本不带该字段仍可运行。停止使用独立 stop.json 优先处理，不占普通请求邮箱；所有游戏操作仍在游戏线程执行。
- 默认等待 5 秒。超时会报错，不能当作成功或直接重复执行有副作用的动作；先查询 status。
- 快照包含人类与 AI 的位置、生命、控制意图、更新计数、聊天和输入状态，不包含聊天内容。

## 自动打开聊天框

如果只是需要打开聊天框，可以直接调用游戏 API：

```powershell
.\scripts\invoke-test.ps1 -Command chat-open
.\scripts\invoke-test.ps1 -Command chat-close
```

这是原生 OpenPlayerChat/ClosePlayerChat API 路径，不是假装物理回车已经成功。聊天框之后是否可正常键盘输入仍属于输入链路测试。

如果需要验证原生 Enter 处理路径：

```powershell
.\scripts\test-enter.ps1
```

脚本提示后有 3 秒切回测试游戏，确保游戏处于前台。它向本游戏的 SDL 队列注入按下和松开，中间保持六次输入更新，再检查 FNA 按住采样与聊天打开状态。它不会向 Windows 或其他应用发送键盘输入。由 AI 在已聚焦的游戏窗口后台调用时可用 `-FocusDelaySeconds 0`。

此测试会先关闭聊天框，结束后也关闭聊天框，不用于提交聊天消息。原生 SDL 队列注入是游戏本地测试适配器，不代表 Computer Use 工具本身已经修好。

## Enter 故障调查结果

对照环境：同一单人测试世界，原生聊天逻辑，测试时没有菜单遮挡。

| 输入方式 | SDL 键盘事件 | FNA Enter 按住采样 | 聊天框 |
| --- | --- | --- | --- |
| Computer Use `Return` | 0 | 0 | 未打开 |
| 人工 Enter | 2，按下/松开相差 20ms | 1 | 打开 |
| Computer Use 普通字母 `a` | 0 | 0 | 在已打开聊天框内对照，不测试打开 |
| Computer Use `KP_Enter` | 0 | 0 | 未打开 |
| 本游戏 SDL Enter，保持六次输入更新 | 2 | 6 | 打开 |

自动 KP_Enter 对照时，Main.hasFocus 和 SDL_GetKeyboardFocus 均报告存在焦点，chatRelease=true，optionsOpen=false。人工对照证明探针可以捕获真实事件。

**已确认的故障边界：当前 Computer Use 键盘调用没有形成游戏可见的 SDL 键盘事件。** 不只是回车别名问题，也没有证据表明是聊天逻辑损坏。由于连事件都没有，当前证据不支持“按下和松开太快，帧采样漏掉”作为本次直接解释。

**尚未确证：工具内部具体用了何种注入方式，为什么没有形成 SDL 事件。** 没有修改或绕过桌面工具内部实现，也不声称原工具已修复。当前稳定方案是通过本地测试入口运行角色动作；需要原生 Enter 路径时使用上述游戏本地输入适配器。

诊断可以手动开启和关闭，默认不追踪：`trace-start` / `trace-stop`。只保留 Enter 事件和计数，不记录文本或其他按键值。

## 外部资料及其适用边界

- [SDL2 官方键盘状态说明](https://wiki.libsdl.org/SDL2/SDL_GetKeyboardState)：按下和松开均发生在事件处理之前，状态查询可能看不到按住。它是调查候选，不是本次已经证实的原因。
- [FNA SDL2 输入实现](https://github.com/FNA-XNA/FNA/blob/master/src/FNAPlatform/SDL2_FNAPlatform.cs)：FNA 处理 SDL 按键事件后维护自己的键盘状态。
- [FNA issue 671](https://github.com/FNA-XNA/FNA/issues/671)：报告了输入焦点不同步导致鼠标可用、键盘状态为空的情况；不是本项目已经复现的同一原因。
- [SDL2 官方 SDL_PushEvent](https://wiki.libsdl.org/SDL2/SDL_PushEvent)：支持事件队列注入，但不会改变 SDL 的设备状态；本项目已实测 FNA 事件处理后能识别 Enter，不能推定所有 SDL 游戏均可用。

## 本批验证记录

编译打包成功，0 警告、0 错误。第一轮自动测试暴露了共用响应文件在 Windows 上的占用竞争，已改成每个请求独立响应文件。另确认单人失焦原生暂停会导致入口超时；曾尝试其他回调，但没有得到稳定的后台通路，已撤回这些改动，保留前台批量测试。没有修改游戏暂停规则。

未安装 Cheat Sheet / HERO's Mod，未改变伤害、刷怪或资源规则。历史命令回填小模组已记入 TODO.md，本批不实现。

## 字符入口小对照（2026-10-21 晚间重新启动后）

新增诊断只在 trace-start 到 trace-stop 之间订阅 IImeService 的字符监听器，统计可打印字符数量，不保存文字内容；停止时解除订阅。

| 方式 | IME 已启用 | 游戏字符入口计数 | 聊天框长度 |
| --- | --- | --- | --- |
| Computer Use type_text 输入 /tpa status | true | 0 | 0 |
| 用户手动输入 abc，不提交 | true | 3 | 3 |

两次采样均有窗口焦点、SDL 键盘焦点且聊天框打开。原始计数分别保存在 .local/test-results/ime-auto.json 与 ime-manual.json。人工结果证明监听器有效；自动输入失败位于到达 IImeService 字符监听器之前，不是游戏收到文字却未显示。

writingText 在本回调采样时两次均为 false，但 IME 已启用且人工输入正常，因此不把这个瞬时值单独当作输入功能关闭的判据。测试结束已关闭追踪和聊天框。

补充网络调查：未找到当前 Computer Use 与此 tModLoader 版本完全一致、已确认可直接套用的修复。微软 KEYBDINPUT 文档明确区分 Unicode/VK_PACKET 输入和物理扫描码输入，并说明字符消息的转换环节；这是后续检查工具注入兼容性的依据，不能据此断定工具实际使用了哪一种方式：
https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-keybdinput

SDL 的历史 issue 892 也讨论了 Windows 字符消息、外部输入和编码的相互影响，但现象、版本均不同，不视为本问题根因或现成修复：
https://github.com/libsdl-org/SDL/issues/892

结论：当前角色控制架构与原生聊天文字接收路径均已有正常运行证据；需要继续定位的是桌面自动输入到 Windows/游戏字符入口之间的兼容环节。没有为此修改角色控制层或声称已修复自动文字输入。

## 游戏窗口内的原生聊天替代路径（2026-10-21）

已新增固定、只读的 /tpa status 全链路测试：

```powershell
.\scripts\test-chat.ps1
```

按提示在 3 秒内切回游戏。游戏已处于前台、由后台工具调用时可加 -FocusDelaySeconds 0。聊天输入必须为空；脚本拒绝覆盖已有草稿。

流程：打开原生聊天框 → 等待 IME 启用 → 每次输入更新向本游戏自己的 Windows 窗口发送一个 WM_CHAR → 检查 IME 收到 11 个字符、输入长度为 11 且命令尚未执行 → 发送提交字符 → 检查原生 ChatCommandCaller 执行 status 恰好一次。

实现位于 Common/NativeChatInput.cs；仅通过 -tpatest 开发入口暴露 chat-type-status 与 chat-submit-status 两个固定动作。窗口句柄来自本游戏 SDL 窗口，不接收外部窗口地址，不使用系统剪贴板，不接收任意文本，不直接赋值 Main.chatText，也不直接调用 DemoCommand.Action 提交。提交仍由原生聊天处理器解析。

DemoCommand 的执行计数、最后动作和调用者类型用于区分原生 ChatCommandCaller 与测试入口的 TestCaller。角色控制逻辑未改动。

实测：11 个可打印字符到达 IME；输入长度 11；提交前执行计数不变；提交后增加 1、lastAction=status、lastCaller=ChatCommandCaller，聊天框关闭并清空。报告：.local/test-results/chat-20261021-213338.json。编译 0 警告、0 错误。

这是 Windows 游戏进程内的替代适配器，**不代表 Computer Use 的物理按键或 type_text 已修复，也不宣称支持任意聊天命令、其他窗口或其他操作系统**。失败时不会自动清掉已输入的部分文字，需人工确认并清理后再测；结束后关闭字符追踪和聊天框。

消息语义参考：
- https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-char
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagew
