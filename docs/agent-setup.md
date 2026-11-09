# Agent接入与运行

更新：2026-10-22。当前已通过单人前台的基础操作闭环：goto到位、原生挖3块、home返程。证据与边界见 [阶段性验收](milestone-acceptance.md)，下一步见 [TODO](../TODO.md)。这是有限任务原型，游戏内自然语言入口和持续陪玩尚未交付。

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

先按 [调试说明](debugging.md) 启动独立配置，进入专用测试世界并保持前台。确保AI存在且已有木剑／铜镐；缺少时用开发命令spawn、equip wood、equip pick发一次，默认slot0／slot1，再select 0。宿主不会自动发这套装备，测试物资不等于普通初始装备已接入。AI退出世界不保存。

## 运行目标

```powershell
.\scripts\run-agent.ps1 -GoalFile agent/prompts/explore.md -MaxTurns 12
.\scripts\run-agent.ps1 -Goal '向左走一小段，然后停止并报告实际位移。'
```

explore.md包含教程和有限探索／挖掘目标。宿主在调用模型前，以本轮AI当前X建立左右25格（400像素）范围及home；模型自行选路线、工具、目标。模板约定最多8批行动，允许遇险提前结束，并预留文字总结；这是模型任务约定，宿主硬限制为MaxTurns，不应说成已实现完整通用任务策略。

普通自定义Goal不会自动重设已有范围。不传Goal／GoalFile仍使用weapon-test.md历史武器任务；该任务目标与探索不同，不建议把它当默认自由游玩入口。`-CheckApi`仅检查连通，按需使用，不在每次运行前重复调用。

系统提示词只发能力、规则和限制；目标单独传入，附当前观察和工具定义，不每轮重发整份项目文档／百科。

## 工具语义

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

## 停止、失败与报告

游戏内`/tpa stop`，或另一个PowerShell：

```powershell
.\scripts\run-agent.ps1 -Stop
```

stop.json独立优先处理，游戏侧更新stopRevision；Agent等待API、动作或额外等待期间检查取消，后续动作带expectedStopRevision，游戏线程拒绝过期任务。旧任务清理也带版本，不覆盖新任务。迟到API响应不执行，但请求本身可能已在服务端完成／计费。

当前失焦暂停可能让入口不响应，停止超时只能说未确认；聚焦后先查状态，不盲目重发有副作用动作。游戏tick持续2秒不前进、暂停或等待超限标unknown并结束；死亡／移除／停止取消旧动作。尚未实现暂停挂起恢复，这属于下一批。

正常结束、预算用尽或异常时宿主尝试stop并保留AI；无法确认时记stop_error。报告在`.local/test-results/agent-日期时间-标识.json`，包含模型实际调用、游戏结果和usage。model_finished不等于通用目标自动验收通过；默认武器任务另有指定命中判定，自定义Goal须结合实际证据。

## 已有检查与用量

离线检查入口：`python -m unittest discover -s agent -p test_run.py -v`。最近17项通过；文档修改不用重跑。游戏机制、停止和最新模型闭环证据集中于阶段报告与validation.md，不每次开工全量重验。

最新完整任务8次请求累计153,201 tokens，含缓存及重复历史，未换算金额。首轮39,505不是当前固定基线；详细口径见 [上下文分析](agent-context-cost.md)。后续持续任务前再评估有界历史和观察去重，不以大型记忆系统或框架迁移阻塞当前功能。
