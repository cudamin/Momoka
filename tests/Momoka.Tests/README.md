# AniBT 整理回执专项（R-038）

本页只说明 [AniBtReceiptStateTests.cs](AniBtReceiptStateTests.cs) 的定向离线测试，不是全 Core、自检或产品交付步骤。入口在 [Program.cs](Program.cs)，生产接线在 [AniBtService.cs](../../src/Momoka.Core/AniBt/AniBtService.cs)。其他订阅页夹具见 [原说明](README.MoviePilotSubscriptionPageState.md)。

## 验证的是真实调用，不是复制算法

40 个正式注册用例执行实际 `AniBtService.SubmitAsync`、`MoviePilotService.TransferPreviewAsync` / `TransferSubmitAsync`、正式 `MoviePilotTransfer.CompleteReceipt` 和 `EmbyClient.ScanLibraryAsync`。沿用 [StubTransport.cs](StubTransport.cs) 记录的真实请求方法、路径、正文、顺序和次数；`preview:true` 是只读预览，`preview:false` 才计实际整理写入。整批每份预览都必须先于第一个写请求。

- `accepted` 已接收待完成，`skipped` 已跳过；都不能宣称文件已移到目标、也不能凭成功信封扫描。空成功信封按正式回执补齐为 accepted，完成状态待确认。
- `completed` 配合逐文件 `success:true` 才报告完成。顶层失败信封中的唯一明确完成项仍保留；缺目标、缺行、重复/冲突回执、人工复核和未知状态不猜文件影响。
- 6 文件混合回执覆盖 accepted、skipped、completed、manual_review、未知状态，后续文件照原处理逻辑发送。`AniBtScrapeResult.Success` 原契约不变，Shell 的「已处理 N / 总数」仍统计处理回执成功（包含 accepted/skipped），**不是已完成或已移动数量**；不改 UI 总数、严重级别或批次停发规则。
- 扫描要求与本次已提交的不可变 `preview.Request` 对上：唯一明确 completed、有效目标路径，以及实际媒体路径被搬离或成功 logid 历史的旧本地目标被清理并换路径/存储；目的存储改变时，同路径字符串也不能视为原文件仍在。不能按历史 `mode` 或 `Reorganize` 开关单独猜；成功 copy/link/softlink 原记录重整也可能清理旧目标。未有成功旧目标清理的复制/链接、原路径完成、回执无法唯一对上预览，不做不必要的全扫。
- 一批只请求一次扫描，只给确有旧路径影响的文件附上原因。扫描成功仅表示「已请求，收录新路径和移除旧条目由服务器决定」；扫描失败、取消或身份改变仍保留已完成整理、手动恢复和勿重复整理提示。
- 可取消/身份拒绝、最后一份预览失败、实际请求丢失回执、原计划重交和新计划重新预览覆盖防重边界。扫描失败不解除 history/file 的实际发送账本。

## 安全边界

夹具设置、历史、FileItem、候选都只在内存。反射预置 MoviePilot 同身份的匿名 `CachedSession` 和 `_availableIdentity`，不调用测试连接或登录；保护器两方法一经访问即失败。假失联撤销 Available 时，仅为防重回归重新安装同身份匿名内存会话，**不清空账本**。静态完成助手来自 [MoviePilotTransferSubmissionTests.cs](MoviePilotTransferSubmissionTests.cs)，不注册或运行该文件的其他测试。

两个 HTTP 客户端都只用正式假 handler；保留路径白名单和请求断言，AniBT 假 handler 请求数必须为 0。`192.0.2.10` 是占位地址，不发生实际网络。媒体路径只是 JSON 字符串，不创建/移动/删除真实文件。零真实配置/凭据/DPAPI、账号登录、服务器、native、WinUI、App、播放或 UI 操作。不重试 R001/003/005/031 受限链，不跑 `AniBtIntegrationTests` 中的登录/跨重定向/凭据用例，不跑全 Core 或 self-check mutex。

## 在当前独立树复验

