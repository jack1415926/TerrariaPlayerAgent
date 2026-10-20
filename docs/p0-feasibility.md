# P0：单客户端 Player 方案调查

调查日期：2026-10-20。状态：源码调查完成，尚未编译或进入游戏验证。

## 结论

**工程判断：单客户端路线有具体源码依据，值得做最小原型；完整玩家机制、正常资源规则与本机兼容性仍未通过验收。**

“继承 Player”“参与玩家数组”“完整遵循普通玩家规则”“作为独立网络玩家登录”是不同的条件。前两项不能自动证明后两项。用户当前优先单窗口、独立生命值与物品，不必先实现第二个网络连接。

## 调查对象与版本

| 对象 | 本轮证据 | 用途判断 |
| --- | --- | --- |
| [TerraGuardians](https://github.com/nakano15/terraguardians) | 已下载并检查源码，提交 `d01ce34fc1d0df926ee8b5b53b20dcb3e2621805` | 优先研究玩家实体生命周期与全局上下文处理 |
| [TerraBlind](https://github.com/Reisenbug/TerraBlind) | 已下载并检查源码，提交 `26361f488da986ee8739dded76fabedfa04f6aa8` | 后续参考动作执行、超时与导航；不能直接作为第二玩家底座 |
| [MoreAIPlayer](https://steamcommunity.com/sharedfiles/filedetails/?id=3772048315) | 已阅读作者 Workshop 介绍；定向搜索未找到可核实的公开源码入口 | 作为体验与技术路线线索，尚不作为可复用源码依赖 |

版本以以上提交为准，避免后续上游改动造成行号和结论漂移。当前调查不是对三个项目的全面审计。

## TerraGuardians：已确认的实现

固定版本源码链接：

- [Companion.cs 第 19 行](https://github.com/nakano15/terraguardians/blob/d01ce34fc1d0df926ee8b5b53b20dcb3e2621805/Companion.cs#L19)：`Companion : Player`，确实是 Player 派生类。
- [SystemMod.cs 第 101 行](https://github.com/nakano15/terraguardians/blob/d01ce34fc1d0df926ee8b5b53b20dcb3e2621805/SystemMod.cs#L101)：备份玩家数组，将伙伴临时放入槽位，设置 `whoAmI`，之后恢复。
- [SystemMod.cs 第 208 行](https://github.com/nakano15/terraguardians/blob/d01ce34fc1d0df926ee8b5b53b20dcb3e2621805/SystemMod.cs#L208)：显式更新活动伙伴，随后还原玩家数组。
- [CompanionPlayerMaskScripts.cs](https://github.com/nakano15/terraguardians/blob/d01ce34fc1d0df926ee8b5b53b20dcb3e2621805/CompanionPlayerMaskScripts.cs)：`UpdateCompanionVersion`、`InnerUpdate` 维护专用更新流程，切换 `Main.myPlayer` 与鼠标上下文；`UpdateItem` 调用 `ItemCheck`；`UpdateDead` 在计时结束后调用 `Spawn`。这不是简单创建一个 Player 就自动完成的功能。
- [CompanionData.cs 第 432 行](https://github.com/nakano15/terraguardians/blob/d01ce34fc1d0df926ee8b5b53b20dcb3e2621805/CompanionData.cs#L432)：保存物品和装备，第 515 行开始加载对应数据。
- [PlayerMod.cs 第 2687 行](https://github.com/nakano15/terraguardians/blob/d01ce34fc1d0df926ee8b5b53b20dcb3e2621805/PlayerMod.cs#L2687)：`SaveData` 调用伙伴数据保存；`LoadData` 恢复伙伴数据，存在实际调用入口。
- [build.txt](https://github.com/nakano15/terraguardians/blob/d01ce34fc1d0df926ee8b5b53b20dcb3e2621805/build.txt)：声明依赖 `nterrautils`，不是可直接抽走一个文件的独立组件。

工程推断：可参考其生命周期组织方式；但专用更新、槽位映射、全局输入和弹幕归属都是兼容风险点。项目自身也有击倒等伙伴规则，不能将其默认玩法等同于本需求的普通玩家死亡规则。

本轮未验证：原生所有武器、第三方装备效果、耗材消耗、敌人仇恨、异常退出后的数据可靠性、多人同步。

## TerraBlind：适合参考控制，不适合直接替代角色层

- [Core/ActExecutor.cs 第 133 行附近](https://github.com/Reisenbug/TerraBlind/blob/26361f488da986ee8739dded76fabedfa04f6aa8/Core/ActExecutor.cs#L133)：`ApplyControls` 取得 `Main.LocalPlayer`，写入 `controlLeft`、`controlRight`、`controlJump` 和 `controlUseItem`，同时维护超时。这条路径控制的是本地玩家，不是独立伙伴。
- [Infra/HttpServerSystem.cs](https://github.com/Reisenbug/TerraBlind/blob/26361f488da986ee8739dded76fabedfa04f6aa8/Infra/HttpServerSystem.cs)：多个接口直接读取本地玩家。接入第二角色需检查整个调用链，不能只换一个入口参数。
- [Core/Concessions.cs 第 15 行及第 240 行附近](https://github.com/Reisenbug/TerraBlind/blob/26361f488da986ee8739dded76fabedfa04f6aa8/Core/Concessions.cs#L15)：定义木材保有量并自动补足；也有直接清除岩浆的代码。直接整体复用会违反正常资源与环境规则。
- [README 的 License 段](https://github.com/Reisenbug/TerraBlind/blob/26361f488da986ee8739dded76fabedfa04f6aa8/README.md#L206)：写有 MIT；实际引入前需核对相关文件声明，并保留适用的许可与版权信息。

工程推断：可借鉴持续动作、失败反馈、寻路分层；不应把其流程成绩直接当成本项目“不作弊通关”的能力证据。

## MoreAIPlayer：作者声明，尚无源码确认

作者描述声称：单人世界中将伙伴注册到真实玩家槽位，支持独立装备、移动和死亡复活；同时有默认弹药补偿、传送恢复等行为。即使体验符合预期，这些规则也需与本项目约束重新对齐。[作者说明](https://steamcommunity.com/sharedfiles/filedetails/?id=3772048315)

本轮没有取得源码或核实其许可，也未安装运行。未找到源码不等于证明不存在源码；目前不能把它列为已确认的开源底座。

## 本机环境（只读确认）

- 游戏目录：`F:\SteamLibrary\steamapps\common\tModLoader`。
- `tModLoader.dll` 文件版本：`1.4.4.9`；产品版本包含 `2026.08.3.0`、`stable`。
- `dotnet` 未在本次 shell 的 PATH 中解析到。尚未检查游戏自带运行时、其他 SDK 安装位置及编译工具，不据此断定未安装 SDK。
- 未启动游戏，未修改模组列表、存档、Steam 订阅或游戏安装目录。

## 建议的首个原型：只验证第二个 Player 的关键机制

这是一份实施建议，尚未落实到代码。暂不绑定 TerraGuardians，也不预先引入 Python、LLM、HTTP 或 MCP。

优先尝试一个独立的 C# 测试模组：在单人测试世界内管理一个额外 Player，研究最小的生命周期、更新和输入上下文适配。参考 TerraGuardians 的处理点，不照搬其完整伙伴系统。若最小路线需要重写大量原生更新代码，则回到决策点，比较依赖现有伙伴模组与独立客户端的代价。

| 检查 | 通过条件与记录 |
| --- | --- |
| 身份和更新 | 人类与 AI 各自有独立对象、槽位/映射、位置和状态；无重复更新；退出后释放 |
| 双方输入 | AI 执行有限时长的左右移动和跳跃；人类同时正常操作，鼠标、选中物品和输入不被污染 |
| 装备生效 | 对比穿戴前后防御与移动效果，记录变化；不是只显示装备图标 |
| 武器与归属 | 先测试一把原生近战武器，再测试一种消耗弹药的远程武器；伤害和弹幕归属正确，耗材按规则减少，人类背包不被改动 |
| 受伤与复活 | 实际敌人/环境造成伤害并触发死亡；按选定普通难度规则复活，不混用击倒或无敌来代替 |
| 保存 | 手动交给 AI 的物品数量、装备与身份在正常退出重进后恢复；无复制或丢失 |
| 停止与清理 | 停止命令在下一次控制更新清除移动和持续使用意图；退出世界和异常路径恢复全部临时全局状态；停止不是无敌或免伤 |

只在独立测试角色和世界验证，不对现有存档做自动迁移。原型测试物品来自玩家手动转交；若使用测试注入，必须标明，不能用作正常获取资源的验收证据。

每次实验记录 tModLoader 版本、模组版本、复现步骤、预期/实际、日志和结果（通过/失败/未测）。编译通过仅证明构建成功，不能代替游戏内验收。

决策条件：上述关键机制通过后再进入 P1 跟随与史莱姆战斗；若卡在只能通过大量规则替代才能工作的机制上，提交具体失败证据，再比较两种备选路线。当前没有切换双客户端的技术结论。
