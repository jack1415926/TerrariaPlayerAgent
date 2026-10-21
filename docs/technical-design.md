# 首批技术选型与实现说明

更新：2026-10-21。本文件描述当前代码，不代表全部游戏内验收已通过。

## 技术选择

| 项目 | 当前选择 | 理由 |
| --- | --- | --- |
| 基座 | 独立 tModLoader 模组 | 用户已选定；不依赖第三方伙伴模组和其玩法规则 |
| 语言与框架 | C# 12 / .NET 8 | 与本机 tML 的 tMLMod.targets 一致；SDK 使用 global.json 固定 8.0.426，允许同一主版本内更新 feature band |
| 角色 | 独立 Terraria.Player 对象 | 可直接观察原生角色更新的能力与限制 |
| 调度 | ModSystem + 原生玩家循环 | 使用 PreUpdatePlayers 写控制；额外槽位由引擎更新、绘制，不手动再调用 Update |
| 输入 | controlLeft / controlRight / controlJump | 让原生速度、重力、碰撞负责动作，不持续修改坐标 |
| 操作入口 | ModCommand 聊天命令 /tpa | 第一批人工操作简单，后续可在命令之外接入 Agent |
| 诊断 | ModPlayer.PreUpdate 计数、/tpa status、tML 日志 | 证明角色是独立对象，并检查更新次数；ModPlayer 本身不创建角色 |
| 构建 | 本机 SDK + 安装目录的 tMLMod.targets | 直接从 F 盘仓库构建，不复制源码到 ModSources，不引入额外 NuGet 包 |
| 本地运行 | 项目 .local/tModLoader 独立配置 | 通过 tML 原生 -tmlsavedirectory 参数隔离本地测试配置、角色、世界和模组列表 |

现阶段不加入 Python、LLM、MCP、HTTP、数据库、寻路框架或大型测试框架。

## 运行流程

1. `/tpa spawn` 选择一个空闲玩家槽位，并在发令玩家旁边查找没有实心碰撞的位置。初始化一个有独立状态、空背包的 Player，保存并替换该槽位原对象。
2. 移动命令记录方向和剩余游戏更新次数；跳跃命令施加 12 次更新的跳跃输入；新的动作替换旧动作。
   移动中增加局部障碍探测：着地且前方 16 像素处的角色碰撞框受阻时，检查上移 1–4 格后的落脚空间及当前头顶的竖直通路，满足条件则保留移动方向并施加同样的 12 次跳跃输入。速度、实际起跳和碰撞仍由原生 Player 更新决定，不直接抬高坐标。
3. `PreUpdatePlayers` 每次清除旧控制再施加本次意图。单人引擎随后更新所有活动玩家；角色使用原生绘制流程。
4. `DemoUpdateProbe.PreUpdate` 只对演示对象计数。`PostUpdatePlayers` 记录本次计数，供状态命令显示；正常应为 1。
5. 停止立即清除控制意图。移除、退出世界和卸载清理角色；仅当槽位仍属于本角色时恢复备份，避免覆盖其他对象。

不改 Main.myPlayer，不将人类键盘复制给 AI，不为通过演示修改世界方块。开发命令可发测试武器和生成原生怪物；位置只在初次生成时设置，后续移动由引擎计算。

## 代码职责与调试位置

- `Common/DemoPlayerSystem.cs`：角色与动作状态、原生循环接入、生成/清理；同文件的 DemoUpdateProbe 提供计数。首查 Spawn、PreUpdatePlayers、Remove。
- `Commands/DemoCommand.cs`：手动指令、参数与单人模式检查，发令者与受控角色分离。首查 Action。
- `scripts/`：build.ps1 编译打包；play.ps1 启动本项目测试配置。

停止是取消主动输入，不会将速度归零或停止重力。动作时长按每秒 60 次模拟更新换算；暂停时不消耗模拟时间。每次手动查询 status 都能观察位置、速度、动作剩余次数和累计更新数。

## 当前限制

