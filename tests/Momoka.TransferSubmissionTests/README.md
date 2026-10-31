# 整理提交前失败的离线回归

本项目编译链接真实 `MoviePilotReorganizeDialog.xaml.cs`，引用当前 Core 并执行实际 `TransferSubmitAsync`。XAML／WinUI 控件与 dispatcher 只是无窗口边界替身，不复制提交、预览或恢复逻辑。

- 以匿名内存会话初始化服务，保护器和登录端点均拒绝访问；不读设置文件、DPAPI 或真实凭据，不访问网络，不移动文件。
- 只读 history 的 404、JSON／schema 错误、断线、500、依赖核验错误必须零实际写，恢复编辑／新预览；已消费的旧预览不能再用。
- 写后丢回执、取消与格式错误继续锁定并保留跨预览防重；明确写拒绝不误归类为提交前失败。原记录改变、软链接依赖和连接取消仍有守卫。
- 剧多选交接只收起重复查找区，显示实际选择数量；单集未传预选记录时保留历史查找与原季集值，构造不写。真实选择与批量预览由 Shell MoviePilot 分区探针另验。
- 内存处理器同步完成；测试只检查已完成任务，遇到未完成任务直接失败，不阻塞 UI 续体、不靠超时或重复运行洗绿。

在当前工作树先编译再执行：

```powershell
& "$HOME/.dotnet/dotnet.exe" restore ./tests/Momoka.TransferSubmissionTests/Momoka.TransferSubmissionTests.csproj --disable-parallel -m:1 -p:BuildInParallel=false
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.TransferSubmissionTests/Momoka.TransferSubmissionTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.TransferSubmissionTests/Momoka.TransferSubmissionTests.csproj -c Release --no-build --no-restore
```

`MoviePilotTransferSubmissionTests` 的 Core 场景另注册在主测试中，定向入口为 `moviepilot-transfer-submission-fixture`，重叠覆盖不累加。当前程序不需要 Shell DLL，不启动 App／WinUI；布局、输入、完整弹框生命周期与真实 MoviePilot 整理均没有运行覆盖。

## 手动整理窗口回归

同一项目也编译实际 `MoviePilotReorganizeDialog.Form.cs`，覆盖自动路径匹配只展示、显式使用/清空的整组配置、可空分类/刮削覆盖、自动来源、类型/历史切换的身份保留、季集定位/偏移/剧集组进入真实请求、编辑撤销确认，以及取消零写入。XAML接线用当前工作树文件检查三个操作在滚动区外、主要选项不藏在Expander。边界控件不是WinUI，不能证明渲染/事件时序；实际窗口由 `--probe-shell --probe-section moviepilot --screen 1` 的独立手动整理用例检查，匿名假HTTP、不操作真实文件或媒体库。运行须等待新PID所属完整报告，不取“最近一个目录”充当这轮证据。

## 单集直接删除回归

2026-10-31 用户要求集页面点击删除范围直接执行、只提示结果。同一项目 compile-link 实际 `ShellPage.MoviePilotMediaManagement.cs`，替换原生 Frame／通知／未使用对话框边界；Core 服务、媒体文件收集、历史核对、四档 DELETE 与防重账本使用当前源码，所有 HTTP 仅经过拒绝未登记请求的匿名内存传输，不登录、不访问凭据、不删除真实文件。

新增 22 项覆盖四档零弹窗／一次请求、已发后的换档拒绝、部分成功／丢失／损坏／旧版无分项回执、准备与提交复核时导航／直接 Content 更换／来源 A→B→A／来源退出／连接撤销／停止取消，以及最终事件与在途所有者释放。新实际 WinUI 检查由 `ShellNavigationProbe.EpisodeDelete.cs` 接入现有 MoviePilot 媒体管理用例，交付会话串行运行；本控制台边界不证明原生菜单点击、Frame 事件、通知像素或真实服务器删除。
