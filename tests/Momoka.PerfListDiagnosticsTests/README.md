# PERF-06 / PERF-10 定向真实源码回归

2026-11-07 提取边界兼容：当前正式入口73项。夹具补实际 Core `RemovedMedia` 与原始 offset 字段，完整生产 `LoadAsync` 仍原样提取。新增已删项过滤不丢原始读取进度、reset 重载清旧 offset 的回归；当前 Release/x64 零警告错误、73/73通过。源码哈希、错误树、重解析路径、缺方法和守卫拒绝保持原有规则。

## 独立入口

- 项目：tests/Momoka.PerfListDiagnosticsTests/Momoka.PerfListDiagnosticsTests.csproj。
- 入口：Momoka.Tests.PerfListDiagnosticsProgram.Main；没有额外运行参数。
- AssemblyName 沿用 Momoka.PageContractTests，复用 Core 已存在的友元边界；本独立项目的 bin / obj 不覆盖原页面契约项目输出。
- 不调用主 Momoka.Tests / PageContract Program；父代理负责主方案、工具清单登记和真实 UI 验收。父集成将本题套件从 PageContract 子目录移到同名顶层项目，符合统一回归清单路径；只调整相对路径，测试与产品逻辑不变。

在当前工作树根执行，先成功编译当前 Release 再使用 --no-build：

~~~powershell
& 'C:/Users/89400/.dotnet/dotnet.exe' restore 'tests/Momoka.PerfListDiagnosticsTests/Momoka.PerfListDiagnosticsTests.csproj' --source 'C:/Users/89400/.nuget/packages' --packages 'C:/Users/89400/.nuget/packages' --disable-parallel -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& 'C:/Users/89400/.dotnet/dotnet.exe' build 'tests/Momoka.PerfListDiagnosticsTests/Momoka.PerfListDiagnosticsTests.csproj' -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& 'C:/Users/89400/.dotnet/dotnet.exe' run --project 'tests/Momoka.PerfListDiagnosticsTests/Momoka.PerfListDiagnosticsTests.csproj' -c Release --no-build
~~~

使用 x64 输出时，显式构建并运行该布局中的 DLL：

~~~powershell
& 'C:/Users/89400/.dotnet/dotnet.exe' build 'tests/Momoka.PerfListDiagnosticsTests/Momoka.PerfListDiagnosticsTests.csproj' -c Release --no-restore -m:1 -p:Platform=x64 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false -p:DebugSymbols=false -p:DebugType=None
& 'C:/Users/89400/.dotnet/dotnet.exe' 'tests/Momoka.PerfListDiagnosticsTests/bin/x64/Release/net10.0/Momoka.PageContractTests.dll'
~~~

## 真实源码与边界

- 完整编译链接 DiagnosticsViewModel、PageViewModel、UpcomingDay、UpcomingListDiff、CardItem、PosterCache、LibraryRequest；MVVM 使用与 Shell 相同已钉版 CommunityToolkit.Mvvm 8.4.2。
- 每次构建逐字抽取当前 LibraryViewModel.UpdateUpcomingDays / LoadAsync，并核对 QueryCurrent 表达式；构建任务从项目目录记录明确工作树根及实际输入路径，运行不再从 bin 上溯猜根。SourceLinkTests 比较记录的实际输入 SHA256 和当轮生成产物；源码改变必须重编译，不接受旧输出。根外输入及通往其他目录的重解析路径在抽取前拒绝，同字节外树源也不能冒充本树。
- Core 日期算法、LoadGeneration 和匿名 EmbySessionScope 实际执行。Library 夹具只替代无副作用读取端点、无关页面 chrome 与其他路由；SourcesAvailable 使用真实 scope.IsAvailable，不复制被测算法。
- P3 普通库窗口在本 Upcoming 夹具固定不适用；`WindowEnd` 使用真实卡集合数量与零起点，新增快照／锚点接口仅补编译边界，窗口读取若被误调用直接失败。真实 `LoadAsync` 仍由当轮生产源码逐字抽取，不在夹具重写窗口或分页算法。
- HTTP handler 一律拒绝；卡片不请求图片，解码边界一律拒绝；PlaybackService allowPlayback=false，后台工厂一律拒绝。没有登录、真实设置读写、DPAPI、账户或真实日志访问。
- 复用既有 DiagnosticsTests 39 项，不运行主套件；其中磁盘日志用例仍使用其原有独立随机临时目录。新增本题 / 源码关联 31 项，独立入口总计 70 项；诊断 Core 与父主套件重叠，不可叠加成不同覆盖。
- 不创建 WinUI 窗口或 Frame，不证明真实 dispatcher、渲染、选择、滚动、历史导航或用户体验。集合身份和通知操作断言不是 UI 验收。

## 计数、行为和取舍

