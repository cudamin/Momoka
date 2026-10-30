# PERF-04 / PERF-09 图片专项生产回归

这是独立套件，不改主 `Momoka.Tests` 入口、csproj、sln 或共享回归清单；父整合任务统一登记。

## 严格入口与当前源码

在待验证的工作树根目录，用用户级 .NET SDK，先 restore/build 当前 Release，再运行：

```powershell
& "C:\Users\89400\.dotnet\dotnet.exe" restore .\tests\Momoka.ImagePerformanceTests\Momoka.ImagePerformanceTests.csproj --disable-parallel -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "C:\Users\89400\.dotnet\dotnet.exe" build .\tests\Momoka.ImagePerformanceTests\Momoka.ImagePerformanceTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "C:\Users\89400\.dotnet\dotnet.exe" run --project .\tests\Momoka.ImagePerformanceTests\Momoka.ImagePerformanceTests.csproj -c Release --no-build --no-restore -- perf-images
```

只接受一个精确参数 `perf-images`。空参数、拼错、重复参数均退出 2，不运行任何夹具。
编译从本树完整读取遮罩调用方法片段，记录六份产品源码 SHA256；运行前重新核对，源码变化退出 3
并报告 `SOURCE_HASH_MISMATCH`，不能靠旧 DLL 获得通过。父整合后须重新 build；其他播放区段的
source pin 由父按其最终 `PlayerViewModel.Events.cs` 定点核对，本套件不编辑已有 pin 或基线。

项目程序集名沿用已有 Core friend `Momoka.Tests`，仅用于匿名构建测试 scope，输出在本项目自己的
bin/obj 中。直接引用真实 Core；完整 compile-link `CoverBackdropImage.cs`、`CoverBackdropSize.cs`，
没有复制图尺寸、烘色、共享调度或 HTTP/store 算法。原图片缓存/烘色 22 项亦链接执行，和主套件
覆盖重叠，不得当成新增 22 项。新专项 41 项：共享预算 12、实际 Store 10、实际 WinRT/尺寸 11、
逐字生产遮罩调用方 8。输出逐项日志与 `work/perf-images/runs/<本轮时间>/results.json`。

## 安全边界与实测覆盖

- 实际 `EmbySession`、`EmbyHttp`、`EmbyClient`、`EmbyImageStore`、`SharedWork` 都执行；仅 HTTP
  handler 为内存脚本，地址限定 `perf-images.invalid` 且只允许图片 GET，不解析 DNS 或连接真实库。
- 不 SignIn/Restore/Adopt；内存注册无令牌、无密码的匿名 scope，保护器任意调用立即失败。缓存仅写
  本树 `work/perf-images/fixtures/<随机值>/images`，不读写用户设置、凭据或真实媒体记录。
- PNG 数据按行生成，不预先分配原大图像素阵列；实际 WinRT 解码覆盖 720p/4K/8K/方图/
  1×40000/40000×1/竖图，尺寸纯函数覆盖零值、uint 极值和固定种子一万种比例。
- 专用托管消息循环验证后台解码期间持续心跳；不是实际 WinUI 窗口/DispatcherQueue 或屏幕帧延迟。
  心跳断言是 1000ms 不挂死护栏，具体读数单列，不把机器抖动预算冒充产品帧时限。
- 调用方三段方法完整逐字提取，只替换遮罩 decoder 等待端点和 XAML BitmapImage；校验创建、
  源设置及发布的 UI 线程，取消/清空/换片/源退出、旧下载/解码/源设置尾、坏解码重试及继承顺序。
  底层独立 decoder 测试使用真正 WinRT，不由这些边界替代。
- 500 不同键、500 同键，以及单取消其他完成、零需求撤销、异常清理/新重试、坏缓存共享恢复、
  取消重试 delay、账号退出、chapter、只有一空槽时新需求优先均执行生产代码。
- 无 UIA、窗口、libmpv、真实播放、发布/快捷方式。真实 WinUI 的 BitmapImage surface、实际 DPI
  布局/UniformToFill、DWM 过渡、两种管线/后端与真实库网络仍由父串行验收。

