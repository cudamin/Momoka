# R-041 缺集清单搜索可用性回归

本项目已最小注册到方案构建，仍需独立运行；不改主测试 Program 或共享认证。产品修复仅在 Shell 的缺集方法补读后、构造清单前复核本次捕获的 MoviePilot 连接；不认领新连接、不扩大 AniBT 写授权。

## 实际执行的源码与替代边界

- 每次构建从实际 Shell 源码原样提取完整 `ShowMissingEpisodesAsync` 和其显示中字段；唯一签名、前后成员与词法大括号边界全部验证。提取失败终止构建，不回退到旧输出或复制的模型。新的不支持字面量会明确拒绝，需人工核对提取器。
- 完整 compile-link 实际 `MissingEpisodesDialog.xaml.cs`，包括构造、列表筛选、真实行的两个 Visibility 值、选择检查和关闭流程。实际 XAML 作为资源校验按钮/来源下拉容器的绑定、Tag 和处理程序。
- 实际 Core 的 EmbySession/EmbySessionScope/ExecuteAsync 和 MoviePilotService Available/IsCurrentConnection/AvailabilityChanged。用反射安装匿名内存身份，并调用真实可用性 setter；绝不调用登录、恢复、连通性/产品检测或保护器。保护器记录并拒绝所有调用。
- 假 HTTP 只允许匿名详情和父剧两条 GET，可分别挂起；MoviePilot HTTP 全部拒绝。真实取图委托的原 scope 失效边界另有断言，正常案例不给图片数据，不解码。
- 仅替换 WinUI 的值类型、XAML 生成控件、ContentDialog Show/closed 时机及 dispatcher 队列。处理程序调用是实际源码，但并非用户的实际点击。搜索端点只记录交接参数，不启动资源窗口或客户端搜索。

因此本项目证明实际控制流与 source-linked 行/列表可见性参数及 XAML 绑定边界，**不证明 WinUI 像素、布局空隙、键盘焦点、命中测试、真实点击、系统事件时序或资源窗口运行**。未运行真实应用、native 探针、服务器、配置/凭据/DPAPI、播放或提交。

## 离线命令

从本项目所属工作树根目录运行 PowerShell 脚本。默认 SDK 为用户指定的 10.0.400。

~~~powershell
& '<当前树绝对路径>\tests\Momoka.MissingEpisodesAvailabilityTests\Run-Fixture.ps1' -Restore -BuildShell
& '<当前树绝对路径>\tests\Momoka.MissingEpisodesAvailabilityTests\Test-Extraction.ps1'
~~~

首次 restore 仅将本机 fixed 包归档按哈希冻结到本树，再用只有该本地源的配置还原；缺包即停，NuGetAudit=false 只在该次命令上。CLI home、TEMP、NuGet 和日志均在本树，且 SDK 执行前关闭 ASP.NET 证书生成。NuGet 解压目录使用本树短路径 `work/r041/n`，避免 XAML 编译器的长路径限制。脚本不安装依赖、不访问外网。

全部构建是 Release、--no-restore、单节点、禁并行/共享编译/节点复用。只有本次成功构建夹具后才执行 no-build；BuildShell 仅编译 x64 Shell，不运行应用。

## 基点红与修复绿

红源必须通过 Git blob 写入本树匿名证据输入，**不覆盖产品文件**，并传入明确的 SHA256 和提交号：

~~~powershell
& '<当前树绝对路径>\tests\Momoka.MissingEpisodesAvailabilityTests\Run-Fixture.ps1' -Phase red -ShellSource '<本树基点blob绝对路径>' -ExpectedShellSha256 '<精确SHA256>' -SourceRevision c94bd18dc41b862ab90878bcb247c6e0319478bc
& '<当前树绝对路径>\tests\Momoka.MissingEpisodesAvailabilityTests\Run-Fixture.ps1' -Phase green -BuildShell
~~~

构建记录完整源码、提取方法、Dialog/XAML 哈希；运行先核对这些实际输入，JSON 另记录夹具/Core DLL 哈希与逐项结果。对比必须使用同一最终夹具，不将夹具早期编译错误当成产品红。

58 项用例覆盖：进入前关闭/未验证/已断线，详情等待期间断线/换连接，详情完成而父剧仍等待期间变化，正常原连接、父剧动画资格与关键词、恢复与显式重开、不自动认领新连接、旧页面或原 scope 撤销、显示中 AvailabilityChanged 撤销、选中并真正关闭后的再次检查，以及原 scope 取图、清单本地筛选和重复进入保护。逐项断言假请求次数/方法和禁止器，真实搜索/写请求为零。

Test-Extraction 的 10 项与上述 58 项分开计数：1 个合法输入成功，9 个缺失/歧义/扩大/失衡/不支持字面量/缺哈希/哈希不匹配/越树输入必须拒绝。它只执行构建的真实提取目标，保留反例源和失败日志，不启动测试程序集或应用。

证据均在本树 `artifacts/r041-missing-search-availability`；含初次夹具与 Shell 构建失败，不删掉旧日志。批次完整 Core、SelfCheck/mutex、真实 UI 和发布统一留主线程整合后，未完成这些不等于完整产品交付，桌面快捷方式仍是旧版。

## 主树限定整合证据

来源提交 `ddb0aaf59e0bab989eda0ced5ca17211d8239f6b`，主树只复制产品缺集方法和本正式项目，不搬来源PROGRESS、bin/obj或包缓存。当前主树 `artifacts/r041-missing-search-availability/main-integrated-20261024-043314655/` fresh夹具／Core／x64ShellRelease零警告错误，58项全过／0失败／0跳过／退出0，匿名详情/父剧GET65，真实网络/MP HTTP/写请求/凭据调用全零。主树 `extraction-20261024-043514048/` 10项提取边界全过（9非法输入预期退出1），不与58项相加。全方案Release/x64空白0修改/退出0、无加载失败，22PS编码语法全过；索引与主树源哈希见 `artifacts/staged-repair/batch05/r041/`。来源配对红58项42过16败仍留原树，不冒为主树当前红。
