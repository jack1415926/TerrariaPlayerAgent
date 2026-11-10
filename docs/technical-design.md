# 当前技术设计与实现

更新：2026-10-22。本文描述工作区实现；验收结论以 [阶段报告](milestone-acceptance.md) 和 [验证记录](validation.md) 为准。

## 基座与职责

C# 12／.NET 8的独立tModLoader模组负责角色和原生连续控制；Python标准库Agent负责HTTPS模型请求与任务循环。SDK由global.json固定，本地配置为`.local/tModLoader`。无第三方伙伴模组、数据库、寻路或Agent框架依赖。

| 文件 | 当前职责 |
| --- | --- |
| Common/DemoPlayerSystem.cs | 独立Player槽位、生命周期、输入、跨坎／恢复、库存与敌怪观察、战斗事件；同文件DemoUpdateProbe补接触伤害并计更新数 |
| Common/GroundNavigation.cs | 水平目标、home／活动边界、刹停判断、目标状态和sequence；不计算完整路径 |
| Common/LocalTerrain.cs | 局部碰撞探测和17×11格观察 |
| Common/NativeMining.cs | 普通挥动镐原生接入、范围／保护条件、候选、原生PickTile前后事件和取消 |
| Common/NativeCombat.cs | 起手／近战／致死归属检查的有限适配，以及软核自动重生 |
| Commands/DemoCommand.cs | /tpa参数、单人限制、明确接受或拒绝；操作真人与受控AI分离；`/tpa goal`把目标文本交给宿主邮箱 |
| Common/LocalTestBridge.cs | -tpatest下的文件请求、停止优先通道、版本检查与状态；`/tpa goal`先RequestStop抢占再发布goal，按id过滤过期结果；复用命令层，不开网络端口 |
| Common/WorldSession.cs | 世界会话标识；进世界生成、退出清空，恢复对账据此拒绝跨世界恢复 |
| Common/NativeChatInput.cs、EnterDiagnostics.cs | 开发输入诊断及固定聊天探针，不是任意文本输入工具 |
| agent/run.py、agent/prompts/ | 模型工具白名单、顺序等待、实际结果判定、取消、有限预算、报告与能力／目标提示 |
| scripts/ | 构建、独立启动、请求入口和相关检查；不负责给已加载程序集热更新 |

## 角色与更新

spawn在真人附近查找无实心碰撞位置，创建有独立生命／背包的Player并放入空闲槽位，保存槽位原对象。除生成／原生重生外不直接设置位置；引擎负责更新和绘制，不再调用第二次Player.Update，不切换Main.myPlayer。

PreUpdatePlayers先清除旧输入，再施加本次移动、跳跃、向下或物品使用意图。时长按60ticks/模拟秒换算，暂停不消耗模拟时间。普通手动新动作替换旧动作；普通jump保留水平移动，方向jump组合移动与跳跃。

低矮障碍检查上移1–5格的空间并自动施加跳跃；持续无进展时最多两次恢复跳跃，仍失败给明确blockedReason。原生重力／碰撞决定实际能否通过，不保证高墙、低顶、平台和洞穴通行。

stop取消主动输入，不强制速度归零或冻结动画。remove／退出世界恢复仍由AI占据的槽位，清除范围和角色；不持久化AI。死亡清旧动作，软核AI原生倒计时到零后，有限补调FindSpawn、CheckSpawn、Spawn(ReviveFromDeath)，保留同一对象和背包；remove取消该对象的重生资格。

## 目标移动与边界

GroundNavigation只管水平世界像素X（actor.x），不接收方块列号。goto／home默认最多10秒，误差8像素内、落地且低速才arrived。依据当前水平速度与runSlowdown提前释放输入，利用原生摩擦减速；受阻、超时、取消与到达分开，本次sequence防止旧结果被误用。

range 1–100格以当前AI的X建立home和左右边界，off解除；设置会取消旧任务并递增停止版本。旧定时移动和方向跑跳也受本地边界约束，范围外目标拒绝；普通stop不清范围，移除／退出清除。模型白名单不开放range；探索模板由宿主在开始时建立25格范围。

这是主动输入边界，不是物理墙。击退／重生可能出界，outsideBounds反映实际状态，向内返回仍允许。到达后不持续守位；总结时世界还在变化，不能用历史arrived替代最新位置。

## 原生物品与结果

战斗复用原生使用、命中和伤害，有限适配针对本机tML 2026.08.3.0；Actor的原生本地交互段被跳过，因此DemoUpdateProbe为其补调一次Update_NPCCollision。没有复制伤害公式、命中框或直接扣血。

挖掘走ItemCheck_Inner → ItemCheck_OwnerOnlyCode → ItemCheck_UseMiningTools → PickTile；仅在本AI挖掘激活时放行必要归属分支。tileTargetX/Y是共享字段，范围检查和挖掘调用内短暂替换、finally恢复；不动真人鼠标。复用原生冷却、强度与方块规则，CanKillTile=false的支撑等目标不推荐且立即拒绝。

原生PickTile前后记录坐标、工具、类型／存在性和broken。草地变泥土不算移除，移除不算拾取。库存可观察并选择；equip wood／iron／pick只在开发模式发测试物资，普通初始装备尚未接入。木剑／铁阔剑攻击已适配；普通挥动镐可用，斧／锤／钻头等未交付。

## Agent与开发入口

Python通过request.json及唯一response-id.json与游戏线程交换；stop.json独立优先处理。stopRevision变化取消旧任务，后续动作带expectedStopRevision，过期时不执行；旧任务清理也不能覆盖新任务。

API请求在网络线程运行，游戏请求由宿主串行处理。等待API时仍检查取消；迟到响应不继续动作，但不承诺服务端请求被取消或不计费。工具仅开放受限命令，不接受任意shell或代码。

game_actions逐条等输入／当前动画结束，再下发后续动作，按tick而非仅墙钟等待。被拒／受阻等情况停止本批后续动作；goto／home和挖掘各核对本次实际证据。自定义自然语言目标没有通用全游戏自动验收器；model_finished仅表示模型正常结束，任务目标仍需事实核对。

游戏失焦时本机入口／世界停止响应。宿主区分明确暂停、tick冻结、请求未执行、结果未知与主动取消；挂起后轮询status，核对stopRevision、世界、AI身份及仍在执行的动作，仍在执行时先发stop接管再结束，不重放原命令；未获stop确认则保留stop_error。后台持续模拟仍未实现，恢复对账只做了离线检查，未实机验证。

## 观察与边界

观察包括AI／真人位置、生命、输入、工具、库存、导航、局部地形／矿物候选、附近480像素最近8个敌怪和有界事件。敌怪位置会过时、slot可能复用，列表不覆盖所有弹幕；候选canKill=true也不保证工具强度足够。

当前单人、一个AI、有限轮次原型；没有完整寻路、持续跟随／守位、可靠拾取、合成、持久化或多人同步。反编译调查只留被忽略的.local/research，不提交游戏源码／二进制；本项目新增代码使用MIT。