## 产品预算与明确取舍

- 客户端两边各至多 1280、总像素至多 921600，BGRA 帧至多 3,686,400 字节（约 3.52 MiB）。
  等比向下取整、不放大，极端比例短边最低 1px；不裁改源图，原有显示端 `UniformToFill` 仍按屏框裁切。
  200ms 入场等待保持原行为，不是解码 deadline 或 UI 阻塞上限。
- 解码/烘色整个入口 `Task.Run`，所有 WinRT 解码 await 不取回 UI；上层保留 UI 同步上下文后才
  创建 BitmapImage，并同时设置 Physical 宽高。取消跨网络、WinRT await 和发布；Bake 自身最多一份
  921600px 的有限工作，取消在前后检测，未强行中止一次正在运行的 Bake。
- 返回 BGRA 帧有界，但不宣称 WIC/编解码器内部绝无原图分配；没有测第二份真实 XAML surface，
  不报告双份峰值或用不同 PNG 压缩/冷启动样本宣称精确速度倍数。失败无可信尺寸时两层退纯色，
  不再无界启动原图第二解码，之后允许详情/重新需求重试。
- 图片执行 6、排队键 48、已注册有效等待需求 128；`Running` 包含取消尚未退出的执行槽，最多 54。
  queued factory 未启动、没有每键 Semaphore 等待任务。单个热点键的等待者也受 128 总需求预算。
  调用者自己保存的已终结/取消 Task 不归队列保留，计数不是进程所有 Task 数量。
- 同键有效等待者共享一次执行，一个离开不撤销其他人。最后需求离开立即移除可加入键、取消工作；
  执行槽直到工厂及异步取消回调真正结束才归还，即使故意不合作也不会虚放槽突破执行上限。
- 新/续等排队需求优先；容量满淘汰最旧未启动整键（显式取消，不是“无图”），不抢占仍被需要的
  活动共享下载。总需求满且不能安全淘汰时，新等待立即取消，无隐藏无限等 admission。慢网超预算
  时较旧可见图可能要等容器再次 Ensure 才重试，新需求优先可能延迟较旧排队需求；这是内存/响应
  优先的明确取舍，不保证 FIFO 公平性或自动回填已淘汰可见图。
- 原 transport 12 connection slots 与每请求 30 秒 deadline 保留，不称无限连接；6 是图片请求执行
  预算（也约束 HTTP/2），不是全应用网络连接量。原 2s/4s 重试表保留，零需求取消包含重试等待。
- `SharedWork` 参数less/无取消 factory 的既有 completion-owned 契约保留；仅显式预算模式需求所有。
  检查当前生产调用者只有 image store，既有 SharedWork 测试继续通过。

## 参考实现与失败证据

本次没有复刻外部 UI。异步取消语义核对实际运行版本 dotnet/runtime `v10.0.11` 的
`System.Private.CoreLib/src/System/Threading/CancellationTokenSource.cs:305-354`：CancelAsync 在调用方
同步标取消，再在 TaskScheduler.Default 执行 callback；等待这个完成任务后才释放执行槽。注册移除
核对 `CancellationTokenRegistration.Unregister`，不用会等待回调的 Dispose 阻塞 UI。
WinRT transform 符号参考本树 HeroPictureLoader 实际调用，并由当前 Shell 编译/实际 WinRT 验证；
find-api 未索引 Windows.Graphics.Imaging，不能把其未查到说成 API 不存在。

首轮保留 `work/perf-images/test-first.log` / `test-diagnostic.log`：测试 XAML 边界对不能 clone 的
MemoryStream 适配器误用 GetInputStreamAt，6 个调用方用例红；修边界为直接读 IInputStream，不改产品
也不删断言。新增 Store 优先级首轮红日志另保留；只释放一槽再读谁先执行，避免把同时空6槽后的
独立 worker 到达顺序误当调度契约。所有必需断言保留，最终源码重新编译、重跑后才提交。
