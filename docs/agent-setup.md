# 安装、构建与 Agent 运行

更新于 2026-11-27。本文提供 Windows 新机准备、构建与 Agent 操作，验收结果见 [集中清单](acceptance-checklist.md)，邮箱和状态口径见 [多人协议](multiplayer-probe.md)。

## Windows 新机准备与构建

- 安装 Git、PowerShell 7、.NET 8 SDK 和 Python 3.12。`global.json` 选择兼容的 8.0 SDK；Python 宿主使用标准库。
- 实机需要 Steam 已登录并安装 Terraria、tModLoader、Cheat Sheet 和 HERO's Mod。已验证的组合为 Terraria 1.4.4.9、tML 2026.9.3.1 和 .NET SDK 8.0.428。
- 独立测试角色和世界使用项目内 `.local/tModLoader`。若需接续旧测试存档，只复制其中的 `Players` 与 `Worlds`；旧电脑角色名与世界名都没有要求。

在 PowerShell 7 中克隆并构建。将示例目录换成实际包含 `tMLMod.targets` 与 `tModLoader.dll` 的安装目录。

```powershell
git clone https://github.com/jack1415926/TerrariaPlayerAgent.git
Set-Location TerrariaPlayerAgent
$tml = 'C:\Program Files (x86)\Steam\steamapps\common\tModLoader'
.\scripts\build.ps1 -TmlPath $tml
```

启动时也传入 `-TmlPath $tml`，直接从仓库工作即可。产物位于 `.local/tModLoader/Mods/`。遇到钩子提示找不到原生分支时先核对 tML 版本；构建、占用及日志排查见 [调试说明](debugging.md)。

### 只有源码或无需启动游戏

