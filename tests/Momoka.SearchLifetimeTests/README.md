# PERF-01 / PERF-12：搜索访问生命周期回归

这是独立离线定向项目，不使用主测试跑器、`SelfCheckRun`、用户互斥锁或真实桌面。
本项目的 `bin/obj` 属于本项目；为复用 Core 已有 friend assembly，输出程序集名为
`Momoka.Tests.dll`，**不是** `tests/Momoka.Tests` 的完整测试输出，不能互换路径。
项目、solution、regression 清单及 source pins 的统一注册由父集成代理完成。

## 执行的生产代码

- 完整编译链接 `PageViewModel.cs`、`MoviePilotSearchViewModel.cs`（含真实结果行）、
  `MoviePilotSearchPanel.xaml.cs`、`FnNasMatchDialog.xaml.cs`，使用 Shell 同版的
  CommunityToolkit.Mvvm 8.4.2 源生成器和真实 Core project reference。
- 每次编译从当前 `ItemCommands.FnNas.cs` 提取完整 `FnNasMatchAsync`，从
  `ItemCommands.Server.cs` 原样提取 `GuardAsync/AskAsync/TellAsync/Root`。
  真正的只读搜索取消和确认后独立写入走真实 Core session、scope、client、FnNas transport；
  只替换 HTTP endpoint，禁止网络备援。
- 每次编译从当前 `LibraryPage.xaml.cs` 原样提取 `Release/OnNavigatedFrom/SearchSource/ApplySource`。
  除 MoviePilot panel/VM 外的页面控制器是惰性边界，但接线控制流不抄写、不重新模拟。
- 提取的 start/end 锚点必须存在且唯一，否则直接编译失败；生成文件在本项目 `obj` 下。
  `FnNasMatchDialog.xaml` 及完整 LibraryPage 源码作为资源保留。

## 参数边界

只接受以下两种输入，**严格区分大小写**：

| 参数 | 行为 | 退出码 |
| --- | --- | --- |
| 无参数 | 运行全部离线定向回归；不选择外部 DLL、账号、目录或网络来源 | 通过 0，失败 1 |
| 单独 `--help` | 只显示帮助；不注册测试、不构造夹具 | 0 |
| 其余、重复帮助、帮助夹带其他参数、空参数、路径/URL/token/UI/播放模式 | 拒绝，不回显传入值，不构造夹具 | 2 |

没有 `--shell-dll`、`--source`、`--token`、`--ui`、`--self-check` 或播放入口。
每个用例有 10 秒失败上限，异步队列条件有受控等待上限。

## 本机命令

在当前独立工作树根目录运行；以下绝对路径是本修复树，集成后换成整合树自己的路径。
必须先成功编译当前 Release Core/项目，0 警告、0 错误后才能执行该树刚产出的 DLL；
不以旧输出或 `--no-build` 代替本轮编译。

```powershell
$dotnet = 'C:\Users\89400\.dotnet\dotnet.exe'
$root = 'C:\Users\89400\.codex\worktrees\perf-page-lifetimes\Momoka'
$project = "$root\tests\Momoka.SearchLifetimeTests\Momoka.SearchLifetimeTests.csproj"
$dll = "$root\tests\Momoka.SearchLifetimeTests\bin\Release\net10.0\Momoka.Tests.dll"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
& $dotnet restore $project -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
if ($LASTEXITCODE -ne 0) { throw '当前依赖还原失败，不执行旧输出' }
& $dotnet build $project -c Release -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
if ($LASTEXITCODE -ne 0) { throw '当前 Release 编译失败，不执行旧输出' }
& $dotnet $dll
if ($LASTEXITCODE -ne 0) { throw '定向回归失败' }
& $dotnet $dll --help
if ($LASTEXITCODE -ne 0) { throw '帮助分支失败' }
& $dotnet $dll --unknown-mode
if ($LASTEXITCODE -ne 2) { throw '未知参数必须在构造夹具前拒绝' }
```

Shell 的完整编译另外执行当前树 `src/Momoka.Shell/Momoka.Shell.csproj`，使用相同 Release/单节点标志；
本项目没有渲染 Shell。不得并行运行完整 Core、自检或 UI 去争用父代理的桌面验证。

## 回归范围

- 重复 Attach 订阅不累加；Release 幂等退订；永久 Dispose 不重启；真实 LibraryPage 离页接线与
  同页 CancelSearch 区分；返回/恢复不自动搜索，显式提交才能读取。
- WeakReference 覆盖 50 次面板/VM/行/图片/载荷/页面回调回收；旧 UI 队列故意保留也不成为根。
  即使 VM 自己仍被外部保留，Release 也必须解除确认、资源、季选择、媒体页面闭包与图片事件。
- 旧事件委托、旧连接、旧请求、旧 success/error/finally、同身份操作锁尾部以及 Busy/Ready/Heading
  同步通知重入，不得覆盖新访问/新查询。
- 飞牛关闭令牌到达真实 Core/FnNas/假 HTTP；非合作读取仍 pending 时取消 UI 等待并回收对话框；
  同实例关闭重开、改词显式替代、相同请求防重复、network-complete 但 UI 仍排队等归属。
- 新查询清旧候选/选择；Primary 关闭保留已选候选，重开清旧选择；旧错误静默但当前失败真实反馈。
  确认后 `Rescrap` 写入独立于搜索令牌，关闭不取消/重放这笔写入。
- 跑器参数正负分支以及本 README 的关键边界/可用命令一致性。

## 不可宣称的覆盖

- WinUI/XAML 控件、图片解码/下载、XamlRoot、Frame 历史栈、焦点/滚动和原生窗口均为值边界；
  WeakReference 证明托管引用根，不证明 WinRT 图片内存曲线、GPU 资源或真实 Frame 回收。
  这些证据由父代理的 `ShellNavigationProbe.Performance.cs` 在共享桌面串行取得。
- 匿名身份直接装入内存，没有真实登录、设置 Load/Save、DPAPI、系统凭据、实际 HTTP/socket
  或真实媒体操作。假 HTTP 中的写请求只证明确认路径/次数/取消边界，不证明真实写操作成功。
- `FnNasMatchAsync` 显示对话框之前的文件读取仍保持现有命令身份生命周期；它不由尚未显示的
  对话框取消。此修复不扩大为所有前置读取、其他弹窗或全项目资源审查。
- 内部两参数 FnNasMatchDialog 构造函数仅兼容既有匿名 UI 取景探针；生产 ItemCommands 与父性能
  探针必须使用三参数带 CancellationToken 的委托。非合作读取的 WaitAsync 取消只结束 UI 等待，
  不声称停止了不合作的服务任务；迟到输出仍无权更新 UI。
- 这是 Momoka 本地生命周期修复，不改 MoviePilot/FnNas 请求协议，不宣称与某个部署版本同步。
  既有上游核查失败日志保留，不反复穷举上游业务或拿历史 revision 当当前源码。
