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

P8只读修正：ItemsView模板使用`x:Bind`填`PosterCard.Card`，外层ItemContainer未必有DataContext。结果定位改读实际子卡片身份并聚焦原ItemContainer，补离线正确/错误/空子卡身份验证，总8项；不以假的DataContext定位取得原生通过。Alt+F4阶段`minimumKeys=0`，系统关闭路径不冒称一定产生XAML键盘事件；其关闭/隐藏与主体恢复单独断言。

首次真实链已完成连接、用户名及登录后，账户Focus验收失败；原运行日志保持。定位起点现在先等实际布局，再用最长2秒的有限Focus/IsWithin读取等待导航重排；名称和角色断言不放宽。异常报告目标名/自动化ID、peer名称/角色、实际焦点类型/名、是否位于目标树、可见性和实际bounds，使空名称与焦点归属问题可区分。程序Focus仍不保证OS输入落点，根代理实际发送前须确认前台/输入句柄。

第二次真实链完成登录、账号菜单选择第二来源，但工具在来源已切换/主页就绪后立刻断言全树Popup数量零，误把关闭动画或其它提示浮层混为账号菜单。正式账号选择Click先AccountMenu.Hide再SwitchAsync；阶段现等待自身`AccountMenu.IsOpen=false`且包含AccountMenuBody的相关Popup退出，保留严格关闭验收，忽略无关ToolTip。共享SourceReady规则覆盖菜单开启/关闭过渡、错来源与未就绪，不修改产品账号菜单行为。

后续原生选来源已到B且菜单已关闭，但工具按键计数未增加：Flyout内容移到独立Popup，菜单事件不冒泡到ShellPage。现在同时在实际AccountMenuBody分支登记同一个PreviewKeyDown witness，finally解绑；source-ready继续要求真实按键增量，不改minimumKeys。挂载/解绑和严格来源阶段回归新增1项，实际修复后链由根代理重跑，旧超时报告保留。

## 用户手动验收模式

`InspectKeyboardJourneyManual`新增屏内中文步骤提示：每步说明“焦点已放到哪一控件”和实际输入/Enter/Tab组合，用户不用返回聊天等待下一条消息。提示在工具单独Auto行，产品Shell在下一行，避免遮住原标题按钮；设置独立窗口也临时增加工具提示行，finally恢复其原content。提示TextBlock具有Polite状态播报与标题语义，但这不证明Narrator实际读出。

屏内“取消验收（Ctrl+Shift+Q）”按钮可键盘到达；取消令牌终止当前步骤，finally解绑输入witness、还原工具占位及窗口。只有manual阶段等待上限为5分钟，普通工具仍60秒；manual Case仍15分钟，只有点击这个manual检查入口才将检查台外层驻留延到20分钟，其它检查路径保持8分钟。12步动作/匿名数据/零播放写入/真实按键判据不变，程序焦点起点仍单独记录，不把手动模式算全链纯Tab无需定位。

新增两条中文动作/时限与提示行/取消/解绑契约，当前KeyboardJourneyTests16项通过、0失败0跳过。原生屏内布局、用户输入和取消成功/失败路径须主控当轮验证，不以源码存在冒称已完成。

## 原生搜索控件名称

手动真实链完成登录、账号及切来源到搜索后，SearchInput peer的Name为空而role为Group；实际焦点是内部TextBox、within为true。LibrarySearchBox只有占位提示没有可访问名称，不能靠placeholder当标签。现在正式LibraryPage.SearchInput通过AutomationProperties.Name及现有x:Uid/resw提供“搜索媒体库”；SettingsSearchBox提供“搜索设置”并接resw，保留原占位提示与布局。

已读取Gallery固定`6de91fe...`的AutoSuggestBoxPage.xaml/.cs：基础样例直接AutomationProperties.Name，QuerySubmitted承接用户Enter。框架固定`693f472`的`dxaml/xcp/dxaml/lib/AutoSuggestBox_Partial.cpp`在模板接入时明确读取group的Name并传给内部TextBox（“Add the automation name from the group to the edit box”）。因此不换控件、不复制模板或在探针里补假名；保留原生名称传播。实际探针进一步检查owner及命名TextBox模板层的peer名相等，避免只有外层名而输入仍无名。

资源/XAML链回归新增1项，KeyboardJourneyTests17项全部通过。实际修复后peer名和后续手动流程由根本轮验证；用户此前已执行的6步与后续恢复第7步的运行范围须分开记录，不能写为同一完整运行链通过。
