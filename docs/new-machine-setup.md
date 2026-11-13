# Windows新电脑接续开发

目标是克隆源码后构建、启动独立测试配置并继续开发。仓库不包含游戏本体、SDK、API密钥、旧电脑的角色／世界或原始测试报告；这些需要在新电脑准备。

只做公司电脑离线开发时，先看第5节与[TODO](../TODO.md)：克隆源码后可先做Python／Mock工作，下方游戏安装、启动和凭据配置按实际需要准备，不是离线开工前置。

## 1. 安装依赖

- Git、PowerShell 7。
- .NET **8 SDK**，不是只有运行时。`global.json`指定8.0.426并允许兼容的较新8.0版本；只安装.NET 9／10不会替代所需的8 SDK。
- Python 3.12，本项目Agent只用标准库，不需要安装pip依赖。
- Steam、合法安装的Terraria和tModLoader；启动游戏时Steam须运行且已登录。
- 实机测试辅助：在tModLoader中安装或订阅 Cheat Sheet 和 HERO's Mod；`play.ps1`会将两者加入独立测试配置的启用列表。它们不随本仓库分发。

最近本机实机环境（2026-11-13）为 **Terraria 1.4.4.9／tModLoader 2026.9.3.1、.NET SDK 8.0.428**；下文2026.08.3.0 release zip是历史离线构建示例，不是当前多人实机版本声明。新机按实际版本检查原生归属钩子，不保证其他版本直接兼容。出现`owner branch missing`／`owner check missing`时先查版本，不是API密钥问题；游戏文件不提交仓库。

## 2. 克隆并构建

在PowerShell 7中运行（仓库放在哪个盘都可以）：

```powershell
git clone https://github.com/jack1415926/TerrariaPlayerAgent.git
Set-Location TerrariaPlayerAgent

# 改成新电脑的实际tModLoader目录，里面应有tMLMod.targets和tModLoader.dll。
$tml = 'C:\Program Files (x86)\Steam\steamapps\common\tModLoader'
.\scripts\build.ps1 -TmlPath $tml
.\scripts\play.ps1 -TmlPath $tml
```

不需要把源码复制到ModSources，也不需要将新电脑改成旧电脑的F盘路径。脚本默认路径是旧开发机约定，新机显式传`-TmlPath`即可；换一个PowerShell会话后需重新设置`$tml`或直接传目录。

### 没有Steam时：用GitHub release的zip只做构建

只编译、不进游戏的公司电脑或离线机器走这条路线；仍需要.NET 8 SDK。

