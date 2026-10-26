# R-045 字幕拖拽所有权专项回归

本项目已最小注册到方案构建，仍独立于共享 `Momoka.Tests` 跑器、需要单独运行；不覆盖共享 Program、不启动 Shell/App/WinUI/native。74 项实际生产方法回归，另有 5 项提取器 fail-closed 构建反例，不把它们与 74 项相加。

## 实际生产边界

- 构建先编译 `SourceExtractor`，引用仓库 SDK 的 Roslyn DLL，不新增解析器包。MSBuild 任务按完整语法节点逐字提取当前树的 `PlayerPage.OnPictureDrop`、`Attach`、`Detach`、`Shutdown`，`PlayerViewModel.AddExternalSubtitlesAsync`、`CaptureInteraction`、`IsCurrentInteraction`、`InteractionContext`、相关原字段／适用性属性和 `OnTracksChanged`。不复制字幕过滤、所有权判断或命令循环。
- 缺节点、重复节点、方法无正文、语法错误、命名空间改变或其它工作树输入／输出均构建失败；提取发生在每次编译前，不能用遗留生成源放行。输出源码和 `SourceManifest.txt` 留在本项目 obj，来源哈希同时编入测试程序并在运行时打印。
- `ViewModelBoundary.AddCaptured` 仅按唯一的实际方法签名调用：基点的一参数入口或修后的显式 context＋页面 predicate 入口，不实现替代守卫。生产页面调用在同一程序集直接编译，不经这个适配器。
- `FixtureBoundary` 使用当前 `Momoka.Core` 的 `PlaybackService.CommandAsync` 和 `SubtitleFile.Filter`，只装入内存 `_current` 假 handle／代次。服务的会话依赖为 null、`allowPlayback=false`，后端工厂拒绝执行；不构造 EmbySession、SettingsStore、CredentialVault、保护器或登录来源。设置替身拒绝读取／保存／恢复；任何旁支调用计数，即使被 Core 捕获也使测试失败。
- WinRT DataView／StorageFile 是匿名内存值和受控 Task，不读取实际字幕文件；WinUI 事件参数／宿主是无窗口值和空订阅边界。async-void 用单逻辑线程队列完成，命令用可控 Task 挂起，不靠 Thread.Sleep／真实时间／媒体交错。

## 覆盖与明确缺口

12 类所有权变化分别放在 WinRT 等待、第一条命令等待、最后一条命令等待：Detach、Detach后同VM再Attach、换VM、新handle／同handle A→B、VM文件代次、仅后端代次、起播意图、同ID新条目对象、交互版本、VM取消、页面Shutdown。补测 WinRT调用本身换片、返回后先拒绝再取Path、读取Path时换片／换VM、旧context入口、过滤期间作废、每条命令前后页面predicate、停止后迟到拒绝。

正常路径保留后缀／去重／次序、sub-add select、最后成功字幕反馈、部分失败继续、全失败错误、未播放／无支持字幕提示、deferral finally、现有DragOver行为和实际VM轨道通知刷新。所有换片场景 B 收到零条旧拖拽命令；已在 A 发送的一条不撤销，失效后停止余下命令并静默丢弃过期反馈，不借换片重发。新增 `PictureInHostWindow` 守卫只收紧当前集成页面适用性，不接管外部／独占原生拖拽；DragOver本身未改。

没有执行完整 PlayerViewModel 构造／Connect订阅、实际播放生命周期／原生命令代次门、WinRT投影／Windows拖拽／WinUI布局和消息循环、IPC／libmpv／媒体、真实配置／账号／凭据／服务器。轨道刷新测试向实际VM通知方法交入内存快照，不证明 mpv 真正发出 track-list。没有完整Core/SelfCheck、用户级mutex、桌面UI、发布／推送／升级；主树限定整合后由整合线程串行重验，快捷方式仍旧版，非完整产品交付。

## 安全复跑命令

仅在当前授权工作树根运行。下面只写当前树 `artifacts/r045-local-run`，所有 dotnet 调用前关掉开发证书生成；首次 restore 仅本机固定缓存，缺包即停，不改仓库 NuGetAudit 基线。

