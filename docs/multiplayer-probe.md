# 多人虚拟 Player：运行、协议与验收

更新：2026-11-13。当前一名真人场景已通过基础角色、显示平滑、清理、原生接触受伤／死亡／软核重生、服务器只读桥，以及真实模型中文短目标／反馈。自动 A→B→stop 验收通过，60tick输入未恢复。主动攻击、正常装备来源、多人挖掘、保存和第二客户端仍未交付或未验；历史过程按日期单列。

## 范围与运行

在独立测试多人世界中加载当前构建的模组，真人客户端使用下列命令；服务器也必须加载同版本。不要直接使用当前仍在运行的旧单人程序集。

```text
/tpamp spawn
/tpamp status
/tpamp diag
/tpamp team 1
/tpamp right 1
/tpamp stop
/tpamp remove
```

team 为原生数值 0–5；本轮仅验证队伍 1 的当前客户端红队地图／信息显示，未验其他颜色或其他组队效果。左右移动限制 1–10 秒（整数），客户端与服务器均校验。生成者独占控制、离线清理及真实连接占槽时让位已写入代码，仍待实测；其他客户端查询和控制拒绝也待补。

服务器执行移动控制，每6tick发送状态，客户端创建显示镜像，反馈走聊天回包；不直接改坐标伪造移动。AI没有真正的网络客户端连接，不宣称享有全部联机玩家语义。自然语言短目标和接触受伤／重生已接入；主动攻击、正常装备、挖掘、持久化未交付。`/tpa`仍限单人，JSON邮箱按模式分工，见下方批次1／2。

本机默认由用户使用“开服并游玩”，助手启动脚本只启动客户端；不要同时再开独立服务器占用端口。需要专门验证生成者离线且服务器存活时，另行安排持久服务器或让非房主成为生成者。

## 多人基础验收范围

本轮一名真人基础项已通过；以下第二观察端、非生成者和存活服务器离线项仍待验，不将代码保护写成实机通过。下一批计划只在 TODO 维护。

- 一名真人客户端连入本地服务器，spawn 后看到一个独立 AI，服务器 status 返回不同的槽位；客户端镜像和服务器位置一致。
- 设置队伍后检查颜色、队伍 UI 和位置提示；不要只以回包文本作成功证据。
- right 1 后比较服务器实际位移与客户端显示，stop 后主动输入不继续。
- 第二观察客户端晚加入后看到同一个 AI，而非生成第二个；两端核对 team 和位置。
- 非生成者控制被拒；remove、生成者退出、退世界后无遗留镜像；真实连接占用探针槽位时让位。

第一轮只验证一名真人＋一个 AI，但跨客户端同步最终必须补第二观察端。可在同一电脑运行服务器与两个客户端，无需两台实体电脑。

## 首次构建证据（历史）

C# 编译及隔离目录打包通过，0 警告 0 错误。普通构建因运行中 tmod 文件被占用而打包失败，未改写该历史失败。隔离包输出到 `.local/mp-build/Mods/TerrariaPlayerAgent.tmod`；正式加载前退出旧测试实例，再运行正常构建。尚无多人运行、移动或队伍成功证据。

## 首轮实机与修复状态（历史，2026-11-10）

用户确认生成、当前客户端红队地图／队伍信息显示、移除通过；移动失败，停止因静止前提未通过验收。移动状态累计更新仅 1 次。已根据本机连接维护逻辑新增虚拟槽位 active 保护及聊天空连接过滤；编译打包通过，尚未加载复验。下一次先检查更新计数与短距离移动，再在移动中 stop；不以停止静止角色作为取消成功。详细历史见 validation.md。

## 基础验收证据（历史，2026-11-10）

修正连接清理保护和 ILLabel 加载问题后，用户确认按秒移动、遇坎自动跳跃及清理回归成功。服务器日志记录 right 3 位移约 434 像素；left 9 在约 4.2 秒收到 stop，随后连续采样输入清零、速度为零、位置稳定。最终 remove → status 返回 No demo → spawn → remove 均有服务器回包与用户观察，当前 AI 已移除。本轮使用 HERO 设为中午并关闭刷怪，不证明战斗／生存规则。

11月10日当时的范围调整：先做好一名真人＋AI，先做移动同步的离线诊断；第二观察客户端、权限、存活服务器离线及占槽验证延后。当时多人战斗、挖掘、复活、保存与自然语言入口尚未迁移。后续已完成的平滑、受伤／重生和短目标以本文当前摘要及11月13日批次为准，不把历史计划当成当前待办。

## 显示同步与诊断（2026-11-11 离线开发）

