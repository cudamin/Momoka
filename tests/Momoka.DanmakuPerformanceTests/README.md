# PERF-05 / PERF-07 / PERF-15 离线专项夹具

本轮同步提取加载方法的 `ResetDanmakuInput(preserveDraft: true)` 参数边界，记录送达值并新增同片重选保稿参数回归。完整LoadSourcesAsync与旧断言保持，边界不实现草稿清理算法；实际草稿保留由弹幕发送/来源专项验证，不能拿此参数测试当真实输入框验收。

独立入口，不修改 `Momoka.sln`、原有测试 `Program.cs` / csproj、共享 Lua 工具或批准清单。构建引用本树实际 Core，链接原有 `DanmakuTests.cs`，并在编译前从实际 `PlayerViewModel.Danmaku.cs` 提取完整 `LoadDanmakuAsync`。只替代 UI 呈现、播放身份和宿主发送边界；不将它当作真实 WinUI 或 native 播放验收。

## C#（先 Release 编译，再运行）

在当前工作树根执行；首次 restore 指向已安装的本地包来源。项目 `NuGet.Config` 清空网络来源。下例只读用户级 NuGet 包缓存作本地源，所有新状态写本树 `work`；可用另一个已准备的本地 nupkg feed 替换。需要 Core 固定的三个分析器包，缺包时失败，不自动下载、降级或关闭生产分析器。

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) 'work/perf-danmaku-lua/dotnet-cli'
$env:NUGET_PACKAGES = Join-Path (Get-Location) 'work/perf-danmaku-lua/nuget-packages'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& 'C:/Users/89400/.dotnet/dotnet.exe' restore ./tests/Momoka.DanmakuPerformanceTests/Momoka.DanmakuPerformanceTests.csproj --configfile ./tests/Momoka.DanmakuPerformanceTests/NuGet.Config --source "$env:USERPROFILE/.nuget/packages" --disable-parallel -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& 'C:/Users/89400/.dotnet/dotnet.exe' build ./tests/Momoka.DanmakuPerformanceTests/Momoka.DanmakuPerformanceTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& 'C:/Users/89400/.dotnet/dotnet.exe' run --project ./tests/Momoka.DanmakuPerformanceTests/Momoka.DanmakuPerformanceTests.csproj -c Release --no-build --no-restore -- --output ./work/perf-danmaku-lua/cache-final-new
```

只有前一条成功才运行后一条；证据目录必须全新，旧结果保留。`--help` 显示用法，未知/重复/缺失参数退出 2。`--measure-only` 只收集公开 API 样本，不代表通过回归。每种 40,000 条负载（24/300 字符）预热后测 5 次：FIFO 队列、调用返回/总耗时、线程、缓存命中和 HTTP 计数。合成 handler 没有 inner handler，不创建 socket；凭据是固定占位值。不读取用户 settings/cache。保留自己的缓存用于复核，不递归删除其他路径。

C# 新用例涵盖大缓存 UI 让出、预取消、后台读取消与闸门释放、排队取消、损坏缓存重取、非法项和同时间稳定排序；真实宿主加载方法涵盖旧片结果、被替换请求、不可用时迟到结果及 clear 前置边界。原有 40 项弹幕测试同时验证签名、重定向、凭据轮换、发送授权/不重试和缓存失效，全部使用进程内替身。

## Lua（随包 LuaJIT，无媒体/窗口）

本树 `libmpv-2.dll` 必须与固定批准清单的 native entry 匹配；新树只取得相同批准字节的副本。夹具只初始化 `config=no / load-scripts=no / vo=null / ao=null / video=no / audio=no / idle=yes` 的 libmpv context，脚本路径只指本轮生成的合成脚本，不启动用户 `mpv.exe`，不加载任何媒体或网络。

```powershell
py ./tests/Momoka.DanmakuPerformanceTests/run-lua.py --root . --output ./work/perf-danmaku-lua/lua-final-new
py ./tests/Momoka.DanmakuPerformanceTests/run-lua.py --root . --output ./work/perf-danmaku-lua/lua-real-timers-new --real-timers
py ./tools/test-uosc.py --root . --output ./work/perf-danmaku-lua/uosc-final-new
# 预期退出 1；验证“断言失败不能算通过”的工具出口，保留失败证据。
py ./tests/Momoka.DanmakuPerformanceTests/run-lua.py --root . --output ./work/perf-danmaku-lua/lua-failure-new --inject-failure
```

Lua 专项采用真实脚本、真实 LuaJIT/JSON、假时钟/字幕属性/OSD 计数；链接原有功能测试中的实际 libass 接收检查。`--real-timers` 另开两个空闲 context，实际使用随包 Lua timer event loop，验证暂停尺寸稳定、文件取消、EOF 停轮询和新 cue 恢复；它不加载媒体，字幕/尺寸/overlay 仍为合成边界。不能与 `--baseline-source` 合用。

`run-lua.py --baseline-source <目录>` 只取明确命名 `baseline-momoka_danmaku.lua` / `baseline-momoka_subtitle_translation.lua` 的基点副本，可复测同夹具旧行为，结果标注 baseline，不能冒充最终修复测试。

- PERF-07：四万条/十次 20ms 连续 resize，静止零重排，快速变化只在稳定后一次重排；持续拖动 500ms 上界，暂停仍能 settle，seek 立即回显及强制刷新待定尺寸，宽高无效/返回原尺寸，旧 timeout 跨 clear/config/start/end/后继文件拒绝，文本宽度/转义复用且不采信 JSON 的 derived fields。
- PERF-15：暂停静态/clear/EOF 三段 60 秒计数；相同 cue 不再序列化，同画面不再格式化或提交；新片/新字幕/清除后恢复；字幕时间/轨道/显隐立即失效；相同/不同译文、字号/尺寸提交；晚 load-script 无需重放 file-loaded。播放期间仍保留 20Hz 属性校验，**不是零成本**；EOF/卸载时停轮询。

## 参考实现证据

既有 ASS 转义与随包 `assets/mpv-ui/scripts/uosc/lib/utils.lua` 的 `ass_escape`（反斜杠与花括号分支）逐行核对；本次仅缓存既有结果，不改变换行政策。timer kill/resume/one-shot 及 overlay update/remove 的 Lua 源码读取自批准 `libmpv-2.dll` 内嵌默认实现，固定 DLL SHA256 `fe583af31036bf3f3adacf6659e1bd448b568091b0334675573a3e303ec89d17`，不是依据上游文档猜 API。实际 native-timer 用例另证其当前运行语义。

## 集成与批准边界

本树不发布、不改桌面快捷方式、不合父树或 master，不 push、不杀进程、不修改真实媒体/账号/设置。`assets/mpv-runtime/approved-payload.json` 原样保留；这两份已变 Lua 的旧批准 size/SHA 必然仍指向基点。父会话审查每文件差异和测试后，逐项更新这两条 UI 批准项并实际执行完整批准校验；不得从待测整体扫描重建清单或刷新 native/其他批准项。

合成 CPU 计时不等于真实播放帧延迟。resize 的全片重排成本依然存在（只合并次数、缓存文本并复用工作表）；文本派生缓存增加有限内存，拖动时 lane planning 最多滞后约 500ms，画布立即更新。主树全量测试、适用运行/截图、最终批准校验和交付由父会话以整合后的代码完成。


## 2026-10-30 客户端关停反证修复

Task.Run热缓存使Dispose与读取finally并发，不能只取消当前VM的一票后立即释放semaphore。正式DandanPlayClient在每个公开读/搜/发入口登记完整operation，统一ShutdownAsync先关入场并取消，再等所有operation退出才释放HTTP与gate；IDisposable不堵UI，应用VM真正关停先await。包括已被替换的旧票，已有发送仍不重试/重发。新增3项实际client及原样LoadDanmakuAsync关闭反例，与原49项合计52全通过；不靠catch ObjectDisposedException遮掩未排空。shutdown触发的已发写仍可能结果不明，不宣称server撤销。
