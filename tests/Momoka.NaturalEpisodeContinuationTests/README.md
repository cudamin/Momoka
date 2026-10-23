# R-046 自然 EOF 同窗口接续的托管源码回归

本项目执行真实 PlaybackService 生命周期以及完整原样提取的 LibMpvHandle、LibMpvLifetimeGate 和命令 marshalling；不复制同窗接续状态机。复用 [切链执行点专项](../Momoka.ShaderCommandGenerationTests/README.md) 的托管 native seam，仅新增匿名边界输入、自然 end-file 事件与 fallback handle。

## 显式所有权

- 调用方票据明确允许 RetainForAutomaticContinuation，当前自动下一集开启且仍是当前请求／可用来源，才尝试保留自然 EOF。
- 真实 handle 必须是未销毁／未 Shutdown／未停止、非自定义配置的可复用独占上下文。集成／外部／Error／Stopped／Quit 不暂存；原 EOF 结果和报告终因不改成 Stopped。
- 结果只交可释放的 PlaybackContinuation，不公开 native handle。后继取得原实例的责任后，旧 lease 失去停止权限；旧释放不等待 B 的播放时长，也不作用于 B 再次停在 EOF 的同句柄。
- 无下一集、准备失败或取消，caller await-dispose 回收；来源撤销／真实上下文关闭也撤销暂存。关窗信号与 EOF 分开，下一集 admission 在实际关闭信号后拒绝，不能等异步取消续体才阻止重开。

## 当前27项与缺口

覆盖自然EOF保留／末集释放／重复Dispose、同句柄Swap及旧lease迟释放、进度上报关闭时来源撤销、Stop／Dispose／撤销并发、旧请求取消后迟到取得锁不清B、lease排在后继gate后仍及时退出、Q／Shutdown／自定义配置／集成／外部／禁自动／未opt-in／非EOF／取消、loadfile拒绝并fallback失败以及parked拒旧shader事务。所有句柄、窗口身份、写入与回收计数仅托管端点响应；没有实际顶层窗口、真实 libmpv、媒体、IPC或服务器。

[实际VM caller专项](../Momoka.NaturalContinuationCallerTests/README.md) 在整合后另行运行，不能把上述Core/native seam当成完整StartPlaybackAsync的返回／递归／finally验证。主树尚未运行实际独占本地素材或 uosc 视觉检查，不能把假handle复用叫真实同窗画面通过。

## 构建运行

首次在当前工作树固定本机缓存restore，SDK以仓库global.json为准。CLIhome/temp/包与日志写本树，执行前关闭ASP.NET开发证书；NuGetAudit=false仅当次restore，缺包停。当前编译成功且零警告错误之后才no-build。

~~~powershell
$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
if ((git rev-parse --show-toplevel).Trim().Replace('/', '\') -ne $root) { throw '工作树根不符' }
$dotnet = Join-Path $HOME '.dotnet/dotnet.exe'
$out = Join-Path $root 'artifacts/r046'
New-Item -ItemType Directory -Path $out,(Join-Path $out 'cli-home'),(Join-Path $out 'temp') -Force | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $out 'cli-home'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:TEMP = Join-Path $out 'temp'
$env:TMP = $env:TEMP
$project = Join-Path $root 'tests/Momoka.NaturalEpisodeContinuationTests/Momoka.NaturalEpisodeContinuationTests.csproj'
& $dotnet restore $project --source (Join-Path $HOME '.nuget/packages') --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false
if ($LASTEXITCODE -ne 0) { throw '固定缓存还原失败' }
& $dotnet build $project -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
if ($LASTEXITCODE -ne 0) { throw '当前编译失败，不跑旧输出' }
& $dotnet run --project $project -c Release --no-build --no-restore
if ($LASTEXITCODE -ne 0) { throw '自然接续回归失败' }
~~~

当前初10项未修行为5过5败；首修9过1旧lease等待B超时，修后10绿。追加19项抓自定义配置资格与预期异常收尾，修后19绿；追加23项22过1败实际旧取消请求迟到消费B，修后23绿；再补两后端边界25绿；暂存前Q取消哨兵26项25过1败→26绿，继任首候选error误取消回退27项26过1败→27绿。注册数变化分别报告，不累加重复覆盖或把假请求／嵌套断言计额外案例。来源日志都留在主树 artifacts/staged-repair/batch05/r046，不读真实配置/凭据/DPAPI、不登录／恢复／发送HTTP或上报，不加载native／播放／App／UI，不发布推送；非完整交付，快捷方式旧版。

真实VM caller已限定整合并主树50项全过，提取非法输入8项预期拒绝后重编译当前源再验50；具体报告见上面caller说明。当前完整Core2549、切链37、来源撤销31、end-file27相关结果分开报告，不累加重叠项。