- 单人演示，最多一个角色；多人命令明确拒绝，尚无服务器权威或网络同步。
- 不是完整玩家兼容实现：原生 Player.Update 中有大量仅对 Main.myPlayer 生效的路径。目前覆盖木剑、铁阔剑、NPC 接触伤害、死亡及普通／软核自动重生；拾取、投射物伤害、环境伤害和完整装备效果未完成验证。
- 不持久化 AI 的物品或状态。退出后需要重新生成；原型还没有物品交互，不应通过其他模组向它转交贵重物品。
- 没有跟随、自主追敌或寻路。移动时可自动尝试跳过低矮实心障碍，但高墙、低顶和复杂地形仍可能阻挡；当前攻击只在原地指定朝向挥剑。
- 第三方模组兼容未测。首次测试只有本项目，后续按需要扩充。

## 本轮证据

对本机 `tModLoader.dll`（Terraria 1.4.4.9 / tML 2026.08.3.0）只读查看：Main.DoUpdateInWorld 遍历活动玩家槽位调用 Player.Update；Main.RefreshPlayerDrawOrder 收集额外玩家；Player.Update 的人类输入读取限于本地玩家路径。构建目标指定 net8.0 和 C# 12。

反编译输出仅留在临时调查目录，不提交游戏源码或二进制。本项目没有复制 TerraGuardians、TerraBlind 或 MoreAIPlayer 的代码和资源；自己的新增代码使用 MIT。

## 0.1.1 测试入口补充

### 双向战斗适配

本机版本的 `Player.ItemCheck_Inner` 第一个 myPlayer 比较会跳过非本地槽位的起手逻辑；`ItemCheck_OwnerOnlyCode` 第一个 myPlayer 比较在近战命中段前返回；`Hurt(HurtInfo, bool)` 最后一个 myPlayer 比较阻止非本地槽位调用原生 KillMe。`Common/NativeCombat.cs` 用 tML 自带 MonoMod IL 钩子，仅在这些比较处将本演示角色视为归属者，武器两处仅对白名单木剑和铁阔剑生效，其他玩家保持原行为。没有改写全局 Main.myPlayer，也没有复制原生伤害公式、命中框或直接扣血。

`Player.Update` 的本地交互段包含 `Update_NPCCollision`。AI 槽位原生跳过该段，因此在 `DemoUpdateProbe.PostUpdate` 为存活的 AI 补调一次此方法，让原生碰撞、伤害计算、免伤时间、击退、Hurt 及 KillMe 生效；不再调用 Player.Update。死亡钩子清除控制。

自动重生沿用同文件 `NativeCombat.UpdateDead` 的方法钩子：先让原生 UpdateDead 执行，包括递减 respawnTimer；然后仅为本项目仍活动、死亡、计时归零的普通／软核 AI 补调 FindSpawn、CheckSpawn 和 Spawn(ReviveFromDeath)。这些方法保留背包并恢复生命、位置及出生保护，未创建新 Player，也没有切换 Main.myPlayer。原生 Spawn 保留自身的本地玩家检查，真人镜头不会被改成 AI 镜头。重生后清除控制并记录事件。

角色移除或退出世界时，沿用 Remove 清理并撤出玩家槽位；没有独立延迟任务或待重生队列，所以对象移除后不会再被重生钩子匹配。当前 AI 没有床出生点设置入口，默认使用世界出生点；不支持中核和硬核复活策略。

适配基于本机 tML 2026.08.3.0 的方法位置；升级后要重查，不能把它视为通用玩家兼容层。反编译调查仅留在被忽略的 `.local/research`，不提交游戏代码。

新增只在 -tpatest 开发启动参数下启用的 JSON 文件测试入口；复用 DemoCommand.Action 和原有控制层，不开网络端口。附带可关闭的 Enter 事件诊断，以及游戏内部的 SDL Enter 对照。具体协议、前台运行限制、自动测试步骤和故障证据见 testing.md。原有角色更新实现未改为手动 Update。
