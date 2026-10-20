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
3. `PreUpdatePlayers` 每次清除旧控制再施加本次意图。单人引擎随后更新所有活动玩家；角色使用原生绘制流程。
4. `DemoUpdateProbe.PreUpdate` 只对演示对象计数。`PostUpdatePlayers` 记录本次计数，供状态命令显示；正常应为 1。
5. 停止立即清除控制意图。移除、退出世界和卸载清理角色；仅当槽位仍属于本角色时恢复备份，避免覆盖其他对象。

不改 Main.myPlayer，不将人类键盘复制给 AI，不给 AI 装备或资源，不为通过演示修改世界方块。位置只在初次生成时设置；后续移动由引擎计算。

## 代码职责与调试位置

- `Common/DemoPlayerSystem.cs`：角色与动作状态、原生循环接入、生成/清理；同文件的 DemoUpdateProbe 提供计数。首查 Spawn、PreUpdatePlayers、Remove。
- `Commands/DemoCommand.cs`：手动指令、参数与单人模式检查，发令者与受控角色分离。首查 Action。
- `scripts/`：build.ps1 编译打包；play.ps1 启动本项目测试配置。

停止是取消主动输入，不会将速度归零或停止重力。动作时长按每秒 60 次模拟更新换算；暂停时不消耗模拟时间。每次手动查询 status 都能观察位置、速度、动作剩余次数和累计更新数。

## 当前限制

- 单人演示，最多一个角色；多人命令明确拒绝，尚无服务器权威或网络同步。
- 不是完整玩家兼容实现：原生 Player.Update 中有大量仅对 Main.myPlayer 生效的路径。移动成功不能证明受伤、武器、拾取、死亡或复活可用，不对这些功能作通过声明。
- 不持久化 AI 的物品或状态。退出后需要重新生成；原型还没有物品交互，不应通过其他模组向它转交贵重物品。
- 没有跟随、战斗、寻路或自动防卡。墙体和障碍可能阻挡移动，跳跃只适用于正常可起跳状态。
- 第三方模组兼容未测。首次测试只有本项目，后续按需要扩充。

## 本轮证据

对本机 `tModLoader.dll`（Terraria 1.4.4.9 / tML 2026.08.3.0）只读查看：Main.DoUpdateInWorld 遍历活动玩家槽位调用 Player.Update；Main.RefreshPlayerDrawOrder 收集额外玩家；Player.Update 的人类输入读取限于本地玩家路径。构建目标指定 net8.0 和 C# 12。

反编译输出仅留在临时调查目录，不提交游戏源码或二进制。本项目没有复制 TerraGuardians、TerraBlind 或 MoreAIPlayer 的代码和资源；自己的新增代码使用 MIT。
