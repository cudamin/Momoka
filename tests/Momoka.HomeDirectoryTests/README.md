# 媒体库目录到当前主页的快照交接回归

本项目在每次构建时按唯一标记原样提取真实 ShellPage 的 EnterShellAsync／LoadLibrariesAsync／GoTo／Open／ReleaseContent／RetryLibrariesAsync，并提取真实 HomeRequest；调用当前 Core 的 SourceLibraries／EmbySessionScope／EmbyClient／HomeLayout。Frame 只是无窗口导航与请求值边界，不复制目录或导航业务算法。

覆盖 Emby／Jellyfin 目录挂起→提前主页→合法结果后当前新请求持相同目录、未选页正常进入、已经转其它页不抢导航、访问／来源撤销拒绝旧结果、成功空目录、部分来源失败保提示、返回当前主页。FnNas／Plex 两项只使用真实 SourceLibraries 的已恢复内存目录＋另一来源挂起，覆盖聚合拓扑，不冒称它们实际 REST 协议验证。音乐库依旧过滤，目录与HomeRequest字段及HomeLayout的库排计划对应。

没有执行真实 HomePage.Attach／HomeViewModel 完整加载、页面渲染／Frame历史／滚动／焦点或UIA；快照到库／最近添加排的下游实际布局与HTTP加载属于运行覆盖缺口。

导航响应回归另外原样提取生产 BrowseTransition、HomePage 导航入口赋值、快照分支和 Loaded 回调。假 Frame 记录真实 Shell.Open 发出的转场参数；主页边界只替代网络、滚动与 HomeMotion，覆盖冷/暖/New/Back/Forward、重新冷访问和播放返回抑制的相互独立。实际动画像素、迟到货架与滚动由 performance/shell/playback 原生探针核验，控制台通过不替代运行验收。

原目录分组保留15个用例，关闭分组新增16个，共31项，先restore、编译再运行：

```powershell
$sdk = "$HOME/.dotnet/dotnet.exe"
& $sdk restore ./tests/Momoka.HomeDirectoryTests/Momoka.HomeDirectoryTests.csproj --disable-parallel -m:1 -p:BuildInParallel=false
& $sdk build ./tests/Momoka.HomeDirectoryTests/Momoka.HomeDirectoryTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& $sdk run --project ./tests/Momoka.HomeDirectoryTests/Momoka.HomeDirectoryTests.csproj -c Release --no-build --no-restore
```

目录分组的scope／profile／目录仅匿名内存；保护器拒绝，SettingsStore使用无效禁止IO路径，handler只接目录GET。该分组不登录、不读取真实配置／凭据DPAPI，不网络／后端／App／UI／播放。已在主页时沿用既有GoTo(force:true)作一次重建，代价是一次正常主页重载及滚动／选择状态可能重置，不抢其它页面导航。目录读取后手动重试的原路径未更改。

## R03-005 关闭期间账号生命周期

关闭分组链接完整真实 SignInViewModel／PageViewModel 和与 Shell 相同的 CommunityToolkit.Mvvm 8.4.2 生成器；另外原样提取当前 Shell 的 Shutdown、登录与来源通知、Start、Switch、聚合模式、ShowSignIn 及计数收尾。真实 Core Session／Gateway／SettingsStore 使用独立临时目录与合成占位凭据，HTTP handler 只接受 close.invalid；翻译、播放器、Frame 和 DispatcherQueue 是可控等待／排队边界，不重写登录采用或关闭业务。

16项覆盖翻译／播放器关闭等待中迟到成功及失败、再次提交、8个入口、关闭前已排队退出通知，以及已捕获来源的合法读取与计数／pending清理。UiQueue 驱动真实 VM continuation，所有关闭等待均在断言前释放并完成，重复关闭共用原Task。

本分组执行匿名登录及临时设置保存，不读取真实配置、DPAPI或服务器，不联网、不播放、不创建HostWindow或真实WinUI页面。可控停止端点不等于实际播放器／翻译收尾；真实主窗口关闭、页面像素及排队输入仍须由批次交付会话运行验证。

## 本轮匿名 UI 读数反例

FoundationProbeReadingTests 链接正式探针使用的 FoundationProbeReadings，接受一致的搜索、目录、计时与行标签读数，并拒绝11种缺代次、旧输入、分页漏项、空目录、固定标题入口、单张仍计时、增加第二张停钟及从属行残留／恢复缺项。它只验证探针读数判据，不能证明 WinUI 输入、真实 Frame、timer 或截图已经执行。测试总数从本次输出读取。

对应真实匿名用例：`--probe-shell --probe-section search,shell,library,settings`。新增当前搜索来源／迟到旧查询／Frame返回、迟到目录历史主页／固定标题／滚动恢复；既有轮播时钟与从属设置用例分别增加1→2→1启停、轮播关开四行与非默认值。实际窗口由最终交付会话串行执行，并按所选section范围报告；本项目通过不替代这些运行检查。

从属设置的宽窄用例另覆盖五个 HDR 数值手动→自动隐藏→恢复，外部 GPU→GPU-Next 的曲线与恢复项，以及精简模式四处辅助说明。MoviePilot 默认站点只提供匿名可用状态，不连接或读取站点；说明、标签及恢复控件从真实模板读取。增加这些切换与截图后，每档用例限时75秒，原断言继续保留。

本轮匿名探针前件（2026-11-07）：目录返回夹具用四个真实可见库及每库24条最新记录，必须有四个纵向货架和正ScrollableHeight后才测试原偏移恢复；水平卡数不能当高度。轮播像素比较在独立30秒停留阶段完成，随后用ApplyDwell(3)产生的实际_cycleStarted对三秒周期逐拍核对，未改计时器或原进度/换片判据。六项source-linked正反拒绝旧起点、超时样本、提前换片、不能滚与缺货架；真实WinUI仍由主树串行执行。
