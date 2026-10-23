# R-040 聚合次级来源 AniBT 识别入口

这是独立、无窗口的实际源码夹具，已注册方案构建，但仍需单独运行。主树基点 `d82ad11bd3593c1ee01c02fe7c2a03d0ac52e4e7`（完整SHA以 [增量记录](../../docs/代码审查-分阶段修复-2026-10-23.md) 为准）；不运行 Core 全套、SelfCheck 或用户级 mutex。

## 实际执行边界

- [项目](Momoka.AniBtEntryTests.csproj) 编译链接当前 [ItemCommands.cs](../../src/Momoka.Shell/Views/ItemCommands.cs)、[CardItem.cs](../../src/Momoka.Shell/Views/CardItem.cs)、[IShellActions.cs](../../src/Momoka.Shell/Views/IShellActions.cs) 与 PosterCache。菜单 Show／Build、补读 Genres、实际点击分派来自生产源码，不复制业务判断。
- 每次构建原样提取当前 Guard／Ask、卡片事件DTO及 [HomePage](../../src/Momoka.Shell/Views/HomePage.xaml.cs) 的 MenuRead；所有起止标记唯一、缺失或反序立即构建失败。主页仅替代 realized-card 列表和 owner 值，不替代 MenuRead 的计划核验。
- 共32注册项：Emby/Jellyfin当前与次级、关闭/失联/撤销、两级版本搜索、四媒体类型 capability 保持、FnNas/Plex旧限制、迟到动画读取、来源恢复、来源监听释放、同步Opened先关闭，以及A→B→A期间旧click和GET先/后排队Hide均不复活旧菜单。
- 假UI只提供值、事件和可挂起的dispatcher。真实HTTP只允许匿名单集 Genres GET；没有真实AniBT/MP搜索窗口或服务器识别执行。Shell端点计数用于断言拒绝时连端点也未调用，不冒充真实对话框/服务器提交。

## 最小行为与代价

AniBT识别继续要求当前默认管理来源；非当前但仍在线的条目只移除此入口，MoviePilot与AniBT独立版本搜索仍保留。Build、延迟补行及Invoke分别检查；主页MenuRead使用同一能力参数。不会擅自扩大次级来源写权限。

已打开菜单在来源能力失效通知时立即锁定不可继续，再UI排队收起；切回原来源不会解除旧菜单失效，旧行点击与延迟GET都检查同一个标志。菜单关闭也失效，来源订阅在Closed释放；恢复需重新打开菜单，代价是一次正常浮层收起，不清偏好/在线来源/条目。

## 当前树运行

先核根目录。使用当前用户级SDK与仓库global.json；新CLI home执行前关闭开发证书自动生成。固定本机缓存缺包即停，NuGetAudit=false仅当次restore，不更改永久策略。

~~~powershell
$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
if ((git rev-parse --show-toplevel).Trim().Replace('/', '\') -ne $root) { throw '必须在本树根运行' }
$dotnet = Join-Path $HOME '.dotnet/dotnet.exe'
$out = Join-Path $root 'artifacts/r040'
New-Item -ItemType Directory -Path $out,(Join-Path $out 'cli-home'),(Join-Path $out 'temp') -Force | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $out 'cli-home'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:TEMP = Join-Path $out 'temp'
$env:TMP = $env:TEMP
$fixture = Join-Path $root 'tests/Momoka.AniBtEntryTests/Momoka.AniBtEntryTests.csproj'
& $dotnet restore $fixture --configfile (Join-Path $root 'nuget.config') --source (Join-Path $HOME '.nuget/packages') --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false
if ($LASTEXITCODE -ne 0) { throw '固定缓存还原失败' }
& $dotnet build $fixture -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
if ($LASTEXITCODE -ne 0) { throw '当前构建失败，不运行旧输出' }
& $dotnet run --project $fixture -c Release --no-build --no-restore -- $root
if ($LASTEXITCODE -ne 0) { throw '实际菜单回归失败' }
~~~

空白以方案Release/x64加载；退出0但工作区加载诊断仍不是完整检查通过：

~~~powershell
$env:Configuration = 'Release'
$env:Platform = 'x64'
$env:BuildInParallel = 'false'
$env:UseSharedCompilation = 'false'
$env:MSBuildNodeReuse = 'false'
& $dotnet format whitespace (Join-Path $root 'Momoka.sln') --verify-no-changes --no-restore --verbosity detailed
if ($LASTEXITCODE -ne 0) { throw '方案空白检查失败' }
git diff --check
if ($LASTEXITCODE -ne 0) { throw '差异空白检查失败' }
~~~

## 红绿证据与安全限制

主树 `artifacts/staged-repair/batch05/r040` 保留有效初20项12过8败→20全绿；增加pure能力/同步断线及真实主页MenuRead后28全绿。独立静态复核发现A→B→A排队失效漏网，追加32项29过3败，补即时失效后32全绿，不把重叠阶段相加。新夹具初期缺using编译失败和Tag元数据边界警告均保留，不当产品有效红、不压制诊断；当前绿色编译须零警告错误。

所有来源在内存直接安装，Select只变匿名_current及真实通知最终边界，不调用SelectSource/Persist、登录、恢复、产品检测或DPAPI，保护器访问即失败。目录只有本树空图像缓存／构建日志，不创建真实字幕或媒体文件。零真实网络、配置凭据、App、WinUI窗口、桌面/UIA输入、native/媒体播放、真实识别或共享写操作。不重试R001/003/005/031受限链，不升级/放宽基线/发布/推送；快捷方式旧版。属性和端点断言不证明真实菜单像素、排队时延或真实用户点击，未称完整产品交付。

主树最终当前方案19项目零警告错误，32项正式回归全过／0失败／0跳过；共享边界order66／Plex67和页面98／翻档51各自全过。完整Core2549全过，不把重叠覆盖相加。最终格式0修改／退出0、无加载诊断；具体最终日志与未覆盖范围见增量记录的R-040段。
