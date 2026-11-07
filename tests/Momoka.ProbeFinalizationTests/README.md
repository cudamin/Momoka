# R-071 / R-072 报告收尾专项

## 2026-11-05 Inspect 检查所有权增补

新增2个真实异步回归，当前套件69/69、0失败、0跳过。构建时原样提取 `ShellNavigationProbe.Inspect.cs` 的局部 `AddCheck`，复用真实 `ShellProbeReport.CaseAsync` 和写入路径；只有布局属性、操作注册及180秒→1秒的超时参数使用边界替身。检查任务在超时后仍挂起时，验证返回Task未完成、控制区仍Collapsed；报告writer在该超时节点抛IOException时也须等待实际任务的finally完成，再恢复Visible并传播原报告异常。这里不复制AddCheck实现，也不把源码字符串匹配作为行为断言。

默认提取当前树产品源；可在构建时传入 `-p:InspectOwnerSource=<绝对源路径>` 进行修前反例或提取边界检查。缺失/重复的AddCheck起点和缺失的ResizeInspect结束边界会使构建失败。只提取局部函数，不编译WinUI或整个Inspect页面。

本轮隔离树基点 `43c4515` 尚无AddCheck，已将根035a未提交的修复方法定点补入本树产品文件作为不提交的编译依赖。方法来源 `C:\Users\89400\.codex\worktrees\035a\Momoka\src\Momoka.Shell\ShellNavigationProbe.Inspect.cs`，保存方法副本SHA256为 `3E57C46DBDD2E2A46F3433CABAAAEACB141575D769D5D6CAD375CC1AAB32F2B4`。根代理整合本测试提交时须同时保留该产品修复；不能只合测试到不含AddCheck的基线，也不要复制本树整个Inspect文件。

本树 `work/ui-polish/probe-tests/red-build.log` / `red-tests.log` 为移除实际finally等待（仅将 `await running` 改为 `_ = running`）的预期反例：构建0警告0错误，69项67过2败，退出1，失败仅新增两条；`green-build.log` / `green-tests.log` 为原样方法69/69、0跳过、退出0。红源为ignored `legacy-Inspect.cs`，通过InspectOwnerSource指向该文件构建，再用正常build恢复。首次试验使用GetAwaiter导致1条RS0030，已换为无等待赋值反例并重新零警告编译；最终红日志不含该试验警告。

提取拒绝反例 `missing-extraction.log` 和 `duplicate-extraction.log` 均按预期构建失败，指出AddCheck起点缺失或歧义；随后正常当前源重新编译0警告0错误。这两条是构建拒绝证据，不计入69项行为回归。受影响C#格式检查退出0，5个测试文件差异/编码检查通过。

本增补不启动UI、不运行全测、不读用户设置或操作真实服务器，也未覆盖WinUI窗口像素、按钮Click重入与外层busy标志；它证明AddCheck返回前仍持有真实检查Task，因此正常外层await不会提前清除busy。真实UI重验由交付会话完成。

独立无窗口控制台项目，已由主线程接入 Momoka.sln 构建映射与开发文档的独立验证命令；不修改公共 Momoka.Tests Program、csproj或报告基线。普通 Core / 页面契约运行不会自动执行本项目。入口为 `Momoka.Tests.Program.Main()`；独立计数，不能把内部断言或重叠覆盖加成其它套件的用例数。

## 验证真实生产控制流

- 直接编译链接 `src/Momoka.Shell/ProbeReports.cs` 与 `ShellSelfCheck.Finalization.cs`。它们也是 Shell 真正使用的报告、Record / Case、收尾与退出码实现，不是仅供测试的 Core 模型。
- 构建时从当前生产文件逐字提取自检 Run 的最终报告段、Schedule 的最后一次回调，以及两种探针的实际入口、Record / Case 委托和退出码属性；边界缺失或起点不唯一会使构建失败。生成源仅在本项目 obj；字符串定位只用于编译真实代码，不以源码匹配充当行为断言。
- 只替换 UI 读数、窗口关闭 / Dispose、Application.Exit、未使用的客户端资源和可选截图端点。两种探针的默认报告写入仍走生产 `File.WriteAllText`，并实际用独占文件锁和目录阻塞路径验证 IO 失败。
- 9 个实际入口用例执行两种探针真实调用方，覆盖成功、断言失败、首次持续写失败、最终持续写失败和 Shell 最终范围报告失败；字幕入口还核对最外层 using 资源确实释放。