在当前工作树根运行；本机 SDK 固定为 `C:\Users\89400\.dotnet\dotnet.exe`（版本见 [global.json](../../global.json)）。只从现有固定 NuGet 缓存读取，包解压、CLI home、临时文件和日志全部留在本树。缺包直接失败，不更换网络源或升级包。`NuGetAudit=false` **仅用于这一次离线 restore**，不写入仓库配置，不作为永久安全基线。

```powershell
$ErrorActionPreference = 'Stop'
$root = (git rev-parse --show-toplevel).Trim()
if ((Get-Location).Path.Replace('\', '/') -ne $root.Replace('\', '/')) { throw '请在当前工作树根运行' }
$dotnet = 'C:\Users\89400\.dotnet\dotnet.exe'
$out = Join-Path $root 'artifacts/r038'
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
$project = Join-Path $root 'tests/Momoka.Tests/Momoka.Tests.csproj'
& $dotnet restore $project --configfile (Join-Path $root 'nuget.config') --source 'C:\Users\89400\.nuget\packages' --packages $env:NUGET_PACKAGES --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false
if ($LASTEXITCODE -ne 0) { throw '离线还原失败，停止' }
& $dotnet build $project -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
if ($LASTEXITCODE -ne 0) { throw '当前 Release 编译失败，不跑旧测试' }
& $dotnet run --project $project -c Release --no-build --no-restore -- anibt-receipt-state-fixture
if ($LASTEXITCODE -ne 0) { throw 'R038 定向测试失败' }
```

空白检查只包含本次三份 C#，无关 Shell 项目不加载：

```powershell
$env:Configuration = 'Release'
$env:BuildInParallel = 'false'
$env:UseSharedCompilation = 'false'
$env:MSBuildNodeReuse = 'false'
$env:MSBUILDDISABLENODEREUSE = '1'
& $dotnet format whitespace (Join-Path $root 'src/Momoka.Core/Momoka.Core.csproj') --include (Join-Path $root 'src/Momoka.Core/AniBt/AniBtService.cs') --verify-no-changes --no-restore --verbosity detailed
if ($LASTEXITCODE -ne 0) { throw 'Core 定向空白检查失败' }
& $dotnet format whitespace $project --include (Join-Path $root 'tests/Momoka.Tests/AniBtReceiptStateTests.cs') (Join-Path $root 'tests/Momoka.Tests/Program.cs') --verify-no-changes --no-restore --verbosity detailed
if ($LASTEXITCODE -ne 0) { throw '测试定向空白检查失败' }
```

同时核对 UTF-8/LF/末尾换行、`git diff --check`、文档本地链接、代码块语法与命令/正式注册一致，确认 `PrepareEmbyIdentifyAsync` 及其后的识别链相对指定基点字节未变。日志存本树 ignored `artifacts/r038/`，不是干净检出的前置依赖；不输出占位会话令牌或秘密内容。

## 本轮证据与取舍

修前正式红：37 项，通过 5、失败 32、跳过 0、退出 1；`red-build-03.log` / `red-tests-02.log`。第一次新夹具构建有 7 个类型名笔误错误（`red-build.log`），立即停止且未跑旧产物；修正后只读红测还发现假失联撤销 Available，修正夹具内存恢复后重新记录正式红。首修 36 过 1 败（`green-build-01.log` / `green-tests-01.log`）暴露扫描前取消仍触及 handler；补发送前取消守卫后 37 全过（`green-build-02.log` / `green-tests-02.log`）。复核实际请求的存储影响时追加 3 个用例，先红 40 项 38 过 2 败（`storage-red-build.log` / `storage-red-tests.log`），修正远程新目标仍可能清理本地旧目标的遗漏后，来源最终 40 全过、0 失败/0 跳过/退出 0。最终证据以本树 `final-build.log`、`final-tests.log`、`format-core.log`、`format-tests.log` 和 `scope-doc-lf.log` 为准。

只把已提交请求与成功旧历史和明确回执作为影响证据，不通过额外文件读写、登录或真实服务器校验追认完成；目标存储与路径一起判断，不能只按相同路径字符串省略旧目标核对。未核对服务器不同版本的清理实现、索引最终收录/移除、实际文件变化或 WinUI 展示；没有最终回执时不自动轮询/解锁/重发。主线程只限定整合题内文件，再在整合后的当前树重验；本分支本地提交不等于已发布，桌面快捷方式仍旧版。

