# R044：播放所属 scope 实际撤销

本项目直接引用当前工作树的 Momoka.Core，运行真实 PlaybackService.PlayAsync／StopAsync，不复制生命周期实现，不启动 Shell、窗口、native 或真实播放器。

## 前件与边界

- reflection 构造匿名内存 EmbySessionScope.Identity，安装三个 source 和默认身份；不 SignIn、TryRestore、SelectSource、DPAPI、保存账号或读写配置。Session 的 store 为 null，SecretProtector 拒绝任何调用。默认切换只改内存字段并发真实 SourcesChanged 通知。
- 直接 IdentityLifetime.Cancel 模拟 SignOut 最后一步，不调用会 Persist 的 SignOut／EndSource。来源字典刻意保留，断言播放服务不改 sources；其它身份仍 available。session.Dispose 场景只测试真实退出的内存取消／清理，不涉及保存。
- IPlaybackBackend／IPlaybackHandle 为可挂起内存替身；开始／monitor／Stop／Dispose／同句柄交接有明确完成信号和计数。未建立时取消后不宣布开播，迟到句柄 Stop／Dispose 各一次；已建立时等 PlayAsync 完成、Finish 一次，StopAsync 等报告和 Dispose，之后新 source 仍可播放。
- 所有 HTTP 都由注入 handler 捕获，只允许 .invalid 域名的题内 POST 路由。拒绝认证及未安排请求，绝不使用默认网络 handler。上报身份已撤销时允许 ProgressReported=false；不改 Reporting.cs，不借当前默认身份补报，也不重新登录。
- 31 项注册用例覆盖准备、初始轨道读取、运行期（显式 scope／条目 scope、报告开关、控制通道）、在飞报告、普通默认切换、旧撤销／迟消息、新 launch supersede、同句柄／不重用、取消／停止／session 退出、异步清理、自然 EOF／UserQuit 终因及清理失败。矩阵内的组合是注册项；后续成功断言不另计用例。没有跳过项。
- 自然 EOF／UserQuit 已确认后才撤销的对照项保留真实终因及真实位置，不调用额外 client Stop，不把取消当播放错误或伪造用户观看历史。已有报告失败／取消的语义不变；离线证据不是服务器 exactly-once 或观看记录验收。

## 当前工作树复验命令

在指定独立树根执行。只读固定包缓存、所有解压／CLI home／临时文件／日志留在本树；缺包直接停止，不升级、不换源。NuGetAudit=false 仅此次离线 restore，不进入项目或共享配置。

~~~powershell
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\89400\.codex\worktrees\r044-playback-source-revocation\Momoka'
if ((git rev-parse --show-toplevel).Trim().Replace('/', '\') -ne $root) { throw '工作树根目录不匹配' }
$dotnet = 'C:\Users\89400\.dotnet\dotnet.exe'
$out = Join-Path $root 'artifacts\r044'
New-Item -ItemType Directory -Path $out,(Join-Path $out 'temp'),(Join-Path $out 'dotnet-home'),(Join-Path $out 'nuget-packages') -Force | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $out 'dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = Join-Path $out 'nuget-packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $out 'nuget-http-cache'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $out 'nuget-plugins-cache'
$env:TEMP = Join-Path $out 'temp'
$env:TMP = $env:TEMP
$project = Join-Path $root 'tests\Momoka.PlaybackSourceRevocationTests\Momoka.PlaybackSourceRevocationTests.csproj'
& $dotnet restore $project --configfile (Join-Path $root 'nuget.config') --source 'C:\Users\89400\.nuget\packages' --packages $env:NUGET_PACKAGES --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false
if ($LASTEXITCODE -ne 0) { throw '固定缓存还原失败，停止' }
& $dotnet build $project -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
if ($LASTEXITCODE -ne 0) { throw '当前 Release 编译失败，不跑旧测试' }
& $dotnet run --project $project -c Release --no-build --no-restore
if ($LASTEXITCODE -ne 0) { throw 'R044 定向测试失败' }
~~~

空白检查仅本次四份 C#，不加载无关 Shell 或全量方案：

~~~powershell
$env:Configuration = 'Release'
$env:BuildInParallel = 'false'
$env:UseSharedCompilation = 'false'
$env:MSBuildNodeReuse = 'false'
$env:MSBUILDDISABLENODEREUSE = '1'
$core = Join-Path $root 'src\Momoka.Core\Momoka.Core.csproj'
& $dotnet format whitespace $core --include (Join-Path $root 'src\Momoka.Core\Playback\PlaybackService.Lifecycle.cs') --verify-no-changes --no-restore --verbosity detailed
if ($LASTEXITCODE -ne 0) { throw 'Core 定向空白失败' }
& $dotnet format whitespace $project --include (Join-Path $root 'tests\Momoka.PlaybackSourceRevocationTests\Program.cs') (Join-Path $root 'tests\Momoka.PlaybackSourceRevocationTests\Fixture.cs') (Join-Path $root 'tests\Momoka.PlaybackSourceRevocationTests\FakeBackend.cs') --verify-no-changes --no-restore --verbosity detailed
if ($LASTEXITCODE -ne 0) { throw '测试定向空白失败' }
git diff --check
if ($LASTEXITCODE -ne 0) { throw '差异空白失败' }
~~~

另外逐文件核对 UTF-8／LF／末尾换行、csproj XML、上述 PowerShell 命令语法／标志／先 build 后 no-build 顺序和只写授权文件。文档检查不运行代码块中的 restore 第二次；实际日志留在 ignored artifacts/r044，不是干净检出的依赖。源路径均由当前 root 展开，主目录和整合树只读。

## 有效红绿与取舍

- 基点 dab7d3c7bf807fc49fdd2999b71d27c6e67a7e65，产品未改时：31 项，12 通过／19 失败／0 跳过／退出 1，red-build-02.log／red-tests-01.log。首次跑器同步等待 warning 的 red-build-01.log 未计证据；改为直接 await 异步入口、没有分析器压制后才取正式红。
- 首修只给 PlayOperation.Startup／Lifetime 链接捕获 scope.Lifetime：重新 Release build 零警告错误，31 项全过／0 失败／0 跳过／退出 0，green-build-01.log／green-tests-01.log。采用现有 monitor 取消 catch、恰一次 Stop task、Finish／release 与 request gate，不另建监听器／源事件／全局 Stop，不修改同窗或 EOF 后端。
- 最终证据为 final-build.log、final-tests.log、format-core.log、format-tests.log、scope-doc-lf.log（全部在本树 artifacts/r044）。只整理同一批 31 项的资源登记和 JSON 释放，不新增播放场景。
- 没有 full Core、SelfCheck mutex、R001／003／005／031 受限链重试、真实 server/native/WinUI/window/App/media 或发布推送。未验证真实后端销毁耗时、同窗视觉、服务器端处理次数／观看记录；本地提交只交主线程限定合并及当前代码重验，不计主树完成，快捷方式仍旧版。

## 主树整合验证（2026-10-24）

来源31项红绿属于 `C:/Users/89400/.codex/worktrees/r044-playback-source-revocation/Momoka`，不代替主树。限定整合时项目已最小入方案，主树当前31项全部通过／0跳过／退出0；完整Core2549项通过（本独立31项不混入Core注册数）。两份夹具仅格式修正，Lifecycle产品与来源逐字相同。主树日志 `artifacts/staged-repair/batch05/r044-*.log`，其中 `r044-final-tests.log` 为最终定向绿，`r044-full-core-tests.log` 为当前完整Core。
