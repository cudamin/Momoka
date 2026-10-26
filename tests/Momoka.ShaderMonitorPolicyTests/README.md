# R-052 固定显示器自动方案与尺寸 watcher

独立离线专项，已注册方案构建，仍需单独运行。调用本树当前 x64 Release Shell 中真实 PlayerViewModel 的 PrepareShaderPlans、AdoptShaderLaunch、NoteSurface、Judge、ApplyShaderGroup、RestoreShaderPlan 和切链队列；实际 Core resolver、OutputWatch、SetShaderGroupAsync 事务不复制。只替代假设置、UI调度端点和内存 IPlayerControl，没有真实 App、屏幕或播放器。

## 验收口径

28项：窗口实际2048×1152保持不变，显示器2560×1440→2880×1800／2800×1575／3840×2160；全屏边界；固定全屏计划与恢复自动一致；同档换屏零写；普通拖窗／进退全屏不重复切链；在途事务挂起期间同屏进退全屏只一笔事务；手动钉链／关闭、偏好快照、未启用／8K／未知尺寸、加载票据Monitor与实际成功源Adopt、默认Active回差及四项纯Core策略边界。

OutputWatch默认保留通用Active＋回差；固定显示器调用显式选择Monitor＋fresh分类，与resolver／恢复自动一致。实际Output仍记Active供日志/OSD，Shader计划、launch票据和Adopt比较使用Monitor。SizeOnly仍只记录实际尺寸，只有离散跨档才切新方案；不因为1.5边界回差本身报错或改全局回差。

## 实际运行与来源

[MonitorFixture.cs](MonitorFixture.cs) 反射当前完整VM，构造后只安装内存_current／generation假handle，禁Start/观看上报；设置读取/导入/写入等旁支全部拒绝。真实事务读取profile/default/旧状态并发set，handle只内存模拟受理／拒绝和可控Task；没有原生调用。仓库 shader 文件只作为切链事务的存在性输入复制到本项目输出，不执行、不代替画质真帧。

启动核Shell绝对目录与当前Core DLL哈希一致，打印SHA256／MVID。两份输出不是同一Core就直接拒绝，不通过覆盖或放宽来源校验洗绿。实际VM及源/后端关系只装入内存，不构造EmbySession、凭据、保护器、真实配置/字幕/媒体文件，不登录、不Restore/探测服务器；无HTTP/native/WinUI窗口、桌面、播放、发布或推送。

## 命令

在当前工作树根运行，本次只用本机固定包缓存，缺包停，NuGetAudit=false仅restore，不改长期策略。先成功编译当前方案中的Core、专项、Shell；失败不运行旧输出。

