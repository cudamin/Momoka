# P6 匿名真实键盘任务链

日期：2026-11-13。独立树`codex/winui-p6-reliability`，工具起点`1b3c98c`。此工具形成可审阅、可执行的原生任务链，原生成功/失败及截图由根代理当轮串行执行，未在子树驱动桌面。

## 场景与安全边界

检查台`InspectKeyboardJourney`创建正式ShellPage、SignInPage、LibraryPage、DetailPage和SettingsWindow/SettingsPage。HTTP只交给两个固定`.invalid`匿名域名，登录固定虚构用户`keyboard-user`/`fixture-password`；人工登录是唯一允许的POST，目录/搜索/图片均在内存回答，其他写入直接拒绝。真实媒体URL不存在，IShellActions播放记录后拒绝、PlaybackService禁止起播；不读用户账号/令牌、生产配置、不连接真实服务器。

来源切换使用正式账号菜单的原生Button，搜索使用正式AutoSuggestBox，结果使用实际Button/ItemContainer的导航调用，避开海报中间播放按钮。设置从正式Shell设置按钮打开，Alt+F4走SettingsWindow既有关闭→隐藏，之后Alt+Left走Shell既有返回栈。1280DIP登录/账号阶段、760DIP搜索结果/设置/返回阶段；中途关闭本次应用动画偏好，保持现有动效回退。

程序Focus只为每段定位起点，不等于用户独立走完Tab序列；`programmaticFocus=true`明确记录。每段关键动作等待根代理真实键盘输入，并记录XAML PreviewKeyDown增量；成功断言不能只靠UIA Invoke或调用生产方法代替按键。设置段要求Tab/Shift+Tab真实返回原输入位置。完整“全链不借程序定位的纯Tab遍历”仍单列缺口，不能据这条辅助定位链宣称已完成；Narrator实际读出、触摸/笔和高对比真实像素也另列。

## 根代理执行协议

每段生成`keyboard-journey-stage.json`，含本轮run、process、hwnd、stage、input；先确认目标窗口属于本轮进程、可见且处在前台，再向它发送所需输入。不向其它应用发送。`keyboard-journey-receipt.json`逐段保存当前页/来源、焦点/自动化ID、peer名称/角色、键盘事件次数、拒播/写入次数及`narratorTested=false`。

| 阶段 | 实际输入 | 成功断言 |
| --- | --- | --- |
| address-ready | 输入`https://keyboard-a.invalid`、Enter | 正式连接进入账号页 |
| username-ready | Ctrl+A、输入`keyboard-user`、Enter | 用户名提交并聚焦密码 |
| password-ready | 输入`fixture-password`、Enter | 正式登录一次、主页就绪 |
| account-ready | Enter | 正式账号浮层打开 |
| source-ready | Enter | 菜单选第二匿名来源，主页和身份匹配 |
| search-entry-ready | Enter | 打开正式搜索页 |
| search-ready | 输入`keyboard-query`、Enter | 一个第二来源影片，原查询匹配 |
| result-ready | Enter | 打开目标详情，播放次数零 |
| settings-entry-ready | Enter | 设置窗口正式导航就绪 |
| settings-ready | Tab、Shift+Tab | 实际键盘事件至少两次，焦点回设置搜索 |
| settings-close-ready | Alt+F4 | 正式设置隐藏，详情主体不变 |
| back-ready | Alt+Left | Frame恢复搜索、词与来源保持、零写入/起播 |

另有`InspectKeyboardJourneyFailure`把实际ServerBox自动化名称改坏，正式RequireName必须报红，预期失败保留原报告。检查台还增加四个独立复用入口：`InspectSignInKeyboardSurface`、`InspectInputFocusSurface`、`InspectCardFocusSurface`、`InspectHomeReturnSurface`，分别运行既有表单恢复、原生输入、卡片/设置名称及主页返回动效，便于逐项定位。

## 离线验证与未覆盖

`Momoka.KeyboardJourneyTests`直接compile-link共享匿名HTTP/读数规则，7项验证正确连接/账号、搜索来源、无媒体URL、错误密码、真实域名/未知写入/取消拒绝，名字/角色/焦点缺失与“没有新增按键却完成”的伪通过，以及最终查询/来源/登录/零播放/零写入回执。Core/Shell/专项Release编译0警告0错误，统一run-regressions入口7项通过、0失败、0跳过；9个文本文件编码/行尾/差异检查通过。

```powershell
./tools/run-regressions.ps1 -Suite Momoka.KeyboardJourneyTests
```

子树Shell编译成功不代表这条原生任务链已经走过；实际成功与预期失败回执由根代理补充。新增两张实屏（设置、返回搜索），不要把直接Focus/UIA属性读数当成Narrator或真实键盘全过程通过。
