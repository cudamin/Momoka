# R-019 Dashboard 导航生命周期夹具

独立题内正式项目；不进入主测试运行器或历史 records。来源代理未改方案，主树整合时已最小加入方案及开发入口。构建时用唯一源码边界原样提取当前
`src/Momoka.Shell/Views/DashboardPage.xaml.cs` 的生命周期字段/属性、路由/inline、完成/取样/刷新/Release
方法以及真实 `StartAsync` 的事件订阅语句。每个边界须恰好出现一次，源保持 LF；区域不改写算法，
生成件用 `#line` 指向真实源，并嵌入本轮页面 SHA-256，运行时复核，不能拿旧输出作当前证据。

这是**真实 page handler 控制流与 notice/Web 属性的离线验证，不是真 WebView2/WinUI UI 验证**。
WebView2 调用只在内存记录匿名 `.invalid` URL，脚本只暂停/返回边界结果，不执行 JavaScript 或
localStorage。WinUI 只替换显隐/文本值；单线程 queue 可在真实 async continuation 应用前挂起。
页面 Attach/Start/login/凭据注入不运行；源身份在全新的内存 EmbySession 安装，空令牌，秘密与
HTTP 边界任何调用直接失败。不开窗口，不读保存的账号/设置/凭据，不联网真实管理页/服务器，
不启动 App/native/播放，不取用户级 SelfCheck mutex。

## 本树命令

在本树根运行；每条源码变更后先成功编译当前测试，才能 `--no-build`。SDK/依赖保持仓库固定版本，
固定缓存还原只当次禁用 NuGetAudit，不改配置；MSBuild 严格 Release 单节点。运行日志留本树
`artifacts/r019/`，红测产品源码未改；主线程只整合题内源/测试/README并在自己的树重新还原/编译/测试。

```powershell
$root = (& git rev-parse --show-toplevel).Trim()
$sdk = 'C:\Users\89400\.dotnet\dotnet.exe'
$cache = 'C:\Users\89400\.nuget\packages'
$logs = Join-Path $root 'artifacts/r019'
$env:DOTNET_CLI_HOME = Join-Path $logs 'dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:NUGET_PACKAGES = $cache
$env:NUGET_HTTP_CACHE_PATH = Join-Path $logs 'nuget-http'
$env:NUGET_SCRATCH = Join-Path $logs 'nuget-scratch'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$project = Join-Path $root 'tests/Momoka.DashboardNavigation.Tests/Momoka.DashboardNavigation.Tests.csproj'
& $sdk restore (Join-Path $root 'src/Momoka.Shell/Momoka.Shell.csproj') --packages $cache --source $cache -p:NuGetAudit=false -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& $sdk restore $project --packages $cache --source $cache -p:NuGetAudit=false -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& $sdk build $project -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& $sdk run --project $project -c Release --no-build --no-restore
& $sdk build (Join-Path $root 'src/Momoka.Shell/Momoka.Shell.csproj') -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
```

## 定向空白与证据

只包含题内当前 9 份 C# 与 DashboardPage，不格式化引用的 Core、生成件或其他页面；所有当前文件须
UTF-8/LF、Git diff 检查通过。format 自身不支持 MSBuild `-m` 开关，使用 Release 环境与单树串行加载，
不把 exit 0 当作工作区已成功加载：日志里有 MSBuild 失败/缺引用即无效。Shell 的 x64 设计时加载
要求本树 x64 Core 输出，必须先用同样的严格构建参数生成，不能复制其他树 DLL。

