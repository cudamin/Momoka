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
