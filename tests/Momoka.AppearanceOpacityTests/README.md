# 外观透明度专项

`C:\Users\89400\.dotnet\dotnet.exe run --project tests/Momoka.AppearanceOpacityTests/Momoka.AppearanceOpacityTests.csproj -c Release`

编译链接实际 `AppearanceOpacity`、`ControlBackgroundBrushes`、`ShellPrefs`；最小 WinUI 对象替身只提供画刷、控件和资源字典边界。覆盖零档保留原外观、只增加不透明度、100 档完全不透明、百分比与非有限值、颜色 alpha 与画刷 Opacity 的线性有效浓度、主题深色表面的 alpha 混色、100 档保持深色与三态区别、三态背景的局部副本、恢复与主题颜色同步、选择框聚焦、连播勾选、主页详情按钮及通知隔离。运行不创建窗口，不访问媒体服务器。

真实模板的资源查找、画刷绘制与跨窗口即时更新另由 Shell 探针验证；这里通过不等于屏幕证据。
