# 集中实测清单

更新：2026-11-18。当前摘要与多人批次1–5反映最新状态；单人P0/P1/P2的详细过程主要在 [验证记录](validation.md)，[阶段报告](milestone-acceptance.md) 保留单人阶段结论；本页不重复历史表，也不把单人结论回填多人。

## 当前验收摘要

实机环境为 Terraria 1.4.4.9／tML 2026.9.3.1；当前离线构建与 IL 核对用 2026.08.3.0，两处版本差异以实际加载日志为准。一名真人通过“开服并游玩”进入专用测试世界。11月13日自动验收确认三模组加载，用户反馈已准备好；具体辅助开关未说明，不沿用旧轮设置推断本轮环境。

| 验收对象 | 结论 | 证据与边界 |
| --- | --- | --- |
| 多人生成／持续更新 | 通过一名真人场景 | AI slot 254、真人 slot 0；更新计数持续增加 |
| 限时左右移动／遇坎跳跃 | 通过本轮地形 | 用户观察与日志一致；right 3 位移约 434 像素，有 AutoJump，不代表完整寻路 |
| 移动中 stop | 通过本轮动作 | left 9 约 4.2 秒后 stop；之后输入清零、速度零、连续位置不变 |
| 红队显示 | 通过当前客户端 | 真人加入队伍 1 后，用户确认地图／队伍信息及红色显示 |
| 移除／再次生成／最终清理 | 通过（2026-11-13 复验） | 保留 AI 后退出服务器及游戏，再开服进入同一世界无旧 AI，spawn/remove 正常 |
| 视觉平滑 | 通过本轮一名真人场景 | 用户确认移动平滑、停止及时；断流恢复、瞬移及第二客户端未实测 |
| 第二客户端、控制归属、离线与槽位冲突 | 待验 | 代码保护不等于实机；房主退出关服不证明存活服务器清理 |
| 多人接触受伤／死亡／软核重生 | 通过本轮场景 | 自然受伤／死亡／重生后再次受伤有日志；用户选敌反馈正常，无独立选敌根因证明 |
| 多人中文短目标／反馈／替换／停止 | 通过 | 两个真实模型目标完成；自动 A→B→stop 通过，60tick输入不恢复 |
| 多人初始装备与持有物显示（批次3） | 离线实现，游戏待验 | 服务器 Spawn 按普通经典三件经 `GetStartingItems` 初始化一次；初始化失败不发布 AI；客户端只显示选中槽；37项回放通过；未实机 |
| 多人铜短剑手动攻击（批次4） | 离线实现，游戏待验 | `/tpamp attack left/right [1]` 经控制者／槽位／物品校验后产生原生刺击 projectile；服务端记录 attack-input／projectile-hit；60项自检通过；未实机 |
| 多人普通近战挥砍（批次5） | 离线实现，游戏待验 | `/tpamp equip iron`（隔离测试物资，需服务端 `TPA_MP_TEST_EQUIP=1`）+ `/tpamp attack left/right [1]`；Iron Broadsword 走原生直接近战；item-hit 记录；60项自检通过；未实机 |
| 自动索敌、多人挖掘／拾取／保存 | 未交付 | 手动短攻击不等于完整战斗；测试发物资不能作为正常成长 |
| 单人历史基线 | 见 [阶段报告](milestone-acceptance.md) | 原生木剑／铁阔剑攻击、铜镐挖掘、goto／home 有阶段性实测；不能直接当作多人能力 |

多人命令与逐批细节见 [多人探针](multiplayer-probe.md)；历史失败（不更新、微移、钩子加载错误）保留在 validation.md。

## 状态口径