1. 下载对应版本的`tModLoader.zip`，例如 [v2026.08.3.0](https://github.com/tModLoader/tModLoader/releases/tag/v2026.08.3.0)，约61MB。版本必须与验收版本一致；`tModLoader.dll`的ProductVersion应为`1.4.4.9+2026.08.3.0`。
2. 解压到`.local/tml/2026.08.3.0`，不要解压到仓库根目录：`build.txt`的`buildIgnore`不排除它，`.tmod`会把整个目录打进去（实测58MB，正常约88KB）。
3. zip不含Terraria资源和启动器，只能编译和打包，不能启动游戏；实机验收仍需Steam安装的游戏。
4. 受限账户下构建时，用进程级变量把写路径指到工作区，不需要管理员权限：

```powershell
$env:PATH = "C:\Program Files\dotnet;$env:PATH"
$env:DOTNET_CLI_HOME = "$PWD\.local\dotnet-home"
$env:NUGET_PACKAGES  = "$PWD\.local\nuget"
$env:APPDATA         = "$PWD\.local\appdata"
.\scripts\build.ps1 -TmlPath "$PWD\.local\tml\2026.08.3.0"
```

`APPDATA`一行用于读不到用户`NuGet.Config`的环境；不需要时可以省略。刚装完SDK后已打开的PowerShell可能还找不到`dotnet`，重开或直接用完整路径。产物在`.local/tModLoader/Mods/`。

如果GitHub网络不通，按新电脑实际代理配置处理，不盲抄旧机7890端口，也不设置全局Git代理。若PowerShell提示脚本策略阻止，按该电脑的脚本执行策略处理；项目不会自动更改系统策略。

## 3. 准备独立测试世界

首次启动在项目的`.local/tModLoader`中创建专用本地角色和普通小世界。不要选择Steam云列表里的正式存档。旧机的`t`／“放松苔原”没有上传，新机不需要同名或相同种子才能继续开发。

当前多人路线用 `play.ps1 -MultiplayerAgent -TmlPath '<本机安装目录>'` 启动，再由用户“开服并游玩”。启动默认加载 Cheat Sheet／HERO's Mod；先按本轮目的准备环境并记录实际开关，不为测试改成仅加载项目模组。多人只需 `/tpamp spawn`，不发测试装备，普通初始装备仍待开发。

以下为单人历史探索所需的开发物资准备，不能用于证明正常成长；单人进入世界后逐条输入：

```text
/tpa spawn
/tpa equip wood
/tpa equip pick
/tpa select 0
```

这是一套测试起始物资，不是普通角色默认装备。`play.ps1`已启用开发JSON入口；手动从Steam直接启动不一定有该入口。

若确实需要接着使用旧测试世界，可自行把旧机`.local/tModLoader/Players`和`Worlds`拷到新机对应目录；不要直接同步整份`.local`、旧的DLL／tmod或凭据。这一步可选，不是构建前置。

## 4. 配置自己的API并运行

仅首次缺少配置时复制模板：

```powershell
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
notepad .env
```

在本机`.env`填写`TPA_API_KEY`；其余TPA_变量按模板和实际服务配置。密钥通过可信方式私下准备，不能通过公开Git仓库转移。`.env`已被忽略，不要强制加入Git。

多人短目标运行方式（每目标最多3轮、宿主4目标后退出）：

```powershell
.\scripts\run-agent.ps1 -HostMode -AgentMode multiplayer_server -MaxTurns 3 -MaxGoals 4
```

生成者在游戏输入 `/tpamp goal <中文目标>`，用 `/tpamp stop` 取消。只读模式用 `play.ps1 -MultiplayerBridge` 和 `run-agent.ps1 -Probe -AgentMode multiplayer_server`。自动替换／停止验收须宿主剩余至少2目标额度、AI已生成且无活动目标，再运行 `scripts/test-multiplayer-goals.ps1`。不要把两个模式的停止命令或发物资入口混用。

以下是单人历史探索入口，保持游戏前台运行：

```powershell
.\scripts\run-agent.ps1 -GoalFile agent/prompts/explore.md -MaxTurns 12
```

游戏中`/tpa stop`或另一个PowerShell运行`.\scripts\run-agent.ps1 -Stop`可取消任务。失焦后台运行尚未解决，不能用“切回聊天后超时”认定新机安装失败；恢复焦点后先看状态。

## 5. 继续开发时的入口

先读README、[TODO](../TODO.md)、docs/technical-design.md和docs/development-experience.md；当前多人短目标、显示平滑和自动替换／停止已通过本轮一真人场景。下一小批是正常新角色起步装备与同步，随后接多人原生攻击。第二真人、长期稳定性和完整单人失焦恢复仍延后；71项Python检查与43项本机临时DLL检查的口径见集中清单，不能泛化为全玩法已完成。

**公司电脑只做离线开发时，不要求启动Steam或游戏、准备角色／世界或API密钥。** Python标准库即可运行现有检查：`python -B -m unittest discover -s agent -p test_run.py -v`；新增检查沿用本地Mock和临时目录。C#构建仍需.NET 8 SDK及匹配的tModLoader文件，没有这些依赖时将构建标为待验证。真实DeepSeek按需少量检查模拟状态下的工具调用，不以此证明游戏动作成功；详细分工和集中验收清单以TODO为准。

只改提示词、Python Agent或文档可复用运行中的游戏；改C#需重新构建并加载。打包遇到tmod占用按docs/debugging.md处理，不把旧代码画面当作新功能。新机首次只需确认构建、模组加载和一条实际动作能工作，不必先重跑全部历史检查。

这是新机安装说明，不是已经在另一台实体电脑验证通过的声明。此前已有干净源码目录使用显式游戏路径构建的检查；2026-11-10在离线机器用上面的release zip路线构建通过（0警告／0错误，产物约88KB），未启动游戏，也未做游戏内或API验收。游戏／API需要在实际验收电脑完成本地准备。