## 实现取舍

- 自检 `CompleteReport` 写前保留退出码 1，完整写入返回后且失败数为 0 才置 0；读取、日志或写入异常仍经过真实 `finally` 关闭。未完成报告和可选 tree / PNG 的现有语义保留，不增加或放宽自检基线。
- 两种探针共享 Shell 内部的无 UI 收尾，不把验收基础设施塞进公共 Core。首次写失败仍中止宿主工作并保留失败；最终写只尝试一次，随后无论成功与否都到 Dispose / Exit。
- Shell 最终先尝试范围报告、再写 JSON 正文；范围失败会保存在内存检查中，正文若可写便能带出该失败。两份 IO 不相互短路，任何报告、断言或清理失败均为非零，不用重试刷绿。
- 本专项证明写入 API 完成与报告可读，不声称能抵御系统断电 / 存储控制器缓存，也不增加 fsync 或原子替换协议。

## 精确运行命令

在当前工作树根目录 PowerShell 执行。SDK 为 `$HOME/.dotnet/dotnet.exe` 10.0.400；首次还原，当前 Release 编译成功后才 no-build。固定缓存离线还原，不升级依赖：

~~~powershell
& "$HOME/.dotnet/dotnet.exe" restore 'tests/Momoka.ProbeFinalizationTests/Momoka.ProbeFinalizationTests.csproj' --source "$HOME/.nuget/packages" -p:NuGetAudit=false --disable-parallel -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" build 'tests/Momoka.ProbeFinalizationTests/Momoka.ProbeFinalizationTests.csproj' -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project 'tests/Momoka.ProbeFinalizationTests/Momoka.ProbeFinalizationTests.csproj' -c Release --no-build --no-restore
~~~

`NuGetAudit=false` 仅用于本次缓存还原，不改变仓库审计设置。产物只能来自本工作树；测试会检查 Core 程序集位置。JSON 结果及所有夹具目录保留于本树 `artifacts/r071-r072/fixture/run-<随机值>`，不递归删除。

## 本轮覆盖与证据

- 64 个注册行为用例：自检 12、两种探针各 16、Shell 范围 / inspect 9、实际探针入口 9、收尾异常边界 2；564 次内部断言不作为额外用例累计。全部 0 失败 / 0 跳过。
- 两种探针均覆盖首次写失败持续 / 恢复、仅最终持续失败、普通断言失败、部分最终写、同一异常对象持续抛、取消、路径缺失、真实锁、报告路径为目录、清理抛异常以及报告与清理同时失败。成功用例验证最终 writer 返回前始终为非零。
- 修前保存起点 `b7e91ea8d0304223e13d51741aac333db631e782` 五份源文件及 SHA-256；运行原样提取的收尾片段，17 场景 10 失败 / 7 通过 / 退出 1。它是修前失败证据，不是通过门禁：本树 `artifacts/r071-r072/before/source-manifest.json`、`build-final.log`、`run-final.log`。
- 官方 `tools/selfcheck-diff.ps1 -Log <本次生成报告> -Baseline <独立 A 夹具基线>` 另跑 6 条只读路径：完整报告退出 0；断言失败、锁后未完成、部分写、缺失、目录非报告均退出 1。官方源码和 `docs/selfcheck-baseline.txt` 不修改；不是正式闸门 4 全绿，也不启动其默认产品 / 登录路径。

## 未覆盖边界 / 正式注册需求

没有运行 App、真实 self-check 或完整 `--probe-subtitles` / `--probe-shell`，因为本任务明确禁止 UI / native / 登录 / 配置与凭据 IO。未验证 WinUI 消息循环、真实窗口销毁、截图 / 原生渲染、实际 DPAPI 或真实服务；`close` / `dispose` / `Exit` 可达由源码链接控制流与注入端点证明，不冒充实际 HWND / WinRT 验收。

## 显式匿名实际自检宿主

### 同程序集实际自检入口