```powershell
$env:Configuration = 'Release'
$env:BuildInParallel = 'false'
$env:UseSharedCompilation = 'false'
$env:MSBuildNodeReuse = 'false'
& $sdk build (Join-Path $root 'src/Momoka.Core/Momoka.Core.csproj') -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false -p:Platform=x64
$env:Platform = 'x64'
$page = Join-Path $root 'src/Momoka.Shell/Views/DashboardPage.xaml.cs'
& $sdk format whitespace (Join-Path $root 'src/Momoka.Shell/Momoka.Shell.csproj') --no-restore --verify-no-changes --include $page --verbosity diagnostic --report (Join-Path $logs 'format-shell-final.json')
$env:Platform = 'AnyCPU'
$files = @(Get-ChildItem -LiteralPath (Join-Path $root 'tests/Momoka.DashboardNavigation.Tests') -Filter '*.cs' -File | ForEach-Object FullName)
& $sdk format whitespace $project --no-restore --verify-no-changes --include $files --verbosity diagnostic --report (Join-Path $logs 'format-tests-final.json')
git diff --check
```

固定基点 `865176932a3ef599f75f93e22c6f6fc4ee8ba332` 未改产品源码时的有效红：48 项，15 过、33 败、
0 跳过、退出 1，源 SHA-256 `0e4d28861c22140ae9c7789b66c94138094ee30bc394525b9851e93f138e7605`。
`build-tests-red-final.log` 当前 Core/夹具 Release 0 警告/0 错误之后才运行 `run-tests-red-final.log`；
前置夹具编译错误及 fake endpoint 名称触发的 WUI4001 不充红数，修边界调用名而非压制诊断。
首次修复同 48 项全部绿，见 `run-tests-green-1.log`；最终格式/重新编译结果以本树 PROGRESS 顶部及
`artifacts/r019/` 的 final 日志为准，日志不入历史 records。新增项目只编译/运行本题，不运行全 Core
套件或任何 SelfCheck，计数不与主线程其他套件相加。

首次隔离 CLI home 的 SDK restore 欢迎输出包含开发证书安装提示；后续命令显式禁止生成。
未读取/清理用户证书存储，不能断言首次命令完全没有用户证书存储写入。主线程重验使用上方
`DOTNET_GENERATE_ASPNET_CERTIFICATE=false`，不为核验这条提示读取账号、凭据或证书。

## API 与覆盖边界

首次 restore 的 `src/Momoka.Shell/obj/project.assets.json` 指向已缓存 WebView2 `1.0.3719.77`；
本项目 `winapp find-api members CoreWebView2NavigationStartingEventArgs CoreWebView2NavigationCompletedEventArgs
--json --project-dir <本树 src/Momoka.Shell 绝对目录>` 与包 XML 确认两事件 `NavigationId` 都是只读 `UInt64`，
以及 Starting 的 `IsRedirected` / WinUI WebView2 的事件签名。事件重叠与 hash 不产生新 Starting 的行为
另核 Microsoft 官方 navigation-events 文档。没有依赖机器上更高版本 Runtime 的 metadata。

目标覆盖：A/B 完成两种顺序、旧失败/成功、真实 B 失败/取消、三路由、Starting 回来前的请求间隙、
UInt64 ID、当前成功清 notice、Refresh/Release/源撤销/默认源与指定源、新 page 重开、同文档 inline/hash、
inline 整页回退、脚本新文档/渲染等待与旧 async 回调、Sample 成功/异常/queue 和 watchdog 当前性。
真实引擎实际事件顺序/取消概率、页面像素/焦点/DOM/render、完整 Attach/启动、真实管理页面均未验证；
匿名 URL 是内存夹具，不是真 browser。
## 同 URL 尚未 Starting 的补修（追加在 9ce5d1 之后）

原提交 `9ce5d1fbde3fecaf2d06aeae93741d0856f3cae7` 的 48 项绿色不覆盖同 URL 请求认领歧义。
本次不改原提交/历史 records。在 `AutoStart=false` 下，真实 `TryShowRoute("users")` 请求 A、
真实 Refresh 请求同址 B、Start(A)、A canceled 先回，再 Start(B)/成功；Emby/Jellyfin 首次有效红
**50 项，48 过/2 败/0 跳过/退出 1**，两项都观察到 Web 被旧取消折叠。对应
`artifacts/r019/supplement/build-red-same-url.log` / `run-red-same-url.log`，产品源仍是原提交。

