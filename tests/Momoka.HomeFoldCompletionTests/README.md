# R066 主页翻档完成／归属专项

轮播暖返回专项原样提取 HomeBanner.Loaded 的选片尾段及 Paint/PaintLogo：首访保留入场，第二次Loaded不再Rise，同步恢复缓存图直接可见，随后才到的背景图/徽标正常淡入；正常轮播保持交叉淡入。边界只替代控件值、Storyboard及Show的文本赋值，不创建窗口/WinRT图像。真实HomeReturn核文字和折叠位置不变、图片淡入最终完全可见；首帧门控探针另核延迟图片实际淡入。

独立控制台夹具，先编译当前源，再运行；不加入或运行全 Core／SelfCheck mutex 套件。

图片需求专项另原样提取 PosterCard.Begin 和 HomeBanner.Prefetch：首次绑定没有可视树时不发新图请求，挂树后按当前控件捕获门控；仍在树的回收容器继续可请求。这里替代门控查找和图片客户端，真实 Frame 所属关系由 BrowsePresentation 专项与原生探针验证。

## 覆盖边界

- 编译链接完整真实 `src/Momoka.Shell/Views/HomeFoldMotion.cs`。构建时以唯一首尾标记从当前 HomePage.xaml.cs **原样提取** fold 字段、Fold／量测／UpdateLibraryOverlay／ScheduleFold／ApplyLibraryChrome／HandBack、完成／取消以及 PreparePlaybackReturn／Release 方法；**不改、不测试 R062 目录快照**。找不到或标记不唯一即构建失败。
- UI 元素、预先给定的布局值、宿主 Content／Visibility、DispatcherQueue 与 Storyboard 是无 HWND／WinRT 的框架边界。Storyboard 可保留已排队 Completed、重复投递、控制 Begin 失败和 Stop 重入；Stop 先退回 Begin 保存的本地值，真实动画类再落定到 Y=0／Opacity=1。
- 默认档量测回归执行真实 ViewportTopAtScrollZero／LayoutTopIn／HeadOf／MeasureCoversLine，框架边界可累加到目标坐标系之前的父链位移。模板根入场 Y=20、整排及祖先正负位移、单个后续货架和滚动恢复都不能改变界线，也不能清除仍在运行的变换。普通导航的 Loaded／Frame 与实际混排仍由匿名 WinUI 探针覆盖。
- HomeViewModel 不复制：夹具只喂 LibraryOnBanner／Shelf／Slides 等读值，并在现有 SetLibraryOverlay 端口发布预先给定的布局快照。使用真实 Core 的 HomeFold／HomeCarousel 和 NullLogSink；混排目录、未用页面／设置／业务／播放器边界拒绝执行。
- 实际数量以本次运行 TOTAL 为准。新增同档媒体库视口为轮播读数留出右边距、读数隐藏后恢复及重复应用不另起动画。核心回归为压上动画完成 → 关闭轮播 → 宿主 Content=null、Overlay=Collapsed、当前横排媒体库一份；同时覆盖真正回程保留原载体、迟一拍在主动画完成后仍可跑、快速翻档／旧完成／规划失败／离页／播放返回／关闭动画，以及原 260ms、150+110ms 换手与 520ms 防抖等待。
- 没有启动 App、真实 UI／输入／截图、native 或播放器，不读取真实设置／凭据／DPAPI／服务器，不 publish／push／升级／放宽基线。本结果**不是**实际 WinUI 渲染、焦点、命中、帧时序或真实播放验收。

## 命令

轮播模式专项还源链接当前 `HomePage.Carousel.cs`，验证商店圆角留白、沉浸通栏/-48顶部扩展、媒体库左边距与标题档、空轮播恢复、设置对象归属和释放后的迟到回调。该专项只核对生产方法写入值；真实窗口剪裁、标题栏命中和滚动条位置仍由原生侧栏/收藏探针核对。

在当前树根运行；示例根是本项指定独立树，主线程整合时将 root 替换为自己的实际树。