当独立 `dotnet exec` 宿主的入口程序集使 XAML 类型解析失败时，可显式运行
`Run-AnonymousSameAssembly.ps1 -BuildOnly` 编译测试 apphost。该脚本只通过临时 MSBuild targets 注入
`AnonymousEntry.cs` 并覆盖 StartupObject，保留 AssemblyName=Momoka、全部原 Shell 源码、XBF、PRI 和依赖；
产物位于本树 `artifacts/anonymous-same-assembly/bin`，是带测试入口的验收产物，不能作为桌面交付件。
普通产品构建从不导入这个 targets，也没有新增产品 CLI。

脚本实际启动的就是被读屏的窗口，使用正常可见启动状态；不以 Hidden 启动后再当作真实像素通过。
`-BuildOnly` 仍仅构建。验收构建共用项目 obj，完成后须重新构建最终正常 Shell/Tests 并核对 Core 哈希，
不能把带测试入口或输出路径不同的产物当作桌面交付件。

```powershell
& ./tests/Momoka.ProbeFinalizationTests/Run-AnonymousSameAssembly.ps1 -BuildOnly
# 由唯一桌面验收会话在当前原生程序均已退出后串行启动：
& ./artifacts/anonymous-same-assembly/bin/Momoka.exe --anonymous-selfcheck (Join-Path (Get-Location).Path ('artifacts/anonymous-selfcheck/run-' + [guid]::NewGuid().ToString('N')))
```

这个测试入口在真实 Momoka apphost 的 STA 线程调用同程序集的原 `Program.Run`，不走真实 profile 解析。
自行生成空账号设置，关闭网络功能，保持生产 `selfCheck:true` 禁止播放策略；HTTP只计数不记录地址。
实际计时器拍数、Report、退出码和前后六份生产源SHA写入 `same-assembly-receipt.json`；少于三拍、
无报告或HTTP计数非零均不算完成。它仍是匿名局部自检，不代表真实服务器分支或正式闸门4全绿。
默认无窗口回归同时执行输入拒绝前件，不启动这个 apphost。

默认入口仍运行无窗口回归；`anonymous-selfcheck-host` 是验收专用入口，不能由默认测试或回归清单隐式启动。
先成功编译当前 Release Shell 与本项目，核对两目录的 Core 字节相同，再显式运行：

```powershell
$root = (Get-Location).Path
$shell = Join-Path $root 'src/Momoka.Shell/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Momoka.dll'
$host = Join-Path $root 'tests/Momoka.ProbeFinalizationTests/bin/Release/net10.0/Momoka.ProbeFinalizationTests.dll'
$output = Join-Path $root ('artifacts/anonymous-selfcheck/run-' + [guid]::NewGuid().ToString('N'))
& "$HOME/.dotnet/dotnet.exe" exec --runtimeconfig ([IO.Path]::ChangeExtension($shell, '.runtimeconfig.json')) --depsfile ([IO.Path]::ChangeExtension($shell, '.deps.json')) $host anonymous-selfcheck-host $root $shell $output
```

宿主只调用当前 Shell 的实际 `Program.Run`，不调用会解析真实 profile 的 `Main`，不新增产品命令行开关。
它在本树全新的固定盘目录自行生成 schema27 空账号设置，关闭 MoviePilot、AniBT、弹幕、翻译、更新及外部配置；
实际 `selfCheck:true` 继续使用生产的禁止播放策略。专用 STA 线程执行真实 App 和 DispatcherQueue 自检计时器，
真实窗口、报告与退出码来自该 Shell，HTTP EventSource 只计请求数，不记录地址或请求内容。

路径、输入、同树 Shell/Core 字节和桌面互斥在启动前检查。MRT/PRI 的本进程基址对照已安装 Foundation2.3.12
`UndockedRegFreeWinRT-AutoInitializer.cs`，只调整此进程的 AppContext，退出后恢复；不改变机器环境或用户目录。
回执 `anonymous-host-receipt.json` 记录程序集哈希、PID、实际报告及退出码；未写报告/初始化失败均不算执行成功。
空账号只验实际登录页、窗口、自检计时器与最终报告，是完整服务器自检的局部执行，不把缺数据或失败当全绿。
对真实服务分支的判断仍由原样源码链接回归提供，不证明真实账号、DPAPI、服务或播放已验证。

主线程仅整合题内生产和本专项项目文件，不复制支线PROGRESS。项目已加入 Momoka.sln 与开发文档的独立 restore → Release build → run 命令；公共 Core Program不注册本项目，没有新增产品开关或基线条目。普通完整构建只编译，不自动执行本套件。未发布、推送或更新快捷方式。
