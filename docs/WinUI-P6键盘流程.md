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

## 从搜索继续的手动入口

用户已经在此前真实运行完成登录、账号与切来源到搜索，`InspectKeyboardJourneyManualFromSearch`允许从第7步继续。此参数只供manual；默认完整入口仍依次等待前六步真实按键。恢复入口仅用固定匿名账号自动登录一次、播种第二来源与正式主页→搜索导航，回执每段标记`resumedFromSearch=true`、`seededIdentity=true`，并另写`anonymous-first-six-seeded`；这些setup操作不能算本轮手动验收。

屏内提示明确“前六步上轮已验证，本轮从搜索继续；本轮账号为匿名播种”。第7步搜索真实键入、结果Enter、设置Enter、Tab/Shift+Tab、Alt+F4与Alt+Left继续用原真实按键增量和最终查询/来源/零播放写入断言。取消写`cancelled`阶段并重新抛出，保留已执行步骤而不登记通过。恢复入口与此前运行只能分别报告范围，不能拼成同运行全链通过。

新增manual恢复限制、前六步播种/回执与取消读数两项源码/规则契约；实际后半成功与取消仍由用户/根代理本轮验证。

## 原生结果容器名称

用户从搜索真实输入`keyboard-query`后查询成功，但搜索分组的外Button peer名为空（角色Button、焦点within为true）。内层PosterCard有片名不保证外Button自动获取名称；ShelfCardTemplate、PosterTemplate与ThumbTemplate现将外导航容器的AutomationProperties.Name以OneWay绑定当前CardItem.Title。保留原Button/ItemContainer模板、选择与事件语义，不在探针Focus时造名字。

Gallery固定ItemsViewPage的各ItemContainer模板显式绑定AutomationProperties.Name到Title，框架已有PositionInSet/SizeOfSet位置语义，本修复不重写它们。页面契约新增实际模板绑定检查与正式CardItem展示标题/身份/原地标题通知验证；原生结果名称及Enter导航由主控本轮重验，原失败保留。

`InspectKeyboardJourneyManualFromResult`供用户已经实际完成第7步搜索后的继续验收，只manual播种固定账号/来源和同一匿名查询结果。从第8步结果Enter继续，之后设置/Tab/关闭/返回仍要求真实按键和严格最终回执。每段标`resumedFromResult=true`、`seededSearch=true`和`seededIdentity=true`，提示说明上轮搜索已验证、本轮账号/结果播种；不把本轮seed计作用户输入。默认完整/FromSearch入口仍保持其原验证范围。

## 手动提示遮挡与设置焦点修复（2026-11-13）

`061220368-25576`真实运行第8、9步Enter已经通过，随后在设置焦点定位中收到取消；回执只记录按钮焦点，没有取消输入来源，不能据此断言用户误按。详情实屏的提示行消失而导航下移：正式DetailPage.LiftBackdrop将背景负Margin扩到XamlRoot上沿，后绘制的Shell能跨行盖住原工具提示。

提示仍是工具专属Auto行，现在以正Canvas.ZIndex在兄弟Shell上方绘制，取消Button保持单次Enter/Space可操作。平台默认TabIndex本来就是Int32.MaxValue；主工具树先登记Shell再登记提示，设置工具行也追加在原Frame之后，按同指数子树次序将取消入口放在末尾，不只靠显式MaxValue冒称末序。只忽略实际KeyStatus.WasKeyDown为真的激活键重复按下，不加时间防抖或禁用取消；Ctrl+Shift+Q保留。`cancel-button`与`cancel-accelerator`先记录来源、当前焦点和阶段，再取消；不能把取消登记为成功。

设置提示直接插进现有Window.Content根Grid的标题栏与正文之间，保留原Window.Content、标题栏、Frame和SettingsPage父链；finally移除工具行并恢复原行号。没有改正式设置窗口的生命周期或产品控件。每个manual阶段先等待真实提示布局并检查连接、可见性、正层级、窗口bounds及文本实际尺寸，回执增加promptGeometry。几何与层级断言仍不代替原生像素和用户输入；协调会话须在本轮最终构建目视提示和完整后半步骤，取消也单独重验。

固定框架源码`microsoft-ui-xaml 693f472aeaf89c6dff66256ee6fcd453e5d553b8`的`dxaml/xcp/core/core/elements/uielement.cpp::ZIndex/SetZIndex/OnZOrderChanged`将Canvas_ZIndex应用到CUIElement，并对任意父UIElement的child render order置dirty，没有Canvas父类型前件；`GetChildrenInRenderOrder`交给子集合排序。`dxaml/xcp/dxaml/lib/KeyRoutedEventArgs_Partial.cpp::get_KeyStatusImpl`把实际physicalKeyStatus的WasKeyDown暴露给事件；`ButtonBase_Partial.cpp::OnKeyDown`先读取Handled，已处理则跳过按钮激活。这些是实现适用性依据，实际Grid内遮挡仍由实屏核验。

新增3项几何/重复激活/源码接线回归；当前KeyboardJourneyTests24/24、0失败0跳过，编译0警告0错误。首轮Shell有2个委托捕获局部变量未初始化编译错误及连带XAML错误，已先显式初始化两个prompt变量，保留原日志。最终Shell与格式验证结果随本提交记录；不读取真实数据、不驱动桌面、不合主树/发布或远端推送。

后续只读复核固定同提交`dxaml/xcp/core/dll/focusmgr.cpp::GetTabIndex/CompareTabIndex`确认默认Int32.MaxValue且同值按focus children集合排序，因此主manualHost改成先登记Shell、后登记提示；布局Row0/1和正ZIndex保持。新增实际源码登记顺序契约，本树KeyboardJourneyTests25/25；不把TabIndex数值存在当原生Tab末序已测。

## 设置 Tab 验收真实性补修

最终审查发现原settings-ready只检查按键计数新增2和焦点仍在设置搜索框，两个编辑键/方向键也能满足，不能证明Tab往返。现在PreviewKeyDown捕获实际Tab、Shift/Ctrl/Alt与WasKeyDown，下一低优先UI回调只读正式FocusManager位置。共享有限序列只接受正Tab从搜索框离开，再Shift+Tab从框外返回；错误修饰、重复Tab、逆序、未离焦与任意两个键均不能完成。阶段仍要求真实按键增量2与最终Search焦点，未由探针程序搬焦点取得通过。

settings根树登记按键witness覆盖工具取消分支，序列观察在该阶段结束及finally解绑，代次拒绝迟到回调。回执新增settingsTabSequence和每次Tab的修饰/前后焦点读数；未接入输入注入，不改正式设置或快捷键。Alt+F4系统关闭仍minimumKeys=0，读数仅证明设置隐藏和详情主体不变；它不保证XAML一定收到该系统组合键，手动操作范围与观察须分开记。设置原caption仍在row0且Window.Content未换，不需要重声明SetTitleBar。

新增正确Tab往返、七类反例及正式接线/解绑/读数契约3项，本树KeyboardJourneyTests28/28、0失败0跳过；Shell、格式及最终提交仍按本单元当前日志核实。首次Shell缺Interop using及局部捕获变量初始化导致5错误及连带XAML红，已修且保留原日志；没有运行失败产物冒称原生通过。主控须用新入口实际重验后半，旧两键回执不得记新序列通过。