| 状态 | 含义 |
| --- | --- |
| Python离线通过 | 仓库内 `test_run.py` 当前71项，使用 FakeGame、临时邮箱及宿主同步保护，不启动游戏 |
| 持有物回放 | `tools/InventorySync.SelfCheck` 37项，验证6字节包布局、三件清单、失败处理与单一调用点；不验证发放／绘制 |
| 显示回放 | `tools/DisplaySync.SelfCheck` 110项，纯逻辑回放，不证明画面 |
| 攻击回放 | `tools/Combat.SelfCheck` 60项，验证命令／身份策略和源码调用契约；不证明原生射弹或命中 |
| 本机DLL检查 | 43项在被忽略的 `.local/research/BridgeCheck` 读取实际模组DLL，用手工构造状态检查协议／门控；不是完整游戏模拟或可移植CI套件 |
| C# 构建通过 | `scripts/build.ps1` 能编译打包；只证明编译，不证明游戏内行为 |
| 真实 API 通过 | 实际调用 DeepSeek 完成一个受限目标；不用 Mock 响应代替 |
| 游戏待验 | 需要启动 tModLoader、进入世界、真人操作或失焦才能确认 |

离线通过不等于功能交付；没有游戏待验结论的条目不能写成“已在游戏中可用”。

## 多人批次1–5

| 批次 | 已实现与证据 | 边界 |
| --- | --- | --- |
| 1 只读桥／Probe | 服务器仅 Server(2) 消费、只接受 status；Host&Play 2026-11-13 通过；主菜单不消费多人请求 | 远程独立服务器未验；过期快照与 `agentMode` 不匹配会被 Python 拒绝 |
| 2 中文短目标 | `/tpamp goal` 由生成者控制；真实模型左右短移动、替换、stop、自动 A→B→stop 通过；60tick输入不恢复 | 仅左右移动和停止，每次≤3秒；第二真人、真实断线重连未验 |
| 3 初始装备／持有物显示 | 服务器 Spawn 按普通经典三件初始化一次；`Snapshot` 可观察 inventory／selectedSlot／item；客户端包尾只同步选中槽；Stop／目标替换／status／重生不重复发放；初始化失败不发布 AI | 离线实现，未实机；客户端只显示一件，不是完整背包同步；单人历史流程仍用 `equip` 开发物资 |
| 4 铜短剑手动攻击 | `/tpamp attack left/right [1]`；服务器校验控制者、AI 槽位、铜短剑和时长；原生 `ItemCheck_Shoot` 射弹；`attack-input`／`projectile-hit` 有界记录 | 仅铜短剑、仅手动、1秒；未实机；不开放 Python attack、自动索敌或其他武器 |
| 5 普通近战挥砍 | `/tpamp equip iron` 隔离测试物资（需服务端 `TPA_MP_TEST_EQUIP=1`）；Iron Broadsword 经原生直接近战门控；`item-hit` 记录；复用批次4的输入、停止和身份保护 | 仅 Iron Broadsword、仅手动、1秒；未实机；默认关闭测试夹具；dedServ 近战框与原生命中触发待验 |

原生依据已收拢到 [游戏机制](game-mechanics.md)：批次3经典三件经 `GetStartingItems`／`SetStartInventory`；批次4 Copper Shortsword 的 Rapier／`CopperShortswordStab` 射弹；批次5 Iron Broadsword 的直接近战链。这里只保留验收结论，不重复调用链细节。

## 显示同步

| 条目 | 离线通过 | C# 构建通过 | 真实 API 通过 | 游戏待验 |
| --- | --- | --- | --- | --- |
| 快照序号／服务器 tick／接收时间／校正量诊断，有界记录 | 是 | 是（仅编译） | 不涉及 | `/tpamp diag` 的真实读数 |
| 权威状态与显示状态分离；乱序丢弃、停止收敛、瞬移重置、丢包 stale | 是（110项回放） | 是 | 不涉及 | 普通移动平滑／停止用户确认通过；断流／瞬移仍待实测 |
| stale 后恢复：限速收敛、不瞬间回退、连续断线有界 | 是（110项回放） | 是（仅编译） | 不涉及 | 恢复瞬间的观感 |
| 瞬移／重生识别：只依据服务器显式标记，断流不被误判 | 是（110项回放） | 是（仅编译） | 不涉及 | 真实重生／传送的位置变化 |
| 移除／重建／退世界清理显示缓冲 | 是（回放 + 重置路径） | 是 | 不涉及 | 本轮移除重建／重进无残留通过；第二客户端待验 |
| 持有物包字段（slot／type／prefix／stack） | 是（37项回放） | 是 | 不涉及 | AI 手持显示、换槽与镜像重建 |

