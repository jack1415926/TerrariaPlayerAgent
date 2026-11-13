# Agent接入与运行

更新：2026-11-13。当前优先体验多人短目标：原生中文聊天、接单／行动／结束反馈、替换与停止已通过本轮实机。仅开放左右短移动和停止；多人主动攻击、正常装备来源、挖掘与持续陪玩未交付。单人历史攻击／挖掘／探索入口单列保留，不能用于多人。当前证据见 [集中清单](acceptance-checklist.md)，计划见 [TODO](../TODO.md)。

## 配置与准备

Python 3.12标准库，不需额外SDK。`scripts/run-agent.ps1`读取本地`.env`的TPA_变量到进程环境，已有进程变量优先；Python只从环境取凭据。`.env`不提交Git，变量名见`.env.example`，报告不含凭据。

| 配置 | 默认值 |
| --- | --- |
| API地址 | https://api.deepseek.com |
| 模型 | deepseek-flash |
| thinking | disabled |
| 最大输出 | 1024 tokens |
| 普通任务请求预算 | 8次，可用-MaxTurns修改（1–30） |
| API／游戏请求超时 | 45秒／5秒 |

这些是当前启动器配置，不代表未来服务状态或计费承诺。网络使用当前进程配置；本机已有通过HTTP_PROXY／HTTPS_PROXY指向Clash的运行记录，不设置全局Git代理。

默认通过 `scripts/play.ps1` 启动独立配置，加载项目模组、Cheat Sheet、HERO's Mod，不另用只加载项目模组的手动启动方式。角色／世界只用专用测试存档，AI退出世界不保存；辅助开关按实际记录。

## 多人短目标与自动验收

```powershell
.\scripts\play.ps1 -MultiplayerAgent
.\scripts\run-agent.ps1 -HostMode -AgentMode multiplayer_server -MaxTurns 3 -MaxGoals 4
```

“开服并游玩”进入测试世界后输入 `/tpamp spawn`，再用 `/tpamp goal <自然语言目标>` 下单、`/tpamp stop` 取消。每次移动最多3秒，目标由当前生成者控制，新目标替换旧目标。当前不接受多人命令行 `-Goal` 或无目标身份的外部 `-Stop`。一邮箱一宿主，示例4目标后退出，待机不请求模型；每目标预算和历史独立。

宿主已启动、无活动目标、AI活着且剩余至少2目标额度时运行 `scripts/test-multiplayer-goals.ps1`。脚本按服务器事件完成替换与停止，不能同时人工操作；报告在 `.local/test-results/acceptance-<id>.json`。只读排查用 `play.ps1 -MultiplayerBridge` 与 `run-agent.ps1 -Probe -AgentMode multiplayer_server`，不开放动作。模式／身份字段与有界诊断见 [多人协议](multiplayer-probe.md)。

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

宿主每0.2秒检查`.local/tModLoader/TPA/goal.json`；有目标就以该文本执行一轮，写完`goal-ack-*`和`goal-result-*`后回到待机。没有目标时不请求模型，单个目标失败不退出。`-MaxGoals N`执行N个目标后退出，便于验证。游戏内`/tpa goal <文本>`（需`-tpatest`）已接入：命令先`RequestStop`停止当前主动输入、递增stopRevision使旧任务失效，再把目标连同当时的stopRevision／worldSession写入`goal.json`；连续输入只保留最新目标，`/tpa stop`、`/tpa remove`、退出世界会清掉尚未发出的目标。宿主接单前核对这些身份信息，接单后进入执行前再核对一次：校验到启动之间发生stop或换世界时目标直接中断，不会被新一轮状态掩盖。世界未运行、游戏暂停时直接拒绝，不排队等待。宿主驻留时由独立心跳维护`host-ready.json`（每5秒，执行长任务、等待API和待机期间都刷新），正常退出时删除该文件；超过15秒未刷新视为不在线，游戏侧拒绝下单。写回的确认与结果在聊天框显示为`[TPA <id>]`消息，旧目标的结果标为忽略。C#侧只做了编译验证，未实机；协议字段和四列状态见 [集中实测清单](acceptance-checklist.md)。

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

离线检查入口：`python -B -m unittest discover -s agent -p test_run.py -v`；先按 testing.md 准备可写测试目录。11月13日71项通过，包含多人身份和共享宿主保护。43项 DLL 检查是本机临时检查，不是仓库内完整回归套件。历史失败仍保留在 validation.md，文档修改不重跑测试。

历史单人8次请求任务累计153,201 tokens，含缓存及重复历史，不是本轮多人任务用量，也未换算金额。首轮39,505不是当前固定基线；详细口径见 [上下文分析](agent-context-cost.md)。本轮被取消的请求仍可能计费，报告 turns=0 不能证明未发请求。后续按实际任务测量，不以大型记忆系统或框架迁移阻塞玩法。