```powershell
$root = 'C:\Users\89400\.codex\worktrees\r066-home-fold-completion\Momoka'
$dotnet = 'C:\Users\89400\.dotnet\dotnet.exe'
$project = Join-Path $root 'tests\Momoka.HomeFoldCompletionTests\Momoka.HomeFoldCompletionTests.csproj'
$shellProject = Join-Path $root 'src\Momoka.Shell\Momoka.Shell.csproj'

& $dotnet restore $shellProject --source 'C:\Users\89400\.nuget\packages' --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& $dotnet restore $project --source 'C:\Users\89400\.nuget\packages' --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& $dotnet build $shellProject -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& $dotnet build $project -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& $dotnet run --project $project -c Release --no-build --no-restore

$env:Configuration = 'Release'
Remove-Item Env:Platform -ErrorAction SilentlyContinue # Shell 项目自身已钉 x64，避免传给 Core 引用。
& $dotnet format whitespace $shellProject --no-restore --verify-no-changes --include (Join-Path $root 'src\Momoka.Shell\Views\HomeFoldMotion.cs') (Join-Path $root 'src\Momoka.Shell\Views\HomePage.xaml.cs')
& $dotnet format whitespace $project --no-restore --verify-no-changes --include (Join-Path $root 'tests\Momoka.HomeFoldCompletionTests\Program.cs') (Join-Path $root 'tests\Momoka.HomeFoldCompletionTests\PageBoundary.cs') (Join-Path $root 'tests\Momoka.HomeFoldCompletionTests\FrameworkBoundary.cs')
```

固定缓存缺依赖即失败；NuGetAudit=false 只在当次离线 restore，不写入项目或安全基线。编译失败立即停止，不运行旧 DLL。

## 本树证据

本地证据目录：C:/Users/89400/.codex/worktrees/r066-home-fold-completion/Momoka/artifacts/r066-home-fold-completion/（忽略产物不入 Git）。

- 基点 070d9783540757295bcc5a66daf3c81f4ff7abaa；修前 build-red-shell.log、build-red-fixture-2.log 均 0 警告／0 错误。red-regressions.log：51 项 **22 过／29 败／0 跳过，退出 1**。核心完成后关闭轮播与旧完成撤掉新载体均真实断言失败。
- 初次 build-red-fixture-1.log 是夹具缺少只读 Shelves 输入及假 Log 与 Core 类型冲突，已改用真实 NullLogSink；单列，不计产品红。
- green-regressions-1.log 与最终 green-regressions-final.log：同 51 项 **51 过／0 败／0 跳过，退出 0**。最终 build-green-shell.log、build-green-fixture-final.log 均 0 警告／0 错误；format-shell-release-diagnostic.log、format-fixture-release-final.log 均定向空白 0 修改／退出 0、无加载警告。首个 Shell format 全局传 Platform 让 Core 引用寻找 obj/x64，已去掉这个全局值并使用项目自带 x64；原日志单列保留，不冒称无警告检查。

## 最小实现取舍

2026-11-14 本夹具同时编译链接实际 `HomePage.Header.cs` 与 `HomeHeaderScrollState.cs`，原样运行主页的纵向滚动回调、外观同步、恢复关联号和 Release。新增两例覆盖下滚隐藏、上滚恢复、改颜色保持隐藏、模式切换、离页迟到事件拒绝，以及恢复完成只接受自己的关联号。框架边界的 ScrollView 只提供偏移、焦点和 TabStop 值；焦点辅助方法不发滚动请求的断言不能替代实际 WinUI 焦点/BringIntoView 和材质截图。71/71 为本次定向结果；第一次新用例在 Release 清空设置后又读取 page.Ui，夹具空引用已改为保留离页前的 Ui 对象，产品没有因此改变。

单趟 HomeFoldMotion 只加 active／complete 与一个 Completed 委托，终态幂等并摘事件；页面只加当前 owner 比较及一个迟一拍版本。主动画完成会清当前活动标记，但不会把属于这一趟的迟一拍误作旧回调；新翻档／取消才废弃它。仍然保留活动回程的保护早退，不改变动画曲线／时长、模板、阈值或用户布局体验，不引入通用动画框架。

主树限定整合时项目已最小入方案，当前51项全过／0跳过／退出0；日志 `artifacts/staged-repair/batch05/r066-*.log`，来源树51项不代替主树。主树一份框架夹具仅对象初始化换行修正，生产两文件与来源逐字一致。

## 2026-11-13 封面流光接线兼容

整合dff1bf2的HomeBanner加入PictureShimmer.IsActive后，本专项原样提取Apply/Paint出现2个CS0103：HomeFoldPage.g.cs 799/869缺PictureShimmer。只在现有BannerClockFixture/BannerReturnFixture增加共享ShimmerBoundary的IsActive值与停止读数，没有改产品、提取器或通过条件，也不复制Composition扫描实现。

新增两例执行原样生产方法：开场Paint(null)即使命中same-null早退，也必须先激活占位；到图关闭、再缺图/到图重复恢复，以及空Apply必须关闭正在等待的占位。当前本树Core/专项Release0警告0错误，68/68、0失败0跳过；定向whitespace及UTF8/LF/diff通过，证据artifacts/homefold-shimmer-boundary保留修前2错、修后build/run和format。

这里仅证明HomeBanner对流光值的需求和停止，不证明实际Shimmer的Composition视觉、帧或功耗。实际运行与主树交付仍由主协调会话验证；没有驱动桌面、连接真实服务、播放、改主树或发布。