先用 [离线检查](testing.md#仓库离线检查) 做 Python 与 Mock 工作。C# 构建可用同版本的 tModLoader 发布压缩包。旧版离线构建样本使用 [v2026.08.3.0](https://github.com/tModLoader/tModLoader/releases/tag/v2026.08.3.0)，它只作为该历史构建的依赖，当前实机版本见上文。解压到 `.local/tml/<版本>`，避免打包把游戏文件一并纳入。该压缩包路径只用于编译和打包，实机使用 Steam 安装。

在受限环境可把 .NET 与 NuGet 写路径设在项目内。只有需要隔离用户配置时才额外设置 APPDATA。

```powershell
$env:DOTNET_CLI_HOME = "$PWD\.local\dotnet-home"
$env:NUGET_PACKAGES = "$PWD\.local\nuget"
# 需要隔离用户配置时设置 $env:APPDATA = "$PWD\.local\appdata"
.\scripts\build.ps1 -TmlPath "$PWD\.local\tml\2026.08.3.0"
```

GitHub 网络异常时使用本机实际代理；此项目的构建与启动脚本不更改系统代理或 PowerShell 执行策略。只改文档、提示词或 Python 可复用运行中的游戏；C# 改动需要重建和重新加载模组。

## DeepSeek 配置

缺少本地配置时，复制模板后填写自己的 `TPA_API_KEY`。

```powershell
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
notepad .env
```

`scripts/run-agent.ps1` 读取本地 `.env` 的 `TPA_` 变量，已有进程变量优先。Python 只从环境读取凭据，变量见 `.env.example`。

| 配置 | 默认值 |
| --- | --- |
| API地址 | https://api.deepseek.com |
| 模型 | deepseek-flash |
| thinking | disabled |
| 最大输出 | 1024 tokens |
| 普通任务请求预算 | 8次，可用-MaxTurns修改（1–30） |
| API／游戏请求超时 | 45秒／5秒 |

这些是当前启动器配置，不代表未来服务状态或计费承诺。网络使用当前进程配置；本机已有通过HTTP_PROXY／HTTPS_PROXY指向Clash的运行记录，不设置全局Git代理。

`play.ps1` 将项目模组与两个辅助模组启用在独立配置中。AI 退出世界后不保存，验收时记录实际使用的辅助开关。

## 多人启动与固定傀儡 Demo

```powershell
.\scripts\play.ps1 -MultiplayerAgent -TmlPath $tml
.\scripts\run-agent.ps1 -HostMode -AgentMode multiplayer_server -MaxTurns 6
```

通过「开服并游玩」进入独立测试世界。持续游玩时省略 `-MaxGoals`，宿主按目标处理，空闲时待命。单次验收加 `-MaxGoals 1`，接完一单退出。后续出现 `Start run-agent.ps1…` 时，确认宿主窗口与心跳。

### 固定傀儡任务

在平地让默认持铜短剑的 AI 与傀儡隔开几格，距离最多 480 像素，真人靠近使傀儡激活。先 `/tpamp spawn`，再提交目标。

```text
/tpamp goal 靠近傀儡，用手上的铜短剑打中一次，然后停下告诉我结果
```

模型每轮读入最新的结构化位置和动作结果。任务选定最近的已激活傀儡，同层靠近最多 3 秒。每目标最多 6 轮模型请求和 3 次攻击尝试。服务器确认本轮指定武器命中后停手。主动取消时输入 `/tpamp stop`。背包换装及蓝史莱姆任务接入后的操作与验收场景见 [测试说明](testing.md#换装与蓝史莱姆合并验收待实机)。

完成、中断或失败都立即生成 `.local/test-results/goal-<目标ID>.json`，包含每轮发送给模型的观察、工具返回、动作时间窗、失败原因与确认的命中。宿主继续待命时也可读该报告。

手动武器、拾取和自动替换／停止的复测方式见 [测试入口](testing.md)。只读排查方式见 [调试说明](debugging.md)。

单在途模型请求会限制线程积累；旧请求可能仍在服务端执行／计费，新目标有时要等它返回才能开始模型调用。取消旧输入与取消服务端请求是两件事。

## 单人运行目标（历史基线）

以下只适用于单人。保持游戏前台，准备 AI 及测试木剑／铜镐；`/tpa equip wood/pick` 是开发发物资，不是正常成长来源。缺少时按调试说明准备，不将其复制到多人产品路径。

```powershell
.\scripts\run-agent.ps1 -GoalFile agent/prompts/explore.md -MaxTurns 12
.\scripts\run-agent.ps1 -Goal '向左走一小段，然后停止并报告实际位移。'
```

explore.md包含教程和有限探索／挖掘目标。宿主在调用模型前，以本轮AI当前X建立左右25格（400像素）范围及home；模型自行选路线、工具、目标。模板约定最多8批行动，允许遇险提前结束，并预留文字总结；这是模型任务约定，宿主硬限制为MaxTurns，不应说成已实现完整通用任务策略。

普通自定义Goal不会自动重设已有范围。不传Goal／GoalFile仍使用weapon-test.md历史武器任务；该任务目标与探索不同，不建议把它当默认自由游玩入口。`-CheckApi`仅检查连通，按需使用，不在每次运行前重复调用。

### 单人常驻宿主

```powershell
.\scripts\run-agent.ps1 -HostMode
```

单人用 `/tpa goal <文本>`（需 `-tpatest`）；切换与取消沿用单人身份和独立停止通道。常驻宿主的领取、回执及心跳见 [多人协议的文件邮箱](multiplayer-probe.md#文件邮箱)。单人实测边界见 [单人阶段报告](milestone-acceptance.md)。

系统提示词只发能力、规则和限制；目标单独传入，附当前观察和工具定义，不每轮重发整份项目文档／百科。

## 单人工具语义与观察

commands最多6条白名单命令，必须写完整`/tpa`前缀，只有status可省略；wait_seconds为整批后的额外模拟等待（0–15秒）。

| 操作 | 语义 |
| --- | --- |
| left/right、jump left/right、down | 定时输入；方向jump组合水平与跳跃，普通jump保留已有移动；down只尝试原生平台下落 |
| goto pixelX、home | 默认最多10秒；本地位置／速度控制，水平8像素误差内、落地低速才arrived |
| select slot | 选择已有库存物品，不生成 |
| mine tileX tileY [seconds] | 世界方块格坐标，默认最多2秒、最多10秒；须选普通挥动镐，复用原生范围／强度／冷却 |
| attack left/right [seconds] | 朝指定方向原地用支持的剑，不追敌 |
| stop | 停止主动输入；不冻结惯性、重力和已开始的原生动画 |

宿主逐条等待移动／跳跃／使用输入和当前动画结束，再发下一条；即时命令直接返回。手动聊天命令没有该顺序等待。结果actions包含实际状态；blocked、rejected、unconfirmed、interrupted、unknown与completed分别记录，拒绝／受阻等情况跳过该批剩余动作。

goto／home核对本次sequence和实际误差，挖掘核对本次目标、工具及原生broken事件。没有命中事件不能根据目标掉血补出归属；草地变泥土不算移除，移除不算入包。

## 观察与范围

- terrain：17×11格，从上到下／左到右，含世界原点、脚下坐标。headClear表示头顶空间；jumpClearance只表示发现可尝试跨越的障碍，无障碍时也可为false。未知液体／区域不作安全推断。
- mineCandidates：附近实心目标，已排除原生CanKillTile=false的受保护方块；canKill=true不保证镐强度足够。收到native_cannot_kill_tile就换目标，不重复空挖。
- nearbyHostiles：480像素内最多8个最近的有接触伤害敌怪；dx/dy相对AI中心，正值在右／下，含距离／速度／生命／接触状态。contactDamage是NPC.damage，实际伤害看hurt；列表和槽位不是永久事实，也不覆盖所有弹幕。
- inventory／mining／navigation：实际背包、本次挖掘结果、目标及活动范围，不能把旧事件或历史arrived当成当前新任务的成功。

真人可用`/tpa range 25`设置范围（允许1–100格），或`/tpa range off`解除，同时取消旧任务。模型工具白名单不开放range。范围保留到重设／移除／退出世界，普通stop不清除；被击退或重生可能出界，outsideBounds反映实际位置，向内返回仍允许。

## 单人停止、失败与报告

游戏内`/tpa stop`，或另一个PowerShell：

```powershell
.\scripts\run-agent.ps1 -Stop
```

stop.json独立优先处理，游戏侧更新stopRevision；Agent等待API、动作或额外等待期间检查取消，后续动作带expectedStopRevision，游戏线程拒绝过期任务。旧任务清理也带版本，不覆盖新任务。迟到API响应不执行，但请求本身可能已在服务端完成／计费。

当前失焦暂停可能让入口不响应，停止超时只能说未确认；聚焦后先查状态，不盲目重发有副作用动作。

宿主侧已区分五类中断：明确暂停（paused）、tick冻结但桥仍应答、请求没被消费（未执行）、被消费但无回执（结果未知）、主动取消。挂起后会等通信恢复并核对stopRevision、世界、AI身份与仍在执行的动作；仍在执行则先发stop接管，再按unknown收尾，不重放原命令。结果和报告带interruption（paused／tick_stalled／unreachable／cancelled／unknown）和resume（resumed／takeover／cancelled／reason）。以上只做了离线检查（2026-11-10，27项通过），未经实机验证；失焦时的真实行为和后台持续模拟仍需在验收电脑确认。

正常结束、预算用尽或异常时宿主尝试stop并保留AI；无法确认时记stop_error。报告在`.local/test-results/agent-日期时间-标识.json`，包含模型实际调用、游戏结果和usage。model_finished不等于通用目标自动验收通过；默认武器任务另有指定命中判定，自定义Goal须结合实际证据。

## 已有检查与用量

仓库离线检查、现场验收和报告位置见 [测试入口](testing.md)。单人历史上下文与本轮短 Demo 的用量见 [上下文用量样本](development-experience.md#agent-上下文用量与后续观察)。

报告的 `turns=0` 也可能出现在请求已发出却在响应返回前取消的情况。
