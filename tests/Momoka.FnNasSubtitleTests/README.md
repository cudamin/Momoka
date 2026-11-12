# 飞牛服务器字幕专项

本工程直接引用当前 Core，并编译链接生产 `FnNasSubtitleDialog.xaml.cs`。HTTP、XAML 生成字段、控件值和文件选择器是匿名终端边界；字幕服务、协议、异步事件处理和关闭守卫均执行生产代码，不复制状态机。使用 `.invalid` 地址和占位令牌，禁止真实设置/凭据读取，不启动 WinUI、系统文件选择器、libmpv 或真实媒体服务器。

2026-11-12 当前 85 项：57 项字幕协议/服务/对话、14 项弹窗样式和取消约定、14 项启动参数（含新增 `fnnas-subtitles` 区域）。覆盖非首版本、普通/管理员权限、同名删除拒绝、完整快照变化、本地字节/32 MiB 限制、取消零写、明确拒绝与未知回执、迟到搜索、离页/注销、固定账号不随默认账号切换，以及确认成功后的只读恢复与跨版本刷新义务。

在所属工作树根执行：

```powershell
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.FnNasSubtitleTests/Momoka.FnNasSubtitleTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.FnNasSubtitleTests/Momoka.FnNasSubtitleTests.csproj -c Release --no-build
```

首次使用先按项目规则 restore。统一入口为 `tools/run-regressions.ps1 -Suite Momoka.FnNasSubtitleTests`，自动串行还原、当前编译和运行。

原生 `--probe-shell --probe-section fnnas-subtitles --screen 1` 使用真实匿名对话/ComboBox/Button、假 HTTP 与假文件选择器，1280/760 DIP 截图、UIA Invoke、权限隐藏恢复和写中关闭保护由主控串行运行。单测里的控件边界不能证明 WinUI 渲染、键盘/指针投递、真实点外取消、系统文件选择器或服务端处理。详情见 [第四批报告](../../docs/媒体库第四批-飞牛字幕.md)。
