# R-051：切链事务的原生文件代次

本项目只验证 R-051 的控制所有权：**同一个 libmpv handle 换片后，旧切链事务和旧回滚不能写进新片**。
它是独立控制台跑器，已最小注册到方案构建，仍需单独运行；不加入共享 Program，不启动产品。

## 真实生产边界

- 直接引用当前 Core：运行完整的 PlaybackService 事务、Lifecycle 的 HandOver/Swap/接班/关闭路径。
- 每次构建原样提取整个 LibMpvHandle、LibMpvLifetimeGate，以及 LibMpvBackend.Run/Describe 的 UTF-8 命令 marshalling；LibMpvNodes/LibMpvFrame/MpvListValue 链接实际源码。
- 从当前 PlayerViewModel.Mpv.cs 原样提取 QueuePictureCommand 和 ChangeShaderGroupAsync，只替换字段/反馈枚举边界；因此 UI 不提交的断言不是复制已修模型。
- 提取标记须唯一且顺序正确，否则构建失败。编译时记录 backend/native/UI/service 的 SHA-256，测试时重新核对；不接受旧来源。
- PausingHandle 只透明转发。命令参数的真实 ToArray 枚举停点位于服务最后 Current 检查之后、实际 handle 读取 native generation 之前；不修改生产方法、不伪造 native gate。真实 HandOver 和 Swap 各推进一次 native 代次，Service 只推进一次，两者不能比较为相同计数。
- 原来的 10 项 ShaderTransactionTests 直接链接，不重写其事务。仅把测试构造器改为反射，使无 Core friend 权限的独立跑器也能使用拒绝 HTTP 的原构造边界。独立项目对该同步 TestHarness 采用与 Momoka.Tests 相同的 banned-awaiter 测试专属例外；生产分析器/基线不变，未压制 native 类型冲突。

## 覆盖

37 项（含上述 10 项，不与完整 Core 数量相加）：

- 正常切链、关闭、缺文件、缺默认、缺快照、无损文件列表、链末项排序。
- 拒绝/异常/部分失败的严格逆序恢复；回滚失败时 ShaderStateKnown=false，下一次完整成功恢复已知；真实 UI 的成功/失败反馈及钉选提交。
- 首笔/第二笔写入、第一笔 rollback 在 Current 后交接；B 的旧写和 rollback 为零，旧事务 false，真实 UI 不提交。
- native 已 Swap、Service 尚未接班的两种时序（旧事务恢复、新事务刚开始）；同句柄多次交换、交接中、关闭、销毁、新 handle。
- 普通命令已捕获 A 后在真实 RunFor 排队，换 B 后拒绝；命令原参数/结果/空命令；入口、枚举停点与执行点取消。
- 可选 IFileScopedPlayerControl 的旧捕获拒绝/新捕获接受、销毁和取消；仅普通 IPlayerControl 的外部后端契约仍可切链及控制。

## 运行

在本树运行（Windows PowerShell；SDK 和包源可通过显式参数提供）：

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Momoka.ShaderCommandGenerationTests/Run-Fixture.ps1
~~~

跑器仅从固定本机 NuGet 包缓存还原，缺包停，不访问外网。CLIhome/temp/packages/缓存/每次独立日志全部位于本树 work/r051；NuGetAudit=false 只用于该次 restore。
当前 Release build 必须成功且无诊断才运行 --no-build；单节点，不使用共享编译/节点复用。

## 证据与边界

本轮未修生产的精确基点为 d82ad11bd3593c1ee01c02fe7c2a03d0ac52e4e7。先保持真实 Service/Backend 不变并用同夹具得红；最终夹具还以精确 Git blob 作为单独编译输入配对，绝不覆盖工作区。来源红证据和编译/格式失败保留于来源树 `work/r051/logs/`，主树本轮结果保留于 `work/r051/runner-*` 与 `artifacts/staged-repair/batch05/r051/`，最终结果与来源在 [本题 PROGRESS](../../PROGRESS.md) 与 [修复增量](../../docs/代码审查-分阶段修复-2026-10-23.md)。

所有 native 返回值均为**合成端点响应**，不表示真实 mpv 接受了设置，更不表示 GLSL 编译或实际画面正确。这里只证明执行点的 handle/file 所有权、参数 marshalling 和托管事务控制流；没有 DLL resolver/初始化/事件线程/真实媒体/真实 native 调用。
匿名身份只装在内存中；拒绝所有 HTTP、凭据/DPAPI、配置 IO 和登录/恢复/重认证。原样 UI 边界不代表真实 WinUI/窗口/指针/布局；外部契约不代表真实 mpv.exe/IPC。完整 Core/SelfCheck、用户 mutex、播放探针、真实服务器、发布/推送由主树会话另行串行验收，本项目不代跑。

## 主树限定整合

来源 `ada1d3cc9e1135c9eb2d2b0b8291b33bf99c13aa`，主树只复制三份Core产品、原10项ShaderTransactionTests的构造边界及本正式项目，不拷来源PROGRESS/日志/旧输出。当前主树 runner37全过后重新当前方案Release构建，三处B旧写/回滚为0，完整Core2549／EndFile27／Monitor28／字幕拖拽74各自全过，不相加重叠项。缺失/重复gate主树提取反例各预期退出1，随后默认当前源码重编译和37项再验全绿。当前x64Shell／Core／专项零警告错误，全方案Release/x64空白0修改/退出0无加载诊断，23PS编码语法通过。主树证据在 `artifacts/staged-repair/batch05/r051/` 与本树 `work/r051/runner-*`；来源37项30过7败精确blob红仍留来源树。