原有目的：服务器持有权威 `Player`，客户端只放一个显示用镜像，每 6 tick 收到一次位置／速度后直接覆盖 `mirror.position`。这样权威性清楚、实现最少；代价是客户端两次包之间（约 100 ms）镜像会继续走原生 `Player.Update`，下一包又把位置拽回，形成可见锯齿。镜像是一个 `active` 的 `Player` 放进 `Main.player[]`，客户端会按正常玩家路径更新它；这一点由现有镜像实现和 `PostUpdatePlayers`（tModLoader 文档：Players 更新之后调用）推断，**客户端对非本地玩家是否完整跑物理更新尚未实机或反编译确认**。

离线改动只落在显示层：

- 服务器每包追加快照序号与服务器 tick；发送频率仍是每 6 tick，没有靠提高频率掩盖问题。
- 客户端不再在 `Receive()` 里写位置，只把快照交给 `Common/DisplaySync.cs`：12 条有界缓冲、120 ms 播放延迟、最多 150 ms 外推、0.5 s 无包判 stale、96 px 判瞬移、速度低于 0.05 不外推。旧序号和重复序号直接丢弃，绝不让旧快照覆盖新状态。
- 瞬移／重生识别：服务器在固定 6 Tick 发送节奏上比较“本次与上次已发送位置”，超过 96 px 就在包尾 flags 里置位；客户端只认这个显式标记，不再拿跨断流的距离猜。标记缺失时（旧协议或离线快照）退化为“只在正常节奏到达且距离超过 96 px 时判瞬移”。断流期间发生的显式传送仍立即重置缓冲；断流期间没有标记的大位移走限速恢复。
- 单位：`Player.velocity` 是世界单位／模拟 Tick，不是像素／秒；按 Terraria 的 60 Tick/s 换算，150 ms 上限对应 9 Tick。外推目标是 `renderTime − 最新快照接收时间`（不是 `now − 接收时间`），所以 120 ms 播放延迟不会被额外算进预测。
- stale 只做标记：超过外推上限后位置冻结在同一预测端点，不会在越过 `StaleAfter` 时退回最后包位置，避免明显跳变。停止时速度低于阈值直接收敛到权威位置。
- stale 恢复：只有断线超过 `StaleAfter` 后重新收到快照才进入“重同步”：显示按 `MaxCorrectionSpeed = 600 px/s`（60 fps 下每帧约 10 px）向新目标收敛，差距小于一步时精确落位；正常回放、首次采样、瞬移重置仍直接赋值，不给普通移动平添延迟。这样恢复时不会从预测端点瞬间跳回旧位置，约 30 px 的偏差需要几十毫秒收敛。
- `ServerTick` 目前只用于诊断，不参与时间轴：缺少服务器 Tick 与客户端墙钟的可靠对齐证据，且 Tick 计数会在服务器重启后归零。先用“接收时间回放”；若实机显示抖动与网络节拍相关，再评估用相邻快照的 `Δtick / Δ接收时间` 估计速率。
- 固定 60 Tick/s 的局限：服务器若因卡顿实际降速，墙钟外推会超前，最多 9 Tick 后冻结；`stale` 与外推计数可暴露这种情况。是否改成自适应 Tick 速率需要实机数据，本批不引入。
- 120 ms 延迟对停止的影响：位置会精确停在权威停止点（速度低于阈值不外推），但“停下来”的视觉时刻比服务器晚约 120 ms；是否可接受需要人眼确认，缩短延迟会牺牲插值余量。
- 诊断计数按当前镜像实例统计：移除、换槽、退世界触发清镜像时会归零，`/tpamp diag` 看到的是这一实例的数据。
- `PostUpdatePlayers()` 在客户端每 tick 采样并写 `mirror.position`，速度仍取最新权威值；服务器位置、速度、碰撞、移动输入、动作时长、stop 归属、控制权与槽位规则均未改动。
- 槽位变化、`slot=-1`、退世界、清镜像时一并重置显示缓冲；瞬移（重生／传送）会丢弃旧缓冲，不跨瞬移插值。
- 诊断有界：接收／重复／乱序／外推／stale／瞬移计数，以及最近 64 条校正量（序号、服务器 tick、接收时间、位移量）；`/tpamp diag` 打印摘要与最近 5 条，不写日志文件、不无限累积。

离线证据（尚未实机）：`dotnet run --project tools/DisplaySync.SelfCheck -c Release` 的 108 项回放检查通过，覆盖正常移动、停止、方向反转、不规则间隔、延迟与丢包、乱序／重复、瞬移、重置、历史有界、px/tick 换算、120 ms 延迟生效、150 ms／9 Tick 外推上限、stale 前后位置连续、移动中突然停止不越界、stale 后恢复（落后／领先、速度反向、恢复后立即停止、连续断线），以及本轮新增的瞬移对照（正常节奏瞬移立即重置、断流后 190 px 正常移动不被误判瞬移、断流中显式传送仍立即重置、显式“未传送”走限速恢复）；模组构建 0 警告 0 错误。回放只证明纯逻辑，不证明游戏画面已平滑。

