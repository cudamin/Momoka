# R056 字幕写响应不明的父页刷新欠项

这是独立、定向的离线源码夹具，已最小注册到方案构建，仍需单独运行；不注册 Core runner，也不运行全 Core、SelfCheck 或用户级 mutex。[Program.cs](Program.cs) 正式注册36项；本树首次枚举没有现成 Momoka.SubtitleDialogTests，所以新建此项目，不猜测或依赖 ignored 历史工具。

## 执行的是真实方法

- [项目文件](Momoka.SubtitleDialogTests.csproj) 默认编译链接当前树的 [SubtitleDialog.xaml.cs](../../src/Momoka.Shell/Views/SubtitleDialog.xaml.cs)，包括实际 Closing 委托、下载／删除事件、确认、恢复 GET、Touched／NeedsRefresh 和互斥，不复制算法。
- 每次 build 从实际 [ItemCommands.Server.cs](../../src/Momoka.Shell/Views/ItemCommands.Server.cs) 原样提取字幕 caller 和 GuardAsync／AskAsync／Root；开始与结束标记都要求唯一，缺失、重复或反序立即构建失败。实际 [ServerSubtitles.cs](../../src/Momoka.Core/Emby/ServerSubtitles.cs)、EmbyClient、EmbyHttp 和 scope 来自当前 Core。
- 为核对离页与旧 scope，不仿造父刷新守卫：只读原样提取 [DetailViewModel.cs](../../src/Momoka.Shell/ViewModels/DetailViewModel.cs) 的 Attached／完整 Reload block／LoadAsync／Cancel 和 [PageViewModel.cs](../../src/Momoka.Shell/ViewModels/PageViewModel.cs) 加载期方法，执行真实父 GET。源文件不修改。电影之外的加载拒绝执行；呈现／其它媒体列表是无请求边界，不扩写整页 stub。
- [UI边界](ControlBoundaries.cs) 只提供值、Closing 事件、ShowAsync 完成信号；[UiQueue.cs](UiQueue.cs) 在单线程队列执行真实 async-void 续体，显式挂起／释放，不睡眠或阻塞 Task。不是实际 WinUI Closed 事件、窗口输入、Frame 历史或渲染覆盖。

## 验收与取舍

36项逐项记录 GET、实际 write、服务器落实和父 changed 次数，包括两种写的首笔响应中断、直接 Closed、恢复 GET 尚挂起即 ReadClosed（迟到成功／失败）、好回读／读失败、写在飞互斥、禁止盲目重发、非首笔、重开、离页和来源切换／撤销。取消确认、权限403预核、快照变化、候选预核、入口取消均不造刷新义务；正常成功＋补读失败只守住既有 R024，不重改它。

虚拟 handler 在 POST／DELETE 到达时先改内存服务器轨道再抛 IOException；HttpRequestException 和传输超时也通过实际 EmbyHttp 走响应不明路径。断言首笔不明立即 NeedsRefresh=true、Touched=false；直接关闭交父一次；恢复 GET 仍挂住时父 GET 已完成交接，迟到结果不回填关闭框；未知回执无自动写入重放。

只新增 Dialog 局部标记方法供两个写异常出口共用；原 caller 已有 Touched 或 NeedsRefresh 加原 scope 的 IsCurrent 守卫，不改 ItemCommands.Server.cs。本地校验、权限异常、明确4xx拒绝不产生新义务；其他不能确认的写委托错误保守保留一次重读，不追认写成功。委托未暴露 HTTP 实际发送点，传输异常不能证明请求到达／服务器落实，某些实际未写的网络错误可能多一次 GET；不为精确分段扩改 Core、认证或协议。身份取消保留原处理，已有欠项不清零；恢复只读取，不重写。

## 安全范围

只用内存设置／虚拟服务器和直接安装的匿名 scope，不登录、恢复账号、检测产品或交换凭据。保护器两方法调用即失败；零真实 settings.json、DPAPI、配置备份或字幕文件读写。handler 只允许 .invalid 虚拟来源及限定条目／字幕路径，零真实网络。不开 App、WinUI窗口、播放器、native 媒体运行时或服务器，不重试 R001／003／005／031 受限链；不升级、放宽基线、发布或推送。构建、包解压、临时文件、CLI home 和日志均在本树。

## 当前树精确复验

以下在当前工作树根执行；SDK 版本见 [global.json](../../global.json)。固定缓存缺包即失败，不换源；NuGetAudit=false 仅各次离线 restore 使用，不写永久配置／安全基线。

