# 中性高斯与外观选色实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use executing-plans; tightly coupled UI paths are edited by one writer. Steps use checkbox syntax.

**Goal:** 实装用户已批准的双颜色拾色器及中性高斯背景，保证颜色保存/取消和亮暗文字可读。

**Architecture:** 使用 UiSettings 两个 RGB 字符串与现有颜色行/拾色器；临时预览颜色不进入 AppSettings，通过 ShellPrefs 单独暂存并通知。控件三态继续使用 ControlBackgroundBrushes 的原有浓度曲线，背景高斯改为轻罩，像素取样复用已有 BGRA 数据与 ThemeColor 色彩数学。

**Tech Stack:** .NET 10、WinUI 3、原有框架/分析器，不新增依赖。

**Spec:** ../specs/2026-11-07-appearance-colors-design.md，用户本轮明确「可以，实装吧」。

## Global Constraints

- 默认背景 `#808080`、控件 `#FFFFFF`；保留旧有效透明度与效果选择，不把 Demo 14.71/15 强加到已保存设置。
- 高斯遮罩 alpha = strength × 0.35，0 关处理，100 最糊仍保留图片；普通遮罩 100 实底。
- 两项颜色独立预设/HEX/拾色器；完成保存，取消/Escape/点外恢复；临时值不能被其他设置保存。
- 复用当前图片缓存，无额外服务器请求、播放或真实配置修改；五存活主题实际截图，高对比度服从系统颜色。
- 单树单写入；代理仅只读研究/复审，本会话编译和桌面验证。版本不改，不上传。

### Task 1: 设置颜色和独立选择事务

**Files:** AppSettings.cs、SettingsMigration.cs、ShellPrefs.cs、SettingRow.cs、SettingsViewModel.DetailCustomization.cs、HtmlColorPicker.xaml/.cs、SettingsPage.xaml/.cs、现有设置和颜色专项。

**Interfaces:** `UiSettings.ControlBackgroundColor` / `BackdropColor`；`ShellPrefs.AppearanceColor(bool backdrop, UiSettings ui)` 读取临时优先颜色；`SetAppearanceColorPreview(bool backdrop, string? color, UiSettings ui)` 设置/清除临时颜色并通知。`SettingColorRow` 通过确认模式与 preview/finish 回调隔离临时值，旧字幕行保持立即提交。

- [ ] 写并先运行默认/缺键/坏RGB回退/保存导入、预览与持久化隔离测试；预期新增断言失败。
- [ ] 实现两个字符串键与 Normalize：`HtmlColor.TryParse(value, out var rgb) ? HtmlColor.Format(rgb) : defaultColor`；没有 schema 语义迁移，不改有效透明度。
- [ ] 实现选择事务：`BeginEdit` 记录原色；`OnColorChanged` 编辑期间只走 preview，`CommitEdit` 才 `_write(Color); _save();`，`CancelEdit` 清预览并恢复原色；预览通过 ShellPrefs 独立字段，不写 live AppSettings。
- [ ] 设置模板传颜色行到原拾色器；确认模式显示十二预设、默认/取消/完成；Flyout Opened 开始事务、Closed 取消未完成事务。完整 HEX/RGB/HSL 行沿用已有输入精度逻辑。
- [ ] 编译并运行对应设置/颜色专项，检查已有字幕行直接保存不变，提交。

### Task 2: 高斯轻罩、自选控件色和局部阅读

**Files:** AppearanceOpacity.cs、ControlBackgroundBrushes.cs、DetailPage.Appearance.cs/.Backdrop.cs/.xaml.cs、DetailViewModel.cs、BannerSlide.cs、HomeBanner.xaml.cs、TextInk.cs、现有外观/图像测试。

**Interfaces:** `ReadingShadeAlpha(strength, true)` 返回 round(255 × clamp(strength) × .35)；ControlBackgroundBrushes.Apply 增加可选 `Windows.UI.Color? tint`，默认参数保留既有调用；BGRA区域取样从现有 HeroPixels 获取 ThemeColor，通过 Mix/Contrast 决定黑白文字。

- [ ] 修改旧固定重罩测试，增加 0/.15/1/NaN 轻罩、颜色三态、采样裁切、白黑文字与透明色合成回归；先看失败。
- [ ] 背景刷子的 RGB 从 ShellPrefs.AppearanceColor 取，色彩/浓度更新不重糊图片；亚克力 Tint/Fallback 同色。
- [ ] Apply 的 tint 在原有三态浓度计算后覆盖 RGB。前景用每个控件自己的刷子，不改全局 ThemeResources；闭合选择框文字/箭头随底图及自选色合成决定，展开列表继续主题前景。
- [ ] 详情正文/片名取实际裁切位置的缓存区域平均值，色罩合成后按黑白对比度择优；首次图、布局、滚动、设置变化重算，只做小数量固定采样。TextInk 阴影按前景深浅换对向色；信息卡保持局部面。主页详情按钮读取自选色，轮播背景规则保持现有。
- [ ] 构建 Tests/Core/Shell 和外观专项，定向测试通过后提交。

### Task 3: 原生验证和主树交付

**Files:** ShellNavigationProbe.DetailCustomization.cs 等现有 inspect 入口、相关测试/说明、PROGRESS.md。

- [ ] 扩展真实 inspect 检查两条颜色、取消/完成/点外、透明度端点、三态与闭合箭头、明暗背景、五主题，使用匿名假服务并复制运行配置；任何验证工具修改当轮实际运行。
- [ ] 当前 Release 主测试完整通过、颜色/设置专项通过、自动化句柄扫描、diff/编码检查；原生宽窄截图目视。独立只读整轮复审必要缺陷。
- [ ] 核对主树写入与程序锁；合入本批提交，保护主树新提交，重新运行受影响检查及当前完整测试。
- [ ] 主树 `tools/publish.ps1 -NoArchive -Shortcut`，实际发布件启动/检查、验证快捷方式目标和载荷；记录证据及未覆盖项，提交收尾。无上传。
