# 外观透明度专项

`C:\Users\89400\.dotnet\dotnet.exe run --project tests/Momoka.AppearanceOpacityTests/Momoka.AppearanceOpacityTests.csproj -c Release`

编译链接实际 `AppearanceOpacity`、`ControlBackgroundBrushes`、`ShellPrefs`；最小 WinUI 对象替身只提供画刷、控件和资源字典边界。覆盖两条滑块 0 档完全透明、100 档完全不透明、百分比与非有限值、颜色 alpha 与画刷 Opacity 的线性有效浓度、主题深色表面的 alpha 混色、100 档保持深色与三态区别、0 档常态不画底色而悬停与按下仍留薄白、三态背景的局部副本、选择框聚焦、连播勾选、主页详情按钮及通知隔离。运行不创建窗口，不访问媒体服务器。

真实模板的资源查找、画刷绘制与跨窗口即时更新另由 Shell 探针验证；这里通过不等于屏幕证据。

材质扩展：每态固定 AcrylicBrush，现有底色用 AlwaysUseFallback 与原有效 alpha 精确呈现，亚克力使用主题 tint；覆盖三态、checked/focused、零档与100、主题、来回切换和资源对象稳定性。亚克力的 0 档同样是什么都不画（tint 与 luminosity 都是 0），100 档回实底。替身不模拟合成器过渡，99.9→100及真实材质仍由运行截图核对。平台 BrushTransition 仅支持 SolidColorBrush，固定材质刷不再提供原83ms背景换色缓动。