~~~powershell
$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
if ((git --no-optional-locks rev-parse --show-toplevel).Trim().Replace('/', '\') -ne $root) { throw '工作树根不符' }
$dotnet = 'C:\Users\89400\.dotnet\dotnet.exe'
$out = Join-Path $root 'artifacts/r056'
New-Item -ItemType Directory -Path $out,(Join-Path $out 'temp'),(Join-Path $out 'dotnet-home'),(Join-Path $out 'nuget-packages') -Force | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $out 'dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = Join-Path $out 'nuget-packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $out 'nuget-http-cache'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $out 'nuget-plugins-cache'
$env:TEMP = Join-Path $out 'temp'
$env:TMP = $env:TEMP
$env:MSBUILDDISABLENODEREUSE = '1'
$fixture = Join-Path $root 'tests/Momoka.SubtitleDialogTests/Momoka.SubtitleDialogTests.csproj'
$shell = Join-Path $root 'src/Momoka.Shell/Momoka.Shell.csproj'
foreach ($project in @($fixture, $shell)) {
    & $dotnet restore $project --configfile (Join-Path $root 'nuget.config') --source 'C:\Users\89400\.nuget\packages' --packages $env:NUGET_PACKAGES --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false
    if ($LASTEXITCODE -ne 0) { throw '固定缓存还原失败，停止' }
}
foreach ($project in @($fixture, $shell)) {
    & $dotnet build $project -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
    if ($LASTEXITCODE -ne 0) { throw '当前Release编译失败，不跑旧产物' }
}
& $dotnet run --project $fixture -c Release --no-build --no-restore
if ($LASTEXITCODE -ne 0) { throw 'R056定向测试失败' }
~~~

初次有效红时产品完全未修改：red-build-02.log 零警告错误，red-tests-01.log 共36项14过22败0跳过／退出1。red-build-01.log 有4条新夹具未用字段警告，已定点修正，不算有效红。首次修后 green-build-01.log 零警告错误、green-tests-01.log 36全过／退出0。

最终夹具加强立即欠项与恢复 GET 仍挂住时的交接断言（不新增注册数）；最终同夹具基点审计 final-red-build.log 零警告错误，final-red-tests.log 36项14过22败0跳过／退出1，默认当前源码 final-green-build.log 零警告错误，final-green-tests.log 36过0败0跳过／退出0。以下审计完整基点 Git blob，逐字原样编译链接，不覆盖工作树产品文件；测试退出1是有效红而非绿验证。之后必须默认源码重编译再跑绿，不留基点 DLL 充当前结果。

~~~powershell
$baseSource = Join-Path $out 'baseline-SubtitleDialog.xaml.cs'
$blobRef = 'dab7d3c7bf807fc49fdd2999b71d27c6e67a7e65:src/Momoka.Shell/Views/SubtitleDialog.xaml.cs'
$blob = & git --no-optional-locks show $blobRef
if ($LASTEXITCODE -ne 0) { throw '基点源码读取失败' }
[IO.File]::WriteAllText($baseSource, ($blob -join [char]10) + [char]10, [Text.UTF8Encoding]::new($false))
$expected = (& git --no-optional-locks rev-parse $blobRef).Trim()
$actual = (& git --no-optional-locks hash-object --no-filters $baseSource).Trim()
if ($actual -ne $expected) { throw '审计源码不等于基点Git blob' }
& $dotnet build $fixture -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false "-p:R056DialogSource=$baseSource"
if ($LASTEXITCODE -ne 0) { throw '基点审计构建失败' }
& $dotnet run --project $fixture -c Release --no-build --no-restore
if ($LASTEXITCODE -ne 1) { throw '没有预期的有效红' }
& $dotnet build $fixture -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
if ($LASTEXITCODE -ne 0) { throw '当前源码重编译失败' }
& $dotnet run --project $fixture -c Release --no-build --no-restore
if ($LASTEXITCODE -ne 0) { throw '最终绿测试失败' }
~~~

定向空白包含本次生产文件与新夹具8份 C#。以方案 Release/x64 加载，避免单项目全局 Platform 被错误传给 Core 引用；退出0但出现工作区加载警告仍不是完整检查通过。

~~~powershell
$env:Configuration = 'Release'
$env:Platform = 'x64'
$env:BuildInParallel = 'false'
$env:UseSharedCompilation = 'false'
$env:MSBuildNodeReuse = 'false'
$files = @((Join-Path $root 'src/Momoka.Shell/Views/SubtitleDialog.xaml.cs')) + @(Get-ChildItem -LiteralPath (Split-Path -Parent $fixture) -File -Filter '*.cs' | Select-Object -ExpandProperty FullName)
& $dotnet format whitespace (Join-Path $root 'Momoka.sln') --include $files --verify-no-changes --no-restore --verbosity detailed
if ($LASTEXITCODE -ne 0) { throw '题内空白失败' }
git --no-optional-locks diff --check
if ($LASTEXITCODE -ne 0) { throw '差异空白失败' }
~~~

来源树 `r056-subtitle-refresh-obligation/Momoka` 在基点 `dab7d3c7bf807fc49fdd2999b71d27c6e67a7e65` 上保留上述 `artifacts/r056` 配对红绿。主整合树将提取边界适配为 R-025 后的完整 Reload block，仅补未用版本／目标值边界，不改父页生产代码。当前主树 `artifacts/staged-repair/batch05/r056-final-build.log` 零警告错误、`r056-final-tests.log` **36过／0失败／0跳过／退出0**，`r056-integrated-shell-build.log` 当前x64 Shell Release零警告错误，`r056-final-whitespace.log` 9份C#题内格式0修改／退出0且无加载失败。以上是主树当前实测，不把来源输出搬来当主树结果。

证据目录是各树 ignored 产物，不是干净检出的依赖。源码／写集／链接／UTF-8 LF与主树整合结果见 [增量记录](../../docs/代码审查-分阶段修复-2026-10-23.md) 的 R-056 段。主树只限定整合本次文件，针对当前 DetailViewModel／DetailPage 与原样提取边界重新验证；不搬来源 bin／obj／包缓存／历史报告。未覆盖真实 UI、布局、字幕服务器处理顺序、账号权限或配置运行；不等于完整产品交付，快捷方式仍旧版。