~~~powershell
$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
if ((git rev-parse --show-toplevel).Trim().Replace('/', '\') -ne $root) { throw '必须在本树根执行' }
$dotnet = Join-Path $HOME '.dotnet/dotnet.exe'
$out = Join-Path $root 'artifacts/r052'
New-Item -ItemType Directory -Path $out,(Join-Path $out 'cli-home'),(Join-Path $out 'temp') -Force | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $out 'cli-home'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:TEMP = Join-Path $out 'temp'
$env:TMP = $env:TEMP
$project = Join-Path $root 'tests/Momoka.ShaderMonitorPolicyTests/Momoka.ShaderMonitorPolicyTests.csproj'
& $dotnet restore $project --source (Join-Path $HOME '.nuget/packages') --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false
if ($LASTEXITCODE -ne 0) { throw '固定缓存不足' }
& $dotnet build (Join-Path $root 'Momoka.sln') -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
if ($LASTEXITCODE -ne 0) { throw '当前方案编译失败' }
$shell = Join-Path $root 'src/Momoka.Shell/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Momoka.dll'
& $dotnet run --project $project -c Release --no-build --no-restore -- $shell $root
if ($LASTEXITCODE -ne 0) { throw '固定Monitor方案回归失败' }
~~~

空白以方案Release/x64加载，退出0但工作区加载诊断仍不合格。SourceExtractor最小显式加入方案配置，使Release方案不会误将该嵌套引用退回Debug。

## 红绿记录

主树基点9033af4上19项实际10过9败→统一target19全绿。补在途/fullscreen至24项23过1败：先处理所有Discrete导致相同方案排3笔事务；恢复同档SizeOnly不动后24全绿。最后加入四纯Core策略项28全过。分阶段用例重叠，不相加；失败与初夹具两个缺接口编译错误、当前Core/Shell来源不一致拒绝分别保留，不算产品有效红。

证据 `artifacts/staged-repair/batch05/r052`：baseline-solution-build／red-tests-03、expanded-build／expanded-tests-01、green-solution-build-02／green-tests-02、final-solution-build／final-monitor／final-core／final-subtitle-drop与current-whitespace。全程无实际显示器换屏、帧时序、GPU编译成本、视觉或native行为覆盖；只验证实际控制流与假事务，快捷方式旧版，非完整产品交付。

## 2026-10-25：第二轮纯合并基线测试兼容
本轮仍在 `C:/Users/89400/.codex/worktrees/merged-contract-tests/Momoka`，产品固定为
`4afca26672297aedd7725d675706d7f2b923519c`＋父 resolved/staged 的
`48c43fc20d5827fe5f5f147eb73e1d1ca7aa2795` 合并快照。629 份产品源均与上一轮清单一致；
未导入父新增 translation/runtime/UI/provider 文件或 `4a15f90` 原子写入修复。
本轮仅测试增量接在第一补丁 `968f2931e4d80346a649c19fd68e10ad91bd36e2` 后，
不提交生产快照，不动共享 `Momoka.Tests/Program.cs`、`Agreements.txt`、`Momoka.sln`。

使用本树实际 x64 Release Shell：
`src/Momoka.Shell/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Momoka.dll`，
2026-10-25 01:36:41 +08:00，SHA256
`b9125a7ff07a46e236603200c97dd0608fd548a84ba588d39431d99fbb5e67f5`。
本轮按固定用户 SDK 10.0.400 离线 restore，再成功 Release 编译当前 Core/相关项目后才 no-build。
Build flags：`--no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false`。
证据留本树 ignored `artifacts/verification/merged-contract-tests/batch02/`，不借父最终功能构建/桌面结果。

统一测试支撑 `Momoka.Tests/PlayerConstructorBoundary.cs` 精确核对实际唯一公开
PlayerViewModel 构造器的7个参数名、类型与顺序：playback/settings/session/images/shaders/ui/dandanPlay。
两处旧6参数反射构造改为按该签名调用，不把第7参数补null。

新匿名依赖使用真实 `DandanPlayClient`＋真实 `DandanPlayCredentials` 包装，偏好关闭、无密钥；
传输与保护器任何调用均抛错并计数，缓存路径含非法文件名，Dispose断言HTTP/凭据调用均0，
及时释放client。实际VM `_dandanPlay` 必须持有同一实例。原6个参数语义不变，images/session的
既有无用null边界不因此打开；没有运行弹幕加载、App、WinUI、服务器、配置或密钥。

新增4项构造边界反例：真实签名；旧6参数/新未审核必需参数拒绝；错名错序/错类型拒绝；
null或非真实第7依赖在调用VM前拒绝。原测试断言均保留，各runner的4项相同边界不能累计成额外12种覆盖。

此项目显式compile-link共用构造边界文件，不增加package、不加载完整main runner。
原28项全部保留，本轮 **32过／0失败／0跳过**＝原28＋4构造边界；真实Core事务仍与Shell Core哈希一致。
复跑按上文 `$shell $root`；本轮仅需当前专项/主Tests/Core相关Release成功，不把本轮结果叫完整方案/桌面通过。
实际monitor/窗口/GPU/Lua画面仍由父最终验收。