扩展未开始取消、三请求、UI queue、释放和真实 B 失败后，同一修前产品有效红
**68 项，51 过/17 败/0 跳过/退出 1**；初次补修同 68 项 **68 过/0 败/0 跳过/退出 0**。
见 `build-red-expanded.log` / `run-red-expanded.log` 和 `build-green-1.log` / `run-green-1.log`；
最后另外增加排队后真正 Navigate 抛错/刷新恢复一项，共69项；当前输出/格式以 supplement 的 final
日志和 PROGRESS 顶部为准。50/68/69 包含原48，不重复相加。
两处旧测试的请求前件按串行改为等待真实 queue 发出，但可见性、旧结果否决和成功断言未放宽。
最终当前 **69/69、0 失败/0 跳过/退出 0**：`supplement/build-tests-final.log` 成功编译当前源之后
运行 `supplement/run-tests-final.log`，页源 SHA-256
`b1969f1d818bde4b152d3296b56dec15628f248013dbb94a890dd27332bb59eb`。
当前 x64 Shell/Core 和夹具 Release 构建均 0 警告/0 错误，`supplement/build-shell-final.log`；
`format-shell-final.log` / `format-tests-final.log` 共10份题内C#零修改、退出0、无工作区加载失败。
排队后 Navigate 抛错/恢复的一项属于最终新增边界验证，不将它伪记为前述68项修前红测。

实现不从 URL 相等或 NavigationId 数值推断请求新旧：未回事件的已发请求留住其原 `(URL, generation)`，
最新后续请求单槽合并；收到 Starting 或没有 Starting 的 Completed 后，才从当前 UI queue 发最新请求，
不在 WebView2 回调里重入 Navigate。已观察的 ID 在本页生命周期保留，旧已开始/完成的 ID 不能消费
新未开始请求槽；ID 可以是大 UInt64 或非递增数值。取消不回 Starting 也能推进，真实当前 B 失败仍显示；
Release/来源撤销后排队任务不发出，队列拒绝/发出异常如实失败。现有 NavigationId/source/generation、
inline/hash、取样和显隐恢复仍走原方法，JS/鉴权/凭据注入未改，不添加 query nonce 或改服务器 URL。

取舍：尚未 Starting 时刷新/换路由会合并等待一次 UI 调度，不再立即连续发同址 Navigate；每个已见 ID
保留到本页 Release，小量内存换取不猜数值顺序。若引擎既不给 Starting 也不给 Completed，不能凭固定
延时猜成“旧请求已取消”，因此只保留现有等待/超时反馈；不擅自伪造当前成功或越过来源失效。
测试对无 Starting 的取消完成、晚到开始/重复完成、已开始旧 ID、先排队再换路由/释放都直接调用真实
handler 控制流。DispatcherQueue 仅一个拒绝/可暂停的单线程边界，签名来自本树已还原项目 metadata，
不重写 WinUI、不启动窗口。真实引擎时序/取消概率/DOM/像素/完整启动仍是覆盖缺口。

此补修复用已还原固定依赖，不新增包/修改配置；`--no-restore` 和全部严格 Release 标志沿用上方命令。
当轮日志单独放 `artifacts/r019/supplement/`；每条 SDK 命令均禁开发证书生成，未再触发首次初始化。
主线程只限定整合最终 DashboardPage、既有夹具改动/新 queue 边界和本 README，不拷 PROGRESS、DLL
或旧输出，在其本树重编译/运行再决定验收。

## 主树限定整合（2026-10-24）

初源9ce5d1、同址补修689c81f的最终控制流已在主树重新编译和运行，当前69项全过／0跳过／退出0；日志 `artifacts/staged-repair/batch05/r019-*.log`，不把来源48／50／68结果替作主树。生产页与最终来源逐字一致，一份匿名identity夹具仅对象初始化换行修正。源码SHA-256与原样提取边界由运行器再核，真实WebView2／UI／凭据注入／完整启动仍未运行。
