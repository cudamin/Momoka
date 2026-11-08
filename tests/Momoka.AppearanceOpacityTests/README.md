# 外观透明度专项

`C:\Users\89400\.dotnet\dotnet.exe run --project tests/Momoka.AppearanceOpacityTests/Momoka.AppearanceOpacityTests.csproj -c Release`

编译链接实际 `AppearanceOpacity`、`ControlBackgroundBrushes`、`ShellPrefs`；最小 WinUI 对象替身只提供画刷、控件和资源字典边界。覆盖两条滑块 0 档完全透明、100 档完全不透明、百分比与非有限值、颜色 alpha 与画刷 Opacity 的线性有效浓度、主题深色表面的 alpha 混色、100 档保持深色与三态区别、0 档常态不画底色而悬停与按下仍留薄白、三态背景的局部副本、选择框聚焦、连播勾选、主页详情按钮及通知隔离。运行不创建窗口，不访问媒体服务器。

真实模板的资源查找、画刷绘制与跨窗口即时更新另由 Shell 探针验证；这里通过不等于屏幕证据。

详情控件按用户批准的0.2.0半透明白三态呈现，三态的薄白下限是alpha15/21/8除255，闭合选择框共用按钮背景；出厂常态浓度2026-11-08起改为39.25%（此前5.88%），由设置页那一行决定，这里只按传入值验证。参数覆盖0/5.88/精确默认5.882/6/19/25/50/72/100、两种材质来回切换、选中状态共底与画刷对象稳定；常态0不画，15/255以下保留悬停21/255与按下8/255，以上连续补至100实底。前景按实际背景、控件底色和系统回退选择深浅色；主题强调图标与展开亚克力由XAML契约及实际运行检查核对。

材质扩展：每态固定 AcrylicBrush，现有底色用 AlwaysUseFallback 与原有效 alpha 精确呈现，亚克力使用主题 tint；覆盖三态、checked/focused、零档与100、主题、来回切换和资源对象稳定性。亚克力的 0 档同样是什么都不画（tint 与 luminosity 都是 0），100 档回实底。替身不模拟合成器过渡，99.9→100及真实材质仍由运行截图核对。平台 BrushTransition 仅支持 SolidColorBrush，固定材质刷不再提供原83ms背景换色缓动。

背景图那一档：`AppearanceOpacity.Backdrop` 0..1 是强度，`ScrimAlpha` 把遮罩表上每一档按 255/0xB8 归一后乘强度（0 → 0，184/255 → 原表，1 → 255），`GaussianBackdrop.SigmaFor` 把同一档换成 sigma（0 → 0，1 → `MaxSigma` 24）。两者都覆盖百分比、越界与非有限值；遮罩表五档在「老观感」那一档逐个还原成原值。真高斯的并行实现由主测试与实屏探针覆盖，这里只测换算。

沉浸高斯的 `ReadingShadeAlpha` 非零档保留 alpha 224 深罩，零档仍关闭；纯白最亮输入下核对五主题正文及卡片字幕对比。普通模式仍读线性暗罩，首页不启用全图高斯。

最终材质探针核对（2026-11-07）：选择框的读取期望经实际 ControlBackgroundBrushes.ApplyForeground 得出，不再固定要求白色。source-linked 明/暗背景与相反色拒绝、材质回退、系统强制前景共三项；首页验证非零材质后零档透明及原反馈，旧零档强开亚克力判据必须拒绝。GaussianProbeReading 仅保留实际代次/半径/布局读数供失稳诊断，不代替原图片实例或像素断言，也不延长原运行截止。真实选择框文字/箭头、展开列表和首页三态仍待父会话从最终主树跑实屏验收。

选择框箭头诊断：材质探针在原前景判据前保存实际picker/glyph/fallback icon source颜色、本地Normal/Focused/FocusedPressed三键颜色及引用归属、焦点与模板状态、实际底色回退浓度到picker-glyph-reading.txt。类型不符或缺键原样记null/类型名，不当作通过；原前景失败和材质/弹层反例继续保留。两个source-linked原始读数测试验证异色、缺资源与非实色不被伪装。

详情自定义运行范围按Series/Episode/Movie分成三项，使用同一真实检查正文；每类型仍120秒，共同季页和导入/默认/首页检查只在Movie执行一次。source-linked样本选择检查三类型完整、无重复、未知或空及缺样本拒绝，不替代实际三项WinUI。

选择框字刷接线修复（2026-11-08）：真实模板在Loaded前已捕获箭头ThemeResource，后加同名键不会换掉其旧引用。ForDetail构造时提前建立选择框八个独立前景画刷，ApplyForeground只修改原对象颜色，使初始、焦点、展开箭头与字的可读前景同源；展开列表和禁用状态仍由平台管理。测试在模板捕获边界先读八键再切亮/暗/高对比，核无后插入/替换及原引用改变；故意替换key的旧引用反例必须拒绝。实际框架模板、箭头和fallback IconSource仍需主树运行核对，source-linked通过不是WinUI证明。

详情首次阅读前景验收前等待当前图片就绪、页面观察者有效、已排队颜色回填完成，以及六个实际可见文本的颜色与当前ReadingBackground经生产Foreground规则一致；仍使用原Until两秒上限，不主动调用PaintReadingColors。随后原逐文本Require不变，超时记录实际颜色/背景/布局并失败。source-linked判据拒绝队列未排空、旧图片、无观察者、空控件与错色，不能让未渲染页面算通过。
