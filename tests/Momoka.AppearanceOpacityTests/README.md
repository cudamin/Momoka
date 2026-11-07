# 当前亚克力透明度与染色专项

当前正式背景和按钮/闭合选择框固定亚克力。整层Brush.Opacity按滑块调节，TintOpacity、TintLuminosityOpacity分别调节且默认55%。FallbackColor自动跟TintColor，平台省电策略负责回退。深色媒体文字固定纯白，浅色固定深色，不根据图片采色切换。下面旧两材质/高斯描述仅为保留的历史算法兼容测试范围，不表示仍有这些设置入口。

实际来源链接覆盖颜色、材质参数、0/20/40/99.9/100、三态/checked、画刷对象/字色稳定及通知/取消。运行不创建窗口、不联网；实际材质和系统省电策略须与源码/实屏证据分别报告。

# 外观透明度专项

`C:\Users\89400\.dotnet\dotnet.exe run --project tests/Momoka.AppearanceOpacityTests/Momoka.AppearanceOpacityTests.csproj -c Release`

编译链接实际 `AppearanceOpacity`、`ControlBackgroundBrushes`、`ShellPrefs`；最小 WinUI 对象替身只提供画刷、控件和资源字典边界。覆盖两条滑块 0 档完全透明、100 档完全不透明、百分比与非有限值、颜色 alpha 与画刷 Opacity 的线性有效浓度、主题深色表面的 alpha 混色、100 档保持深色与三态区别、0 档常态不画底色而悬停与按下仍留薄白、三态背景的局部副本、选择框聚焦、连播勾选、主页详情按钮及通知隔离。运行不创建窗口，不访问媒体服务器。

真实模板的资源查找、画刷绘制与跨窗口即时更新另由 Shell 探针验证；这里通过不等于屏幕证据。

详情控件按用户批准的0.2.0半透明白三态呈现，默认alpha15/21/8除255，闭合选择框共用按钮背景。参数覆盖0/5.88/精确默认5.882/6/19/25/50/72/100、两种材质来回切换、选中状态共底与画刷对象稳定；常态0不画，默认以下保留悬停21/255与按下8/255，默认以上连续补至100实底。白色前景、主题强调图标与展开亚克力由XAML契约及实际运行检查核对。

材质扩展：每态固定 AcrylicBrush，现有底色用 AlwaysUseFallback 与原有效 alpha 精确呈现，亚克力使用主题 tint；覆盖三态、checked/focused、零档与100、主题、来回切换和资源对象稳定性。亚克力的 0 档同样是什么都不画（tint 与 luminosity 都是 0），100 档回实底。替身不模拟合成器过渡，99.9→100及真实材质仍由运行截图核对。平台 BrushTransition 仅支持 SolidColorBrush，固定材质刷不再提供原83ms背景换色缓动。

背景图那一档：`AppearanceOpacity.Backdrop` 0..1 是强度，`ScrimAlpha` 把遮罩表上每一档按 255/0xB8 归一后乘强度（0 → 0，184/255 → 原表，1 → 255），`GaussianBackdrop.SigmaFor` 把同一档换成 sigma（0 → 0，1 → `MaxSigma` 24）。两者都覆盖百分比、越界与非有限值；遮罩表五档在「老观感」那一档逐个还原成原值。真高斯的并行实现由主测试与实屏探针覆盖，这里只测换算。

沉浸高斯的 `ReadingShadeAlpha` 非零档保留 alpha 224 深罩，零档仍关闭；纯白最亮输入下核对五主题正文及卡片字幕对比。普通模式仍读线性暗罩，首页不启用全图高斯。
