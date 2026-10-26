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

只更新原第27项的退役行为预期，不删测试。主干
`ff31c732ff13876551119011c7270a2ccb3551cd` 按用户 2026-10-22 指令取消自动媒体版本回退。
实际当前 PlaybackService 只执行 ticket 选中的版；测试依然注入一个“如果被调用就成功”的备用后端，
但必须返回所选 B 的 Error，备用 MediaSourceId 未传入，BackendStarts 保持1（A已起，B原句柄复用），
未用备用句柄的 Dispose/Stop 均为0。不能通过恢复旧 candidate-loop 让旧绿重新出现。

保留并加强 EOF/接班/回收边界：A自然结束有 lease、retention 精确推进 native一次；B Swap从保留代次
再推进一次、Service只增加一场；非retaining失败B没有新lease，实际native销毁一次、服务停止、HTTP为0；
A旧lease重复晚Dispose仍不能重复销毁或启动备用后端。其他26项保持原样，包括重置失败后针对
**同一选中source重启上下文**的 fallback（不是自动选择另一媒体版本）。

原27项本轮 **27过／0失败／0跳过**。当前第二次 `build-natural-episode-02.log` 为0警告0错误。
新代次检查初稿错误把已经EOF的HandOver也多算一次，首跑26/1保留 `run-natural-episode.log`；
按完整生产 `TryRetainAfterEndOfFile`/`HandOver`/`Swap` 核实后分别断言真实两道增量，
没有修改native生产或放宽成范围/大于零。最终结果在 `run-natural-episode-02.log`。
复跑命令仍为上文当前项目 Release build/run；无真实mpv、窗口、素材或服务器。