实机复验时应先看 `/tpamp diag` 的校正量与 stale／外推计数，再看画面是否仍抖；如果客户端实际不会给镜像跑物理更新，锯齿假设不成立，平滑仍正常但收益会变小。

## 服务器只读桥与 Probe（批次 1，2026-11-11）

批次 1 只打通“Python 能读到服务器 AI 的权威状态”，不开放目标与动作。

- `Common/MultiplayerAgentBridge.cs`：服务器侧 ModSystem，在 `PostUpdateEverything` 里每 3 tick 轮询一次邮箱；**只有 `Main.netMode == NetmodeID.Server(2)` 才消费**，客户端不消费。只接受 `status`，其他命令直接拒绝；响应为 `response-<id>.json`，`state` 字段即服务器快照。
- 服务器快照字段：`agentMode="multiplayer_server"`、`netMode=2`、`worldSession`、`stopRevision`、`tick`、`gameMenu`、`paused`、`focused=null`、`player=null`、`actor`（沿用 `DemoPlayerSystem.Snapshot()` 的完整字段，并新增 `active`、`generation`）。
- `actor.generation`：Spawn 成功 +1，实际移除 Actor +1，`ModPlayer.OnRespawn`（XML 文档写明服务器也会调用）触发 +1；重复 Remove、失败 Spawn 不递增。generation 与 worldSession 一起标识 AI 实例生命周期。
- `LocalTestBridge.Poll()` 增加 `netMode != SinglePlayer` 直接返回；与服务器桥互斥，同一目录不会双消费。
- Python `--probe --agent-mode multiplayer_server`：只发一次 `status`，不读 `goal.json`、不调模型、不执行动作；校验 `netMode=2`、`agentMode`、`player=null`、`actor.slot/generation` 后打印摘要退出。`--probe` 与 `--host`／`--goal`／`--stop`／`--check-api`／`--max-goals`／非默认 `--goal-file` 冲突会在启动前报错；`multiplayer_server` 在批次 1 必须配合 `--probe`。
- 邮箱路径：Python 启动打印 `Mailbox: <绝对路径>`；两个 C# Bridge 在 `OnModLoad` 记录 `[TPA]`／`[TPA-MP] mailbox=<绝对路径>`。二者必须一致（`play.ps1` 把 `-tmlsavedirectory` 指向 `.local/tModLoader`）。

离线证据：61 项 Python 测试通过（新增模式校验、probe 只发 status、冲突参数、`actor.generation` 等 10 项）；用假服务器桥做了一次文件协议烟测，probe 输出服务器快照、单模拒绝 netMode=2、冲突参数被拒；C# 构建 0 警告 0 错误。尚未实机。

### Host & Play 显式启用只读桥（2026-11-13）

“开服并游玩”创建的服务器不会转发客户端的 `-tpatest`。需在启动客户端时显式启用可继承的只读开关：

```powershell
.\scripts\play.ps1 -MultiplayerBridge
# 开服进入测试世界并执行 /tpamp spawn 后：
.\scripts\run-agent.ps1 -Probe -AgentMode multiplayer_server
```

`-MultiplayerBridge` 仅给新游戏进程设置 `TPA_MP_READONLY_BRIDGE=1`，由其创建的服务器继承；不改用户／系统环境变量。未设置环境变量且无 `-tpatest` 时桥默认关闭；原有服务器 `-tpatest` 方式仍保留。PowerShell 7 的 `Start-Process -Environment` 为启动器依赖（本机已验证支持）。

此开关只启用 `MultiplayerAgentBridge`，不启用单人 `LocalTestBridge`，不开放目标或动作。客户端即便继承开关也不消费邮箱；只有 `netMode=2` 的服务器处理 `status`。独立服务器可给自身设置同一个进程级环境变量，Python 必须运行在能访问该服务器邮箱的机器上；本批不增加远程协议或邮箱路径配置。

检查服务器日志中的 `[TPA-MP] mailbox=...` 是否与 Python 的 `Mailbox: ...` 一致。AI 移除后原始状态应为 `actor=null`（要求有 AI 的 Probe 会明确失败）；重建后 `generation` 增加，同一世界重开后 `worldSession` 应变化。实际本轮验收结果见 validation.md。

