# 本地自动测试与 Enter 诊断

版本：0.1.1，日期：2026-10-21。

## 直接运行测试

继续使用项目现有的独立测试角色和普通小世界。先关闭旧测试游戏，再构建和启动：

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

只使用已有平整地表，不修改世界方块、不生成测试物资。截图仍用于检查可见性和动画，人工仍用于体验验收；结构化测试不能代替所有视觉检查。

## 测试入口的工作方式

- 只有通过 `-tpatest` 启动才启用。play.ps1 已添加该参数，普通 Steam 启动不启用入口。
- 脚本写入独立配置的 `TPA/request.json`；在 PostUpdateInput 中每三次输入更新检查一次，所有游戏操作在游戏线程执行。
- **测试游戏必须保持前台。** 本机单人失焦会暂停，并跳过输入及世界更新；后台请求可能超时，不宣称支持后台测试。普通状态/动作命令也可以先在 PowerShell 输入好，再切回游戏；批量脚本已提供 3 秒切换时间。AI 从后台工具调用时只需聚焦游戏一次，不需要逐步截图。
- 每条请求带唯一 id，响应写入 `response-id.json`，脚本读完移除。使用原子重命名避免半写入文件，每次只允许一条请求。
- 只接受现有 `/tpa` 演示动作，以及下文列出的输入诊断动作，不执行任意代码、外部程序或其他模组命令；不开网络端口。
- `/tpa` 请求复用现有 DemoCommand.Action 及角色控制层；没有复制一套行为实现。它不模拟聊天输入，也不测试 tModLoader 的聊天命令分发器。
- `ok=true` 代表请求已处理，不代表游戏行为已通过；行为通过需要状态断言或人工观察。
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