- UpcomingPaginationTests.Register 调用真实 UpcomingListDiff.Apply，计数引用 comparer：10,000 旧项 + 100 新项恰为 10,000 次 Equals；审查原方法为 101,014,950 次。真实 UpdateUpcomingDays 同日 / unknown 分别断言只有 100 Add，旧 day / Cards 集合 / 每张 CardItem 引用不变。计数不含 Core 分组或日期排序，不是 UI 帧时长。
- 删除 2,000 -> 1,500 并反序，真实 helper 4,500 次引用比较；无 Reset、引用与顺序正确。最坏重排仍有 ObservableCollection 数组位移和 numeric position 镜像位移，总时间可能二次方；不声称完全线性，也不用 Clear 抹掉 identity。
- 普通分页每页一个 source-qualified HashSet，保留重复原始页项消耗 offset 的规则；实际 LoadAsync 回归同 ID 跨来源、旧 source 迟到、离页与来源撤销，保持 query/load 代次。
- DiagnosticsCoalescingTests.Register 使用实际 VM + RingBufferLogSink(1000) + 受控队列：万条只 Post 一次、消费后待执行 0、只留最后1000条；首读 / 增量均测。八线程12000条、执行中新增、交错消费、Post false / throw / 重入、回调异常复位、pause / Detach / 同sink重入 / 旧sink已捕获通知、Refresh和筛选重入、水位、WeakReference均测。
- SnapshotAfter 追平后仍为 O(1) 空结果；不改 Log.cs，不夸大为每回调扫描1000条或永久泄漏。解决的是空回调积压和队列强持 VM。
- 极端持续写入会使单次 Drain 多次补尾；只承诺每访问期最多一个待执行 / 执行唤醒，不引入定时器。Post拒绝会清pending，下一次合法wake-up补齐仍保留的尾部；不在关闭中的dispatcher上无限重试。

## 父代理真实 UI probe 对接

不新增产品测试 API，可用计数 dispatcher 包装和反射：

- DiagnosticsViewModel.Attach(..., IUiDispatcher ui, ..., RingBufferLogSink? sink)：wrapper 原样转发真实 Post；burst期间不泵队列，断言10000条 => 1 Post。
- VM字段 _logVisit => 嵌套 LogVisit；属主字段 _pending / _dirty / _active / _following / _owner（WeakReference<DiagnosticsViewModel>），Sink属性和OnWritten / Drain / Stop方法。
- VM字段 _logWatermark / _logInitialized / _logSnapshotVersion / _filterVersion；结果集合 Rows / Categories、LogCount / Following。
- Detach后 _logVisit为null、水位0；旧属主Stop置_active=false并清弱target。同sink重入旧回调不能消费新水位。
- WeakReference离页断言必须释放Frame历史、页面缓存和探针局部等其他引用；可保留旧日志回调队列，VM仍应回收，不能把其他合法持有者误归因为日志队列。

断言入口：UpcomingPaginationTests.cs、DiagnosticsCoalescingTests.cs、SourceLinkTests.cs；Main一次登记全部，无筛选参数。

源码抽取负例可传 build 参数 -p:PerfUpcomingSourcePath=<本树独立负例文件绝对路径>。缺方法 / 改query-source guard必须拒绝构建且不覆盖合法生成源；正常构建不传此参数。两种负例均实际运行，再编译正常入口，拒绝日志不算产品检查红项。

`Test-SourceInputs.ps1` 实际构建和运行默认及 x64 布局，再用本树独立副本验证过期哈希、缺方法与守卫改变，用外树同字节副本和本树 junction 验证错误来源拒绝，最后重建正式输入。脚本不修改生产源码；证据目录必须是本树 artifacts 下的新目录，负例输入和日志留作证据。当前入口均为71项，诊断Core用例与主套件重叠，不累加。

~~~powershell
& 'tests/Momoka.PerfListDiagnosticsTests/Test-SourceInputs.ps1' -DotnetPath 'C:/Users/89400/.dotnet/dotnet.exe' -PackagesPath 'C:/Users/89400/.nuget/packages' -EvidencePath (Join-Path (Get-Location) 'artifacts/perf-source-inputs')
~~~

## 当前证据

2026-11-04：P3 新增窗口接口后，本树抽取编译先红（缺 17 个边界引用）；只补 Upcoming 不使用的窄边界，未改产品或抽取算法。最终 Release 0 警告 0 错误，确切独立入口 **71/71**、失败 0、跳过 0；日志在 `work/p3-deep-return/perf-boundary-red-build.log`、`perf-boundary-final-build.log` 和 `perf-boundary-final.log`，交主代理整合后重验。

2026-10-30当前Release Shell / Core及定向套件0警告0错误；70/70、失败0、跳过0。日志在本树ignored work/perf-list-diagnostics/：shell-build-final.log、suite-build-final.log、suite-final.log、missing-method.log、changed-guard.log。中间比较数和Remove筛选重入失败已定位修复，不放宽断言；原始审查证据仍保留在用户指定的performance-audit目录。父代理按最终提交另行编译、真实UI队列和主树验收。