## 主树整合补充（2026-10-24）

原40项证据属于 `C:/Users/89400/.codex/worktrees/r038-anibt-receipt-state/Momoka`，不等同主整合树。独立只读复核发现远程存储的对象路径可为相对值，却被本地绝对路径条件提前挡住，漏掉成功历史旧本地目标的清理判断。主树追加3项：远程相对对象路径＋成功旧目标需扫描、同路径无成功历史不扩扫描、本地完成目标仍要求绝对路径。补修前43项42过1败；修后43全过。目标明确非本地时只要求非空回执路径，本地继续按原绝对路径规则；不新增真实远程存储操作。

主树当前日志 `artifacts/staged-repair/batch05/r038-review-red-*.log`、`r038-fixed-*.log`，完整Core2549项通过（含43，不相加），三份C#定向Release/x64空白零修改，无工作区加载失败。来源40与主树43不累计；实际服务器文件清理／索引结果仍未验。

## 2026-10-25：纯合并基线的旧测试兼容
本轮验证树为 `C:/Users/89400/.codex/worktrees/merged-contract-tests/Momoka`，基点
`4afca26672297aedd7725d675706d7f2b923519c`，产品只来自父树
`mp-translation-interpolation/Momoka` 已 resolved/staged 的 `48c43fc20d5827fe5f5f147eb73e1d1ca7aa2795`
合并差异。导入 patch SHA256：`7ef4a4592f66b219639aa0ca499427da971443a34e9b8fe0f894cc36cffcd680`。
本树 629 份产品源的 Git blob 与父 index 逐份一致；未导入父未暂存的 translation/AppSettings 改动。
产品快照不暂存、不提交，不改共享 Program/Agreements/方案；最终功能再验由父线程完成。

测试 VM 使用本树实际重新编译的标准 x64 Release Shell（不是复制父 DLL）：
`src/Momoka.Shell/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Momoka.dll`，
2026-10-25 01:36:41 +08:00，4,743,168 字节，SHA256
`b9125a7ff07a46e236603200c97dd0608fd548a84ba588d39431d99fbb5e67f5`。
父首次合并 DLL 只作来源对照：2026-10-25 00:34:42 +08:00，SHA256
`735c3295886cb0dce9c8e4b92aa265739290959e0c8ce5befca5f9dcd1f97f77`；没有将它称为本树输出。
所有日志/输入清单留在本树 ignored `artifacts/verification/merged-contract-tests/`。

只补 `Momoka.Tests.csproj` 的 StartupReapplyContract.ShellPrefs.ApplyDanmaku 无 UI 边界，
不修改正式 Program、Agreements.txt 或原用例。`ReapplyAndReloadAsync` 完整实际方法继续原样提取。

本轮已离线缓存 restore，当前 Core/主Tests Release 0 警告/0 错误；主 runner **2912 全过/0 失败/0 跳过**。
Settings 的正式显式入口：startup-state-contract 61、startup-reapply-notice 34、settings-page-state 27、
settings-applicability 29、settings-screenshot-restore 14 均全过，互不相加为主 runner 覆盖。
`core-settings` 独立 154 项仍 153/1/0 原子 File.Move 权限错误，原失败日志保留，不重跑洗绿。
额外轨道读回 11 项因旧播放器构造边界少主干 DandanPlayClient 而在构造前全部失败；并非产品轨道断言已执行。
它在初始写集外，须经父授权定点补实际匿名依赖后重新编译/运行，不减少用例或改产品构造签名。

本树恢复/构建/运行仍用 `$HOME/.dotnet/dotnet.exe` 10.0.400，固定本机缓存、NuGetAudit=false 只用于
该次离线 restore。统一 flags：Release、`--no-restore -m:1 -p:BuildInParallel=false
-p:UseSharedCompilation=false -p:MSBuildNodeReuse=false`；`run` 只在当前编译成功后 `--no-build --no-restore`。
整方案首轮 0 警告/2 错误仅 natural caller 缺两个新旁支端点；未以部分项目成功冒充整方案绿。
没有启动 App/WinUI/UI、真实账号/服务器/media/libmpv、读取真实配置/密钥、发布或推送。