```powershell
$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
$sdk = 'C:\Users\89400\.dotnet\dotnet.exe'
$out = Join-Path $root 'artifacts\r045-local-run'
foreach ($name in @('', 'cli-home', 'temp', 'packages', 'http-cache', 'plugin-cache', 'logs')) {
    [IO.Directory]::CreateDirectory((Join-Path $out $name)) | Out-Null
}
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_HOME = Join-Path $out 'cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:Configuration = 'Release'
$env:TEMP = Join-Path $out 'temp'
$env:TMP = $env:TEMP
$env:NUGET_PACKAGES = Join-Path $out 'packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $out 'http-cache'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $out 'plugin-cache'
$config = Join-Path $out 'NuGet.Config'
$xml = '<configuration><packageSources><clear/><add key="fixed" value="C:\Users\89400\.nuget\packages"/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders></configuration>'
[IO.File]::WriteAllText($config, $xml + "`n", [Text.UTF8Encoding]::new($false))
$project = Join-Path $root 'tests\Momoka.SubtitleDropOwnershipTests\Momoka.SubtitleDropOwnershipTests.csproj'
& $sdk restore $project --configfile $config --source 'C:\Users\89400\.nuget\packages' --packages $env:NUGET_PACKAGES --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false *> (Join-Path $out 'logs\restore.log')
if ($LASTEXITCODE -ne 0) { throw '离线包不足／还原失败，停止，不访问网络' }
& $sdk build $project -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false *> (Join-Path $out 'logs\build.log')
if ($LASTEXITCODE -ne 0) { throw '当前编译失败，停止，不能跑旧测试' }
& $sdk run --project $project -c Release --no-build --no-restore *> (Join-Path $out 'logs\run.log')
if ($LASTEXITCODE -ne 0) { throw 'R-045 专项失败，保留日志' }
```

Shell 只构建、不运行，使用同一隔离环境、缓存和参数；第一次缺obj时仍先按上面的固定源 restore Shell。定向空白检查使用 `Configuration=Release`，否则 `dotnet format` 默认Debug找不到已编译的Release提取任务，返回0但工作区加载失败也不算通过。主树可用方案Release/x64检查全部C#，同时检查日志没有加载失败；文本严格UTF-8、无BOM、LF，PowerShell例外按主规则使用BOM/LF。

## 本次红绿证据

基点 `32178fc365a8f17e41580df29ad6759bd9a3bd6b`。先不改生产，实际74项得到19过／55败／0跳过／退出1；修后74过／0败／0跳过／退出0。最终夹具加入禁用旁支计数后，用 `git cat-file blob` 逐字取同基点七份Shell输入到本树artifacts，不覆盖工作区，再次19过／55败／退出1，随后重新编译当前源并74全过。两轮都是相同74个名称，不叠加为148项。

本次完整命令参数／环境／退出码保存在本树 `artifacts/r045/logs/*.json`，文本日志同名 `.log`；有效最终红／绿为 `21-red-final-fixture-baseline.log`／`23-green-final-current-fixture.log`，其编译是 `20`／`22`。当前x64Shell编译为 `24-build-final-current-x64-shell.log`。

保留且不计有效红：初内联提取器61编译错误；提取器初跑3项未使用字段警告；缺嵌入manifest的首跑中止；Debug空白检查返回0但工作区加载失败。随后正式编译与Release空白均清零，不禁用／压低警告。5项提取器反例各预期build退出1，不在失败构建后运行旧测试。更详细取舍及所有路径在本树PROGRESS与本机审计报告。

## 主树限定整合

来源 `9314fe1080aca131fea0eaaa36f2bae8d1113d5b`；主树只复制两产品与本正式项目，不带来源PROGRESS／bin／obj或缓存。当前主树 `artifacts/staged-repair/batch05/r045/main-final-build.log` Core／专项／SourceExtractor零警告错误；`main-final-tests.log` **74过／0失败／0跳过／退出0**。主树5份提取反例各预期退出1，之后默认当前源重编译再验74，未运行坏构建旧输出。当前x64Shell `main-shell-build.log` 零警告错误，实际VM轨道11／音量31相关回归通过，全方案Release/x64格式0修改／退出0、无加载诊断。来源有效红留来源树，未拿来源产物冒主树结果；当前7源码SHA256已核与生产文件一致，独立静态复核无保留。
