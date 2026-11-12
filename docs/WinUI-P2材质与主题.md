# WinUI P2：材质接入、主题资源与系统回退

日期：2026-11-12，Asia/Shanghai。开始基线为 `321753c9b029105c24bb294d243732f5299f94ef`，代码编辑在本聊天的 `winui-rules-plan/Momoka` 工作树。本批产品修复已提交为 `b4cd92f`，真实探针与契约修复为 `2646b6e`。本页记录本树收尾结果；源码改动、模拟边界测试、真正窗口运行和系统实验分别记录。**本批尚未合入 master、未做主树复验、未发布；第6节候选仍未完成。**

本批处理已有材质接入与回退缺口，不恢复浅色主题、增加逐卡系统材质、切窗口宿主、改播放管线或调整现有染色/浓度。范围承接[P1基线](WinUI-P1基线.md)、[材质与窗口清单](WinUI-P1材质与窗口清单.md)及[分阶段计划](WinUI重构计划.md)，确认规则仍以[CLAUDE](../CLAUDE.md#winui-官方优先与偏离确认)为准。

## 1. 版本与证据边界

| 输入 | 本批采用的依据 | 能证明与不能证明的事 |
| --- | --- | --- |
| Momoka 代码 | 起点321753c；产品b4cd92f、探针2646b6e；Shell DLL SHA-256 `2F6D71162BC6B2569E2DE8FC23FD3234C3AD543C5CF3C096534BD6A865A65AF6`；EXE `443179E26E8C3CDCC7F19E66E7CFB845E148637ECCC5B5BDE06207BA7DF69268`；未整合 | 文档行号不自动等于未来主树；运行证据须记录实际构建来源，不用旧P1件冒称本批 |
| WinUI Gallery | 固定SHA `6de91feccadfbb0a796b71caedead5a2284f730e`，本机只读克隆 `C:\Users\89400\AppData\Local\Temp\momoka-winui-gallery-20261112` | 读取SystemBackdrops支持窗口、Acrylic/XamlResources、SystemBackdropElement及ThemeHelper的实际XAML和code-behind。Gallery的2.6.6-experimental只作为参考，不引入该依赖 |
| 框架机制 | `microsoft/microsoft-ui-xaml` 固定SHA `2a73274618c3f1042ee617dca5f64be782f93261`，`dxaml/xcp/dxaml/lib/SystemBackdrop_Partial.cpp` | 第64行断开清per-target entry，第77行返回同一configuration，第160行析构退订XamlRoot/ActualTheme/HighContrast/Input；可解释配置/观察所有权，不能宣称此SHA就是当前已加载二进制源码 |
| 当前API合同 | 编译包WinUI2.3.9、InteractiveExperiences2.1.9、Runtime NuGet2.5.1；本次实际模块位于WindowsAppRuntime.2_2.6.5.0_x64 | 当前winmd/投影确认AddSystemBackdropTarget有bool回执、Popup目标的SystemBackdrop使用WUC类型；包声明、启动请求、实际模块分开，不因当前机器加载更高runtime而称项目已升级 |
| Windows外观事件 | 当前SDK19041 net.ref XML记录HighContrastChanged、ColorValuesChanged、AdvancedEffectsEnabledChanged | 事件存在不等于所有最低OS支持；AdvancedEffects事件按ApiInformation.IsEventPresent守卫，旧环境走显示期降级观察，不提高OS最低版本 |

本批构建使用SDK10.0.400、当前包声明不变；启动请求Runtime2.5，PID26424实际加载2.6.5.0目录的Microsoft.UI.Xaml.dll（3.2.0.2511）、Controls.dll（3.3.0.0），模块回执在`artifacts/winui-p2/inspect-p2-close-modules.json`。Windows11专业版10.0.26300，双屏：主屏2560×1440、工作区2560×1392，副屏1080×1920；本批`--screen 1`、主窗dpi96。模块与包声明不是同一个版本判断。最终源码与构建时工作区内容相同，Git提交本身不重编译产物。最新退出码补验PID31280受桌面遮挡而失败，见第7.1节；未发布。主树整合只读预演发现Momoka.sln和regression-suites.json两处新增测试登记冲突，需要同时保留P2和第五批项目；未修改主树。

## 2. P2a：主窗口材质状态与controller所有权

### 2.1 修复的是接入结果与重试，不是播放功能

原来的HostWindow catch会把 `_useBackdrop`改false；`VideoVisible`又由其取反，材质失败因而混入播放/浏览诊断和后续重试意图。P1已追全调用者：没有根据这个读值自动起播的分支。本批不称为修复误播放。

新[HostWindow.Backdrop.cs](../src/Momoka.Shell/Windowing/HostWindow.Backdrop.cs)把有关成员收在同一partial：

- `UseBackdrop`保存调用方请求，接入失败不改它；`VideoVisible`继续反映调用方要求撤浏览材质，不证明后端是否在放视频。
- `BackdropConnected`同时检查source、成功登记controller目标及source当前backdrop引用；系统controller.State=Fallback仍可能是成功连接。
- 同true但未成功接入时，先真正摘旧引用再重挂；相同引用赋值不能假定重新触发OnTargetConnected。
- 同false请求仅在source实际backdrop已为空时略过；如果前一次正常摘除和错误清理都失败，下一次false仍可重试，不因意图已false而直接跳过。
- 相同激活意图仍重新写入configuration，临时setter失败后下一条通知可以恢复；不因已保存意图跳过失败状态。
- 最近宿主激活状态单独保留：材质尚未创建或播放请求已摘除时，WM_ACTIVATE仍更新快照；下一次接入继承它。
- 退出先退订FocusChanged并尝试摘材质，再由HostWindow释放DesktopWindowXamlSource；异常回执与尝试释放不能冒称native资源一定已释放。

保持当前主宿主HWND＋XAML岛及AppWindow路径，不迁移到framework Window。集成视频仍在页面Composition树，独占视频仍由mpv顶层窗负责；此partial不操作交换链或发播放请求。

### 2.2 IslandMicaBackdrop的失败与登记边界

[IslandMicaBackdrop.cs](../src/Momoka.Shell/Windowing/IslandMicaBackdrop.cs)继续使用MicaController Base和框架的每target configuration，只由宿主提供IsInputActive，主题深浅仍明确同步。改变之处是控制器所有权与错误路径：

| 阶段 | 当前行为 | 验证要求 |
| --- | --- | --- |
| 支持性检查 | 不支持时不构造controller、不登记连接成功，保持浏览请求 | 核ConnectedTargetCount=0和可读回退；不支持不冒称Active |
| 构造与Kind/配置setter | 先将新controller赋到局部变量，再设属性；setter抛也有可释放的对象 | 构造/Kind/GetDefault/配置赋值各自注入错误，不用对象初始化器导致未接管对象 |
| AddSystemBackdropTarget | 检查bool，false不登记Entry；若add已尝试则清理尝试移除目标再Dispose | false、部分add后throw、Remove/Dispose异常分别保留回执 |
| Entry登记成功 | 立即把局部controller置空，Entry接管；随后诊断State读取不影响已接管对象 | State读取失败不能产生“字典有Entry但controller已释放”的假成功 |
| 系统配置变化 | 更新同一configuration的宿主激活/主题，不重新构造target | 系统fallback与连接计数分别记录；重复切换没有多controller |
| 断开 | base断开置于try，自有Entry移除及controller清理置于finally | base/Remove异常不跳过自有Dispose尝试，登记最终归零 |

主窗口没有新增逐帧取样、高斯模糊或逐控件controller。Mica是否真正采到壁纸、失焦如何回退仍须当前实屏，而不是用对象类型或ConnectedTargetCount证明像素。

## 3. P2a：详情主/子菜单、色板更新和实色回退

### 3.1 controller分支

[DetailMenuBackdrop.cs](../src/Momoka.Shell/Views/DetailMenuBackdrop.cs)对每个实际target保存一份Entry。成功分支保留Thin、TintOpacity0.65、LuminosityOpacity0.45、SurfaceAlt染色及回退色；这些是已有项目参数，不冒称微软默认推荐。本批不改变参数或扩大到全应用菜单。

构造、Kind、染色、configuration设置与Add回执分开；false或异常不登记成功controller。第一份Entry接入才订阅ThemeHost.Changed，最后一份断开退订。菜单打开期间换应用色板直接重画已接入controller的Tint/Fallback，不用重开菜单来假装主题更新。框架默认configuration改变时更新该target条目，并触发表面反馈。

### 3.2 MUX/WUC边界与初次构建失败

最初尝试用 `Microsoft.UI.Composition.CompositionColorBrush`给connectedTarget.SystemBackdrop提供fallback。当前投影/元数据的这个属性却是 `Windows.UI.Composition.CompositionBrush`；不能因两个命名空间都有CompositionColorBrush就认为可直接赋值。第一轮编译真实报 **CS0029**，原日志保留为 `artifacts/winui-p2/build-initial.log`。这是一条API类型边界，不是XAML模板或网络环境故障；没有用那份失败产物作运行验证。

**最终弃用直接MUX→WUC合成brush桥接，改由XAML presenter实色层回退。** 不通过未核实的跨compositor桥接、IBrush互转或换运行库绕过。回退不是新的桌面模糊：它是当前菜单所属XAML树中的可读SolidColorBrush。

### 3.3 XAML回退表面

[DetailMenuSurface.cs](../src/Momoka.Shell/Views/DetailMenuSurface.cs)通过附加属性接到[EgDetailMenuPresenterStyle](../src/Momoka.Shell/Theme/Styles.xaml)：

- presenter Loaded后观察其SystemBackdrop替换，连接当前DetailMenuBackdrop的SurfaceChanged；重复Loaded不重复订阅。
- 接入成功时用透明XAML背景，让系统材质呈现；缺controller或任一共享target失败时使用不透明SurfaceAlt实色。高对比回退色取系统背景。
- 不把“没有Entry”理解为透明成功：初始化前、失败后均保守实色。读取系统高对比颜色前先提供不透明SurfaceAlt，首次读取失败也不留下透明空面；后续通知恢复系统色。
- presenter Unloaded后解除backdrop/属性观察；切换或禁用附加属性时Dispose旧State并只清自己持有的背景。
- 主菜单/子菜单各有实际target；共享backdrop任一失败导致整组保守实色的范围写清，不能用主菜单成功覆盖子菜单失败。

系统State=Fallback仍可能有controller，平台负责该分支呈现。`UsesCardColor`核参数与色值，`NeedsSolidSurface`说明XAML回退需要；二者都不单独证明实际桌面亚克力Active。实色颜色、主/子target数、打开状态、主题代次和同帧截图须一起记录。

## 4. P2b：资源首帧与输入主题重解析

### 4.1 三处字面值与实际默认规则对齐

只修[Palette.xaml](../src/Momoka.Shell/Theme/Palette.xaml)的Dark默认字节；[UiThemes.Make](../src/Momoka.Core/Theming/UiThemes.cs)与[ThemeHost](../src/Momoka.Shell/Theme/ThemeHost.cs)的运行推导不变。

| 角色 | 原字面值 | 与生产默认推导对齐后的值 | 影响 |
| --- | --- | --- | --- |
| EgBorderStrongColor | `#FF49515A` | `#FF495158` | B通道减2；输入聚焦/加强边线在未Apply前的定义一致 |
| EgAccentSoftColor | `#FF1C332F` | `#FF1C3332` | B通道加3；柔和强调层默认一致 |
| AccentFillColorDisabledBrush | `#FF2D544F` | `#FF2D5450` | B通道加1；禁用强调角色默认一致 |

这是资源/默认推导合同修正，不改变应用主题算法、主强调色、普通0x99画布或已有材质浓度。启动ThemeHost在显示前执行，未实测到肉眼闪色，不能写成“修复启动闪烁”。Light保留派生路线，不恢复已删daylight。

离线测试需读取当前XAML并用真实UiThemes默认和InputControlTheme角色表比较字节；只检查引用某个Color键或只更新字符串不足以覆盖漂移。

### 4.2 设置卡片输入的局部主题别名

原[SettingsPage.xaml](../src/Momoka.Shell/Views/SettingsPage.xaml)的SettingsScroll.Resources顶层用StaticResource将TextControlBackground/PointerOver接EgInputGlassBrush。顶层静态别名可能钉住打开时的Dark Acrylic，即使框架转高对比仍有这份局部覆盖。

本批把这两个别名放入Dark/Light字典，高对比同名键直接是系统WindowColor实色。聚焦和disabled键继续沿现有框架/Palette路径，搜索框仍在此作用域外；不改行模板、圆角、输入法或草稿语义。原生文本控件模板继续保留，避免为主题问题重写键盘、选择与可访问状态。

固定Gallery XamlResourcesPage有实际ThemeDictionaries/ThemeResource对照，App.xaml及控件局部资源有独立HighContrast；其Acrylic样例仍有Default字典。因此“不允许Default”是旧项目经验，不能写成WinUI硬限制。本批若改相关注释，保留Momoka现Dark/Light/HighContrast结构作为项目选择。

## 5. P2c：设置窗口的系统高对比与关闭透明

### 5.1 手造画布与原生caption不能只等应用主题事件

设置仍用framework Window、Mica Base、一个根canvas，标题栏/分类栏不另铺Mica Alt或卡片。`_mica`表示支持检查及Window.SystemBackdrop赋值结果，不能当controller Active/Fallback。普通环境画布仍0x99；系统高对比或关闭AdvancedEffects时画布实色，不改Windows偏好。

[SettingsWindow.Appearance.cs](../src/Momoka.Shell/Views/SettingsWindow.Appearance.cs)读取系统HighContrast、AdvancedEffects、Background/Foreground/Highlight/HighlightText，选择画布与caption颜色：

| 环境 | 画布 | caption/系统按钮 |
| --- | --- | --- |
| 普通，Mica赋值成功，透明开启 | 当前应用Window角色，A=0x99 | 原普通主题前景与悬停/按下覆盖层 |
| 普通，透明关闭或Mica未赋值 | 当前应用Window角色，A=255 | 保留普通主题可读前景，不强迫透明 |
| 高对比 | 系统Background，A=255 | 系统Foreground；hover/pressed取系统Highlight，字取HighlightText |

标题、按钮普通/失焦背景仍透明，因为实际不透明高对比canvas已在同一个根上；不再以不同背景条把整窗切开。非客户区API失败前先更新canvas；caption成功结果才更新状态快照，失败不会让同状态重试永远被跳过。

### 5.2 观察、线程与缓存寿命

- UI线程创建时缓存Window.DispatcherQueue。HighContrastChanged、ColorValuesChanged、AdvancedEffectsEnabledChanged的系统callback只向该队列派发，不从工作线程访问XAML。
- AdvancedEffectsEnabledChanged实际在当前19041合同存在，运行用ApiInformation.IsEventPresent守卫；事件不可用或部分订阅失败时才使用可见窗口的1秒降级观察。
- 同一系统通知洪峰合并为一个排队工作；TryEnqueue拒绝或异常复位，后续通知仍可重试。
- Show/Activate重新读取状态；Hide停止降级timer，保留缓存窗口和原页面活动释放语义。再次Show不重复订阅系统事件。
- 真Close先标记观察停止，独立退订每个成功订阅及timer；已排队工作看到停止标记不重画。任何一项退订失败不跳过其余清理尝试。

当前SettingsLifetime源码提取只负责Show/Hide/Close的页面与身份生命周期；新增Resume/Pause/Stop是该夹具的窄系统边界。SettingsAppearanceTests执行完整生产appearance partial，PaintCaption/原生setter仍是模拟边界；返回false、抛异常、相同状态重试及独立退订失败由测试覆盖，实际caption值由系统检查核验。创建ApplyBackdrop后使快照失效，材质赋值和首次重画双故障不会让旧快照阻止恢复。

### 5.3 用户授权的系统实验

原聊天2026-11-12用户明确回答“允许临时切换，测试后恢复”，本聊天继续同一收尾范围。2026-11-12用户又要求少用鼠标；最后透明检查用明确UIA Invoke/TogglePattern执行，调用后释放工作流。授权只涵盖这两个全局状态及恢复，不包含更换壁纸、开启真实播放、写服务器、迁移宿主、恢复主题或改变D01–D07产品选择。根代理负责串行桌面实验，切换前记录原状态，正常/失败finally均恢复，恢复结果还须实际读取和截图/回执确认，不能只说“调用了恢复”。

| 实验 | 实际结果/证据 |
| --- | --- |
| 普通透明开启，设置首开与隐藏重开 | 通过：settings PID14048，首开稳定855ms、4次缓存重开406/273/263/263ms，五主题画布读数和输入实屏；开窗截图的配色延迟见下方限制 |
| 透明关闭，canvas变实色，功能与焦点保留 | 通过：PID2868的3采样为effects真→假→真，画布#990F1420→#FF0F1420→#990F1420，UIA采样/结束按钮均可用；焦点测试另见settings分区 |
| 高对比开启，系统前景/背景及hover/pressed | 通过：PID12988，HC画布#FF202020、前景#FFFFFFFF、hover/pressed底#FF8EE3F0、字#FF263B50；这是原生caption属性读数，未另模拟按下关闭按钮 |
| 高对比关闭和透明恢复，普通0x99恢复 | 通过：PID12988第5采样HC假/effects真/#990F1420；PID2868第3采样恢复普通；系统注册表Flags126、EnableTransparency1读回 |
| 退出时事件/目标清理及原系统状态恢复 | 两轮正常报告1/1并正常退出，当前无本轮Momoka进程；模拟退订/迟到守卫测试通过，native事件计数或失败Dispose的物理释放未测 |

未真实执行前表内不得填写“通过”；只读源码和假configuration不能替代系统切换。若环境或窗口遮挡使必需像素未取得，保留红项并说明缺口。

## 6. 本批保留与后续候选

本次没有找到足够证据把当前设置card、详情头板和原生聚焦层统称为重复模糊。以下层次保留：Settings根唯一canvas、Frame透明、标题透明；设置card单一Acrylic Border且BorderThickness0、内行透明；详情整页PlainBackdrop已移除，HeroMicaTop/Bottom为不同区域，沉浸时摘系统材质/正文面板让开；InfoPanel卡面承担阅读。原生ComboBox的HighlightBackground是状态层，不为“减层”拆模板。

| 尚未处理候选 | 具体依据 | 后续范围 |
| --- | --- | --- |
| 普通菜单实色盖住controller | 当前generic默认MenuFlyoutPresenter仍带SystemBackdrop；Palette只覆Background实色 | 先量实际target/State/覆盖范围，证明多余后才定点关闭，不扩到详情Thin或所有Popup |
| 详情/MP手造HC配色刷新 | 当前部分timer只比较HighContrast/AdvancedEffects两个bool；HC一直true而系统配色换了，复制前景/纸面可能不更新 | P2c后续同target配色实验与最小通知转发；不凭源码认定所有卡面不合格 |
| 设置下拉CheckGlyph | EgAccentBrush在HC是Highlight，而选中行背景亦为Highlight系列 | 真实选中/hover/pressed/disabled对比，必要时仅HC取item相应前景，保留普通QQ模板 |
| Poster/详情封面按钮局部别名 | hover/pressed资源仍有顶层StaticResource指EgPlayGlass/OnAccent | 按实际树HC→普通恢复量值/像素，证实后分主题局部资源，不修改播放入口 |
| 普通菜单/系统fallback主动更新 | 有controller与XAML实色状态分工，特殊系统策略仍需实测 | 关闭透明、失焦、RDP、省电按API/OS分别观察；不保证统一fallback行为 |
| 旧注释 | 九套主题、Default禁令、mpv child旧宿主、Opacity乘积当透明率、BaseAlt无标签禁令 | 随实际路径定点修，不把历史截图/近似公式写成当前硬限制或新实测 |

D01–D07在本批没有新批准产品改变。深色主题、点外取消、影视按服务/音乐统一、不可用功能隐藏和已确认详情参数继续保留。出现新的实质取舍时按具体表面、版本、收益/代价及验证计划确认；无需让用户一次回答七项。

## 7. 本批验证与交付账本

| 检查 | 当前可执行覆盖 | 最终结果 |
| --- | --- | --- |
| Shell/Core Release构建 | 当前所有controller/附加属性/Settings API与XAML编译 | 通过：build-p2-probe-final.log，0警告0错误；hash见第1节。CS0029属于初次错误实现的失败历史，当前无编译红 |
| MaterialLifecycleTests | 完整生产Island/HostBackdrop/MenuBackdrop/MenuSurface；false、setter、部分add、State读错、重试、连接vsFallback、主题/释放与附加属性生命周期 | 56/56，0失败0跳过；regressions-material-repair，模拟边界不算native释放证明 |
| SettingsAppearanceTests | 完整生产appearance partial；系统选色、通知排队/合并、拒绝/异常恢复、迟到、缓存与缺事件timer | 47/47，0失败0跳过；regressions-recovery |
| SettingsLifetimeTests | 原Show/Hide/Close身份、目录、草稿与页面活动期不回归 | 61/61，0失败0跳过；regressions-recovery |
| Mica/Input/Acrylic内容测试 | 资源首帧字节、局部主题别名、Base/透明caption/实色菜单入口及模板契约 | 完整主套件含Mica；input-theme29/29、acrylic-controls39/39，0失败0跳过；这些内容测试不是像素 |
| ProbeFinalizationTests | 新检查台入口及当前报告/范围/关闭责任 | 106/106，0失败0跳过；regressions-final-probe，包含成功/超时/写报告失败及迟到收尾 |
| 匿名真实materials分区/检查台 | 主岛四次浏览摘/接、五色板、同主/子菜单换色与循环关闭、零残留，真实表面截图 | materials PID28220报告3/3、settings PID14048报告28/28，正常退出；主岛/主子菜单35记录与五主题实屏，范围回执分别排除207/182项；最新PID31280退出码补验1/3、退出1，两项桌面遮挡，保留红项；不代完整探针 |
| 设置系统实验 | 第5.3节授权范围，实际状态与恢复 | 两轮分别1/1，PID12988高对比、PID2868透明关闭/恢复；两轮均正常收尾并读回原值 |
| 完整Core主套件及适用交付检查 | 按CLAUDE本批最终范围串行执行 | Tests/Core当前Release0警告0错误；完整主套件4881/4881，0失败0跳过。check-diff43文本通过、check-scripts63脚本通过。旧library/moviepilot红保留，不在本批适用分区中冒称已修 |
| 最终主树整合/发布 | 根代理核最终master、复验、文件锁与实际交付目录/快捷方式 | 主树检查为88692b5且干净；本批两提交未合入master、未做主树复验、未发布或推送。待用户明确要求“合并 交付”后按主树最终代码复验/发布；快捷方式仍为主树既有版本 |


### 7.1 当次运行目录、历史失败与视觉限制

下表目录均位于`C:\Users\89400\AppData\Local\Momoka\logs`，结果在各自`logs/shell-probe.json`，源码为2646b6e的最终内容；Shell hash见第1节。过程创建时间另在本树`artifacts/winui-p2/*process.json`，不通过PID单独选择旧件。

| 当次scope/PID | 实际开始/结束（Asia/Shanghai） | 匿名目录和有效结果 |
| --- | --- | --- |
| materials/28220 | 19:53:44.554→19:56:07.169 | shell-probe-20261112-115344700-28220；3/3，五主岛＋30主/子菜单记录；岛Active及连接、菜单Fallback及连接分开记录 |
| settings/14048 | 19:57:36.438→20:03:46.163 | shell-probe-20261112-115736550-14048；28/28；五主题输入、1440/1100/760宽、普通/聚焦/离焦/恢复和缓存重开 |
| inspect/12988 | 19:46:24.311→19:52:49.717 | shell-probe-20261112-114624533-12988；1/1，五采样中一组HC及恢复；前三组重复普通状态，不计为关闭透明 |
| inspect/2868 | 20:14:29.004→20:17:35.554 | shell-probe-20261112-121429133-2868；1/1，普通→透明关闭→恢复三采样 |
| materials/31280 | 20:43:21.836→20:44:53.163 | shell-probe-20261112-124321986-31280；1/3、进程退出1；QQ及另一桌面窗口遮挡两个截图检查，材质生命周期通过 |

系统注册表恢复回执为EnableTransparency=1、HighContrast.Flags=126（HC位关闭）；两轮最终采样同时读取AdvancedEffects=true/HighContrast=false，与切换前一致。机器HighContrastChanged订阅实际报0x80070490，显示期降级观察与激活刷新完成当前验证；不能据此说该事件在所有机器不可用。文件系统保留初次API类型错误build-initial.log（CS0029等、1警告32错误）；旧materials PID12236的薄白默认夹具超时、P2重跑PID16216的AutomationId误当字段名失败均保留，修复后PID28220报告全绿。旧系统PID24668超时后PID19836曾1/1；本聊天PID26424和35152又因交互中断超时，不使用这些红件冒称通过。

退出码补验PID31280的原失败包含目标与遮挡归属：第一项point=237,231、遮挡PID18040（QQ）；第二项point=263,191、遮挡PID18780。守卫正确拒绝把其它窗口像素算作本应用；没有改守卫、关闭用户程序或删除证据。两项都是实际未完成的像素检查，最新运行保持红；之前PID28220同一源码/hash的3/3报告仍作为已取得覆盖保留。因为补验未通过，串行脚本停止，后续settings退出码补验未执行；此前PID14048的28/28及正常退出日志单独保留。当前没有本轮Momoka残留进程，不据日志“正常退出”猜测未捕获的退出码0。

真实截图核对共35张输入图片，未达到40张暂停线。有效五主题颜色验收采用`settings-inputs-<theme>-1100-screen.png`及对应`input-theme-<theme>-1100.txt`，各自背景/焦点线与当次主题一致；菜单五主题也逐张核对。`settings-opening-*`部分截图的勾选/背景落后配色读数（相邻主题/旧勾选），Rendering等待不是DWM已呈现保证：这些开窗图只证明层次与可读性，不承担主题同步的像素通过证据，取样时序列为后续候选。系统采样截图与caption/画布值配对；原生hover/pressed属性符合HC角色，未实按危险关闭按钮。未对实际Narrator、完整键盘、触摸、GPU或native资源计数作扩大结论。

完整主套件与桌面探针共用用户级锁；材质/设置窗口、系统切换、截图严格串行。不得终止用户程序来清场；运行受遮挡时保留失败回执。每个报告匹配本次PID＋创建时间＋scope＋二进制身份，避免PID复用选旧日志。

本批没有测GPU/显存/功耗、PresentMon/WPA帧时序、Narrator实际读出、完整物理键盘流程、触摸/笔、100/125/150/200%/跨屏DPI、HDR/RDP、省电和真实媒体服务/播放。Mica/Acrylic对象、参数、单测与屏幕结果各自记录，不用任一项概括其它项通过。P2a/b/c仍有第6节候选，不能写全部P2已完成；P3–P8也不因此自动开工。

本页最终产品提交、实际验证摘要、系统恢复回执、主树整合及交付状态：**产品b4cd92f、探针2646b6e；通过覆盖及最新遮挡失败均已记录；主树整合和实际交付待明确指令，桌面补验红项不能算交付通过**。