## 单人历史基线（保留范围）

单人 P0（中断与安全恢复）、P1（常驻宿主／`/tpa goal`）、P2（动态切换）的逐项离线／编译／真实 API 结论及失败历史已移至 [阶段报告](milestone-acceptance.md) 与 [验证记录](validation.md)，本页不再复制表格。

- `/tpa` 命令、`/tpa goal`、攻击／挖掘／goto／home 只适用于单人；多人未开放这些命令，不能作为多人产品验收。
- 多人只使用 `/tpamp spawn|goal|stop|left|right|attack|equip|remove`；手动动作已有左右移动、停止、铜短剑短攻击和隔离测试物资的 Iron Broadsword 挥砍，自动攻击／挖掘仍未开放。
- 失焦后台持续运行仍属单人未解决项；不能用它推断多人服务器行为。

## 常用命令

```powershell
# Python 离线检查（先准备可写目录，避免文件协议检查被跳过）
New-Item -ItemType Directory -Force .local/test-mailbox | Out-Null
$env:TPA_TEST_MAILBOX = "$PWD\.local\test-mailbox"
python -B -m unittest discover -s agent -p test_run.py -v

# 批次3／4／5离线回放
dotnet run --project tools/InventorySync.SelfCheck -c Release
dotnet run --project tools/DisplaySync.SelfCheck -c Release
dotnet run --project tools/Combat.SelfCheck -c Release

# C# 构建（路径换成自己的 tModLoader 安装目录）
.\scripts\build.ps1 -TmlPath "$PWD\.local\tml\2026.08.3.0"

# 真实 API（需要 .env 里的 TPA_API_KEY）
.\scripts\run-agent.ps1 -CheckApi
```

## 文件协议（宿主）

| 文件 | 方向 | 字段 |
| --- | --- | --- |
| `goal.json` | 游戏 → 宿主 | `id`、`text`、`stopRevision`、`worldSession`；宿主读取后立即删除，不重放 |
| `host-ready.json` | 宿主 → 游戏 | `pid`、`started`、`beat`；独立心跳每 5 秒刷新（执行任务和等待 API 期间同样刷新），过期 15 秒视为不在线；宿主正常退出时删除 |
| `goal-ack-<id>-<status>.json` | 宿主 → 游戏 | `id`、`status`（accepted／rejected／duplicate／stale）、`reason` |
| `goal-result-<id>.json` | 宿主 → 游戏 | `id`、`status`（finished／budget_exhausted／interrupted／unknown／failed）、`summary`、`turns` |

确认文件按状态各写一个：Windows 上覆盖另一个进程正打开的文件会失败，和桥接层 `response-<id>.json` 的处理一致。

## 边界

- FakeGame 与临时邮箱只证明状态机与文件协议，不证明游戏线程真的按这个节奏消费文件。
- `finished` 只表示模型正常结束，不表示目标事实达成；证据仍要看动作结果和游戏状态。
- 抢占延迟：宿主在动作等待或 API 轮询中约 0.1–0.2 秒发现版本变化；卡在单次游戏请求时最长 5 秒。
- 暂停时提交目标会被拒绝并提示恢复；不会替玩家排队等待。
- 本清单不含近敌反应、拾取、合成等后续玩法；这些尚未开始，范围见 [TODO](../TODO.md)。