2026-11-13 本机 Host & Play 已通过上述检查：服务器 slot 254、generation 1→3，世界重开后 worldSession 改变、actor=null；实际服务器拒绝动作和目标请求。退回主菜单的过渡期，单人桥可能返回 netMode=0 的状态，Probe 会拒绝，待服务器就绪后再读；不把该状态当成服务器状态。远程客户端及独立服务器未实测。

## 多人短目标宿主（批次 2，2026-11-13）

在只读桥之外显式开放本批短任务：

```powershell
.\scripts\play.ps1 -MultiplayerAgent
.\scripts\run-agent.ps1 -HostMode -AgentMode multiplayer_server -MaxTurns 3 -MaxGoals 4
```

进入多人世界后由生成 AI 的真人输入：

```text
/tpamp spawn
/tpamp goal 向右走一秒，然后停止，完成后告诉我
/tpamp stop
```

`-MultiplayerAgent` 给游戏及其子服务器设置进程级 `TPA_MP_AGENT=1`，并启用只读桥；`-MultiplayerBridge` 继续只读。多人宿主只能从游戏控制者接收目标，不接受命令行 `--goal` 或无身份外部 stop。每个动作仅限左右移动（>0、最多3秒）与停止，模型和服务器双侧校验；不开放生成、发装备、攻击、挖掘、goto 或跟随。手动 `/tpamp left/right` 会取消当前目标并接管。

游戏即时回复提交结果，随后显示宿主接单、开始移动、任务结束或取消。最终总结仅表示模型结束，实际完成仍看位移和动作记录。无需使用 `/tpamp combat`；该命令只用于前一批战斗诊断。

目标绑定 `worldSession`、`actorGeneration`、`controllerToken`、`hostSession`、目标 id 和 `stopRevision`。控制者身份关联服务器上的真人 Player 对象及当前 Socket；重新连接或重建 AI 后旧身份无效。新目标先停止旧输入，再替换未读目标；旧响应和收尾不能派发到新目标。stop、remove、死亡/重生、断线、世界变化、宿主心跳过期或重启均使旧目标失效。

宿主以 OS 文件锁保持单邮箱单宿主；启动前已有目标先标 stale，再发布 ready 心跳，不依赖文件时间戳推断。模型调用最多一个在途请求：旧请求不能保证在服务端取消或免计费，新目标可取消旧动作，但可能等待旧请求结束才开始下一次请求。待机不请求模型，目标独立预算/历史；本批仍由外部 Python 宿主运行。

多人请求现在带 `agentMode`，主菜单的单人桥会保留它给服务器处理，替代上节批次 1 过渡期可能读到单人快照的行为。单人未标记请求与原有暂停/恢复入口保留。

本批71项仓库内Python检查通过；本机临时DLL检查由40项协议／门控增加到43项（含验收协调器），并非公开可直接复跑的CI套件。构建和实际加载通过。实机中文短目标由用户确认，完整宿主报告为 `.local/test-results/agent-20261113-193551-17a6e7.json`：2目标finished、2目标interrupted，随后宿主退出并删除心跳。

### 自动验收替换与停止

游戏已用 `-MultiplayerAgent` 启动、多人宿主在线且当前无活动目标时，真人只需 `/tpamp spawn`，随后运行：

```powershell
.\scripts\test-multiplayer-goals.ps1
```

脚本通过带控制者／世界／AI 身份的开发请求启动有界服务器验收。验收直接复用 `QueueGoal` 和 `StopGoal`：A 接单后替换为 B，确认 B 左移输入开始后立即停止，再观察 60 tick 输入不恢复，并核对两目标的 interrupted 结果。服务器最长等待60秒，脚本最长70秒；不伪造模型响应、移动或伤害，不通过 UI 抢输入时机。它会使用现有真实宿主的两个目标额度；宿主剩余额度不足时先重新启动宿主，避免把预算不足当作功能失败。

报告在 `.local/test-results/acceptance-<id>.json`，服务器原件在邮箱中。失败、死亡或世界变化保留失败证据，不盲目重跑。服务器测试入口与目标动作一样依赖显式 `TPA_MP_AGENT=1`，只读模式不可调用。新增验收协调逻辑也已通过3项真实 DLL 检查，服务器针对性检查合计43项。

2026-11-13 实机报告 `.local/test-results/acceptance-85ea8609a8c1425ba919cce4601b81f6.json` 为passed：A接单后以新版本替换B，B左移输入开始后立即停止，两个结果均interrupted，之后60tick输入未恢复。末次测试默认加载两个辅助模组；具体白天／禁刷怪开关未说明，不推断已生效。收尾报告 `.local/test-results/goal-closeout-1364583f.json` 为AI生命100、存活、无目标／主动输入，宿主心跳已删除。这些是验收时点，不是实时服务状态。
