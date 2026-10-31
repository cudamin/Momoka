# 输入框底色与中性焦点回归

[项目规则](../../CLAUDE.md)决定验证和交付范围；本页只说明这组检查。

## 覆盖

- [InputFocusThemeTests.cs](InputFocusThemeTests.cs)解析本树实际 Palette / XAML，检查首帧 Dark 与 Light 输入底色使用 Window、焦点使用 BorderStrong；读取实际 ThemeHost 源码，避免运行期切主题退回旧颜色。
- 检查文本、密码、搜索、数字、闭合下拉及快捷键局部覆盖。播放器弹幕输入沿用专用玻璃背景；不改展开下拉材质或高对比度系统资源。
- 对每个首帧字典注入强调色和删除焦点资源，检查回归判定确实拒绝它们。这个契约检查不等于真实 WinUI 渲染证明。
- [真实输入模板探针](../../src/Momoka.Shell/ShellNavigationProbe.InputFocus.cs)在五个存活主题的1040宽窗口，以及默认主题760宽窗口中，聚焦真实控件、回读模板边线与底色、核对输入值未变并保存实屏；普通、多行、密码、数字内外层、搜索、闭合下拉及快捷键框都核对与搜索相同的10 DIP圆角。旧圆角、卡片色、强调色与空画刷反例必须被拒绝。

## 命令

在当前工作树根目录使用 global.json 指定的用户级SDK，首次构建前先restore：

```powershell
$repo = (git rev-parse --show-toplevel).Trim()
& "$HOME/.dotnet/dotnet.exe" restore "$repo/tests/Momoka.Tests/Momoka.Tests.csproj" -p:BuildInParallel=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" build "$repo/tests/Momoka.Tests/Momoka.Tests.csproj" -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project "$repo/tests/Momoka.Tests/Momoka.Tests.csproj" -c Release --no-build --no-restore -- input-focus-fixture
```

构建当前Shell成功后，用本树实际Momoka.exe运行：

```text
--probe-shell --probe-section search --screen 1
--probe-shell --probe-section settings,shortcuts,playback --screen 1
```

两次实际窗口走查串行，真实屏幕截图必须对应该运行和最终代码。按实际显示器选择screen；可见GUI不能用隐藏启动状态来冒充屏幕覆盖。
`input-focus-{theme}-{width}-{control}-screen.png`为实屏，`input-focus-readings.txt`为模板读数。设置、快捷键与播放器输入来自另外三分区的正式页面。

## 边界

仅离线假服务、隔离设置和拒绝实际播放的测试外壳；不登录真实服务器，不提交真实下载、元数据保存或弹幕。
没有验证Windows实际高对比度切换、独占uosc输入或真实媒体播放。临时树通过不等于已合入master或桌面交付。
