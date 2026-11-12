# WinUI P1：现状、匿名运行与性能基线

日期：2026-11-12，Asia/Shanghai。P1 的源码盘点、匿名运行、缺口登记和后续决策准备已经完成。**这是一份包含红项的现状基线，不是产品改造验收通过。** 本批没有修改产品行为、资源、默认设置、依赖或播放管线，没有发布应用，也没有推送远端。

逐表面方法和源码登记见[材质与窗口清单](WinUI-P1材质与窗口清单.md)、[页面与控件清单](WinUI-P1页面与控件清单.md)。机器可读结果见[baseline.json](winui-p1/baseline.json)，音乐完整观察见[music-observations.json](winui-p1/music-observations.json)。它们保留成功、失败和未覆盖项，不能整份替换自检基线洗绿。

## 1. 源码、产物与机器条件

### 1.1 两个版本分别记录

| 用途 | 源码及产物 | 覆盖边界 |
| --- | --- | --- |
| 主运行基线 | `4e0394acbb8ee5b03a39e2078736d40254e8cb15`；本树 Release Shell/Core；Momoka.dll SHA-256 `0841C9938BFE5F1E9F33D8C3E5E27F51B3914EA895897BA3CDA727465580AE5D` | 85项表面探针、3轮探索performance＋3轮比较performance、音乐临时观察的产品输入。包括设置Mica及IslandMicaBackdrop采样修复 |
| 临时音乐观察件 | 上述产品源码加一个检查台入口和只观察partial；DLL `7547B169BD48C11B72000391F44EA788B8341190330912B1C48CBF512BC341BD` | 只额外采样真实Tab、滚动、集合/容器/图片和请求。未调用LoadMore、未更改页面模板或业务逻辑 |
| 同期主树整合 | `07e991b4cb79fa1ca3e9a0373d817c9772d087a1`；未带临时仪器的新构建位于 `artifacts/winui-p1/final-uninstrumented`；DLL `472BB9112AE3907E4F912B4695B0CFABDF7DACDFC87EC4C6FE971EAE2F16A9AD` | 新字幕/花絮做增量源码审查；重编当前Shell和三个专项，另运行music两项。原bin保留4e比较件，不能靠目录名误认已更新。旧85项/六轮性能不冒称这个版本的运行结果 |
| 已有实际发布件 | 主树 `artifacts/publish/win-x64/Momoka.dll`；8,453,632字节，最后写入UTC 06:40:28（本地14:40:28）；SHA-256 `FE953FCAF5BA4595D93C884F6B14F0507CB8BD5368D0BB67058FCFE55FB80FA9` | 只读取文件身份，没有拿时间戳证明源码来源，没有运行它作对照，也未覆盖发布目录或快捷方式 |

本聊天保留受保护的工作树 `winui-rules-plan/Momoka`，原detached基线先快进到4e0394a，再吸收同期07e991b。主树开始和整合检查均无未提交改动；其它聊天的树不清理。三份构建分别0警告、0错误；临时仪器首次编译的nullable警告已修正并另存原失败/警告日志，不使用该件作观察。

临时入口和partial已逐字核对并移除，产品路径相对07e991b无diff。可复查的[MusicSurvey源文本](winui-p1/MusicSurvey.cs.txt)仅为证据，未进入编译；SHA-256 `008A5BD421B4FE5A5E170C9C1813ED765BC6E056735DF4420C994109E7F6632F`。重新构建无仪器版本后运行匿名music，不能用临时观察替代正式产品验收。

### 1.2 编译包和实际加载运行库

| 条件 | 本次读数 |
| --- | --- |
| OS | Windows 11 专业版，10.0.26300/build26300 |
| .NET SDK | 用户级10.0.400，匹配global.json；.NET10、非打包、x64、framework-dependent路线 |
| 编译包 | WinUI2.3.9、Runtime NuGet2.5.1、Foundation2.3.12、InteractiveExperiences2.1.9；本批未升级 |
| 生成的启动请求 | Runtime2.5.1包的`WindowsAppSDK-VersionInfo.cs`请求MajorMinor=`0x00020005`、stable、空VersionTag；不能把这叫固定实际加载2.5.1 |
| 运行进程实际模块 | `Microsoft.WindowsAppRuntime.2_2.6.5.0_x64`中的Microsoft.UI.Xaml.dll，FileVersion3.2.0.2511；Windowing.Core12.6.5.0。第一轮PID33620读取Modules，第六轮PID11528再次读并保存[模块回执](winui-p1/runtime-modules.json) |
| 显示器 | DISPLAY1主屏2560×1440、工作区2560×1392；DISPLAY2竖屏1080×1920、工作区1080×1872 |
| 本次UI环境 | 使用主屏，支持1280/1420等宽度且不越出副屏窄边；实际RasterizationScale=1。透明效果开启，文字缩放100%，HighContrast注册表Flags126；没有切换系统设置 |
| 图片 | 由现有FixtureImageAsync生成确定PNG，音乐160×160；其SHA及每场景读数存音乐JSON。没有网络图片或真实媒体 |

本机已安装多个运行库版本不等于项目升级；实际模块版本与NuGet版本分开记录。没有验证100/125/150/200%的完整矩阵、跨屏DPI、文字放大、关闭透明、高对比、省电、RDP或HDR，不能用当前单环境结果覆盖这些条件。工作树缺少根libmpv，不影响本轮拒绝播放的匿名探针，但没有证明任何播放/发布载荷完整。

## 2. 页面、窗口和行为库存

当前07e991b按Views XAML根类型实际枚举：**19个Page、31个UserControl、24个ContentDialog**。4e0394a的前两类相同、ContentDialog为23；新增飞牛字幕表单是Dialog。纯代码控件、间接继承对话框、系统FileOpenPicker与独占mpv窗不混入XAML计数。

窗口分主Win32+XAML岛、framework设置窗、framework版本搜索窗；临时视频帧遮罩和独占mpv另列。设置最新为Mica Base＋一层0x99画布，分类局部Mica Alt已由同期工作撤掉。主岛已有IslandMicaBackdrop按宿主激活喂配置。普通库/设置CardBackground键实际是应用内Acrylic；PlayerGlass等部分名字对应实色刷，按真实API记录。具体Root/target、长期/短暂、覆盖层、主题更新及清理所有者见材质清单。

已存在NavigationView、CommandBar、有限Frame缓存、页面Attach/Release/请求代次、ShelfStrip焦点重试、CardCollection按需数据卡、Settings860断点、Shell1008断点、Detail819.2断点、MusicBar760/480布局。后续以缺口为起点，不当成空白功能重新实现。

## 3. 实际执行和失败账本

### 3.1 离线构建与测试

| 输入 | 结果 | 说明 |
| --- | --- | --- |
| 4e0394a Shell/Core | Release0警告0错误 | 开始前编译，未读真实设置 |
| 音乐实际VM | 16/16，0失败0跳过 | 80+20队列和VM分页、代次/取消/音量/分区；不证明墙页入口 |
| 页面源码契约 | 198/198，0失败0跳过 | 真code-behind/卡片/焦点/图片控制流，UI边界被替换 |
| PerfListDiagnostics | 80/80，0失败0跳过 | 实际VM/源链接与探针契约，不是WinUI运行；DLL名是Momoka.PageContractTests.dll |
| 07e991b重新构建及相同三个专项 | Shell及各专项0警告0错误；16、198、80全部通过 | 最终统一入口记录当前HEAD/清单hash，覆盖重叠不相加 |

最初统一脚本在编译前拒绝：68清单对69顶层项目，漏UiMotionTests。此问题已由同期主树补齐；07e991b `run-regressions.ps1 -List`列70专项，三个选定专项用统一脚本重新成功执行。手工PerfList第一次按项目名猜DLL导致“找不到文件”，没有执行测试；随即读csproj用实际程序集重跑，保留该命令错误日志，不登记为产品测试失败或通过。

### 3.2 原生表面探针

4e0394a：`--probe-shell --probe-section shell,settings,library,music --screen 1`，PID27504，UTC目录 `shell-probe-20261112-070702432-27504`，**85项74通过11失败，退出1**。其余120个检查按scope未执行；不是全量Shell。没有登录/读取生产账号或真实服务写入，假动作/假HTTP的写入意图另有回执。

| 失败项/数量 | 当次实际原因 | 下一步；不宣称已修复 |
| --- | --- | --- |
| 飞牛人物头像/1 | “人员头像没有进入实际人物模板” | 模板每人两份PosterCard、父显隐互斥，探针数全树；先同帧记录祖先显隐/Loaded/图源再判断 |
| 三类详情自定义＋响应式/4 | CastRow副行期望色与实测色相反 | 共用CardsIn没有排除祖先Collapsed；先区分可见卡和隐藏副本、同帧OnScrim/主题，不能把期望色改成实测洗绿 |
| 集页819背景/1 | compact档期待Visible，实际Collapsed | 记录PageWidth、819.2阈值、HeroArt/BackdropShown/Showcase/实际层；待P4独立处理 |
| 合集菜单/1 | 四点归属guard采到本进程PopupWindowSiteBridge而非主HWND | 当前缺像素条件。不能把同PID任意窗算主客户区；单项前先查菜单取样点 |
| Jellyfin混合标题、继续观看标题/2 | 实际36DIP，旧探针仍期待20DIP | 对照已确认标题统一提交、资源和真实渲染，补行为/错误反例后修探针；不改产品字号回旧值 |
| 飞牛移出/1 | Sequence contains no elements | 正式对话异步/菜单枚举路径待复核。匿名动作结果不能证明真实文件移除 |
| 设置显隐窄窗截图/1 | 取样点被QQ窗口覆盖 | 像素前件失败；保留报告，空闲条件单项补测，不能称设置逻辑失败或已视觉通过 |

本轮这85项没有用例外层WaitAsync超时，红项为异常/guard。源码还存在外层超时只结束等待、不终止原任务的工具风险；以后发生该类超时须标注后续污染并单项重测，不能让两个用例共享窗口继续验。

07e991b去掉临时仪器的正式构建另跑music，PID13536，目录 `shell-probe-20261112-073804184-13536`，**2/2、退出0**。它仍是12曲目/9专辑的已有功能检查，不能用2/2抵消下节18场景的产品缺口，也不覆盖新增字幕/花絮。

## 4. 性能基线和比较方法

先执行三轮探索，复核条件发现本任务的编译工作并行，因此另补三轮比较：期间没有本任务编译或另一个桌面探针，正常桌面后台应用保持。六轮都是相同4e0394a DLL、新进程、同一主屏/100%缩放、现有匿名performance11项。没有清理OS缓存；“首访”是新fixture首访，“暖访”是同Frame/快照重访。进程采样请求步长200ms，比较轮实际中位218/219/219ms；记录WorkingSet64、PrivateMemorySize64、CPU time，未测GPU/显存/功耗。JSON的runs是比较轮4–6，exploratoryRuns保留1–3，不能删探索红项。

| 轮次/PID/UTC运行目录 | 检查与退出 | click→first-render回调 | 内存采样/峰值Private、WorkingSet |
| --- | --- | --- | --- |
| 比较4／31264／074956256 | 10/11，退出1 | 427.17ms | 500次；928.9、1021.3MiB |
| 比较5／37000／075145655 | 11/11，退出0 | 595.66ms | 505次；973.5、1065.6MiB |
| 比较6／11528／075335723 | 11/11，退出0 | 405.58ms | 518次；963.9、1057.1MiB |

比较轮导航回调中位427.17ms，是Rendering事件标记，不是GPU提交或显示器像素首帧；LayoutAsync额外等待150ms，不能据此承诺冷启动延迟。整轮Private峰值包含连续页面、固定图和诊断等工作量，未按阶段做泄漏归因，不作为单页内存预算。第4轮普通根缓存检查在`ScrollProbeAsync → WaitForDiagnosticsFrameAsync`等待Rendering超时（LibraryCache61/Diagnostics167），并非集合/身份断言失败；等待异常经finally清理，不是60秒外层超时留下旧任务。第5/6轮全绿不能给第4轮免责，这条呈现/等待稳定性另列P4/P5调查。

探索轮1/2/3分别11/11、10/11、10/11，598/460/601样本，导航559.87/476.84/475.40ms，Private峰值926.7/878.1/873.3MiB；这些值保留但不作比较基准。探索2/3都在`PerformanceDeepLibraryReturnAsync`首个1500位置BringIntoView→IsRealized/AnchorY等待处超时（现源117行）。这是内部UntilAsync约2秒超时，任务经过自己的finally清理后交回异常；不是90秒外层WaitAsync留下任务。并行编译可能影响时序，但没有据此证明因果，也不删除锚点稳定性调查。比较4–6的深返回均通过。探索2读取目录曾因PID复用误选2026-11-04旧目录，已按本次创建时间、scope和2026-11-12目录纠正；旧报告没有纳入本表。

比较轮暖库40项快照到`library-cards-end`三次33.10/108.43/30.51ms；暖首页到`home-force-layout-end`169.48/187.23/176.18ms。它们事件定义和等待成本不同，只分别比较，不写“主页比库慢几倍”。JSON保留探索与比较的每阶段原值。

第一轮2000集：系列首屏CardItem物化16、跳到1500为28、恢复33、两端焦点48；深处集页8、长季首屏1280为14/760为13。这里是**数据卡物化**，不等于realized XAML、解码图片或GPU资源。封面检查保留1280×720 BGRA 3,686,400字节、XAML请求解码1280×720，但明确内部表面峰值未量测。10000条日志burst为1次Post/1回调；这是队列合并证据，不是全流程p95/p99。

后续比较冻结：每个受影响性能路径同环境至少3次，报告所有红项和中位数；计时回归超过`max(基线中位数20%, 30ms)`作为调查阈值，超时/错项/锚点失效立即红，不能靠中位掩盖。此阈值是项目初步比较约定，不是微软推荐或已达产品SLA；当前三次样本不足以评估掉帧/p95。内存先采用同阶段往返后是否持续增长及保留对象证据，尚不设置全应用硬上限。P5开工时根据选定页再定解码/realized预算。

## 5. 音乐81/160项与图片生命周期观察

临时instrument通过检查台真实UIA按钮进入；本次18次Tab实际均使用Toggle→Click，调用真实OnSectionClick，读取真实生产MusicLibraryPage/VM；代码的Invoke备选没有被执行。HTTP只允许`.invalid`假目标和GET，按类型/StartIndex/Limit严格切片，曲目先12条，三墙各81或160。每个宽度1280/760/600新建访问，共6个曲目首屏、**18个墙场景**，每场景都有内/外滚动及实屏。没有反射调用OnMore或直接LoadMore证明入口。

| 指标 | 18个墙场景实际结果 | 判定 |
| --- | --- | --- |
| 分页 | Rows=80、MoreAvailable=true；请求只有StartIndex0，滚底后仍没有offset80；第81项未加载 | 产品分页缺口成立；首部总数81/160不等于内容都可达 |
| 页面入口 | MoreButton自身可有名字“加载更多”，祖先曲目GridCollapsed；effectiveMoreVisible=false | 隐藏祖先使墙模式不可达；UIA名字存在不能当有效入口 |
| 图片 | 独立ID＋Primary标签＋固定PNG响应准备；总图片GET=0、Image.Source非空=0、CardItem.Poster非空=0 | 新卡没有启动图片加载；不是服务图片下载失败。底部播放栏的独立图片路径不抵消页内缺口 |
| 滚动 | 1280实例outer viewport557/extent3518，滚底2961；inner viewport=extent3518、offset0 | 内层测到整墙高度，实际滚动由外层承担；仍须逐层量回收和缓存，不仅凭嵌套下结论 |
| 容器 | 1280首个首屏树中31个、视口18个；滚底树中80、视口14，TryGetElement约33 | 活动realized、树内回收/保留对象和视口交集不同，不声称80全活动或虚拟化完全失效 |

`shell-probe.json`里的临时“P1音乐墙只读基线”通过，只表示测量执行成功；各场景JSON `requirementPass=false`，本报告的music.productPassed=false。**不能把观察红项写成修复测试绿。** 两张目视代表图头部81张/160个、末项079/080完整可读、封面占位、无More，与请求/集合读数相符。

下一步最小方案：P4为三墙补可见加载入口，明确按钮最接近曲目现有语义；自动增量滚动会新增自动请求、取消/失败反馈责任，先做具体方案比较。P5单独补音乐模板的容器图片claim/load/release/recycle，复用既有CardItem所有权，测晚图/离页/身份替换。不能把两项合为“换ItemsView就好”。

## 6. 截图、输入和未覆盖项

已目视13张匿名截图：settings-opening五主题名图；sidebar1240、compact760、展开/收起600；home-card-alignment1280/760；音乐Albums81-760/Playlists160-600。分类/导航/主要文字可读、正常滚动边界、焦点描边及有/无年份对齐有屏幕证据。其它生成图没有逐张完成视觉审查，不写“所有截图都通过”。

设置五张主题名图存在文件名与实际底色/强调/勾选不同步疑点：midnight/oled-black仍有薄荷勾，plum/graphite灰底/琥珀；`SettingsOpeningAsync`只等一帧，未锁定controller/resources到呈现的同一代次。未核清前不宣称五主题像素正确或材质失败。P2需同帧记录ThemeHost.Current、RequestedTheme、角色刷、caption/controller及截图完成，保留这些反例。

真实物理键盘全流程、触摸/笔、Narrator读出、手动拖拽、Mica/Acrylic强制回退、controller连接异常、打开菜单期间换主题、GPU帧时序/显存/功耗、RDP/省电、跨显示器DPI和真实播放均未覆盖。现有程序化焦点/peer检查只证明其调用结果；不能将它们替代完整用户输入验证。

## 7. 决策准备与后续最小单元

| 决策 | P1结论/下一步 | 状态 |
| --- | --- | --- |
| D01主题 | 五深色继续保留，P2先修系统高对比/回退与同帧证据；浅色/跟随系统会增加资源、图片上文字及迁移验证 | 新主题未批准；触及时给具体方案再问 |
| D02弹窗 | 保留点外取消、取消不保存且不点穿；需要换成标准模态或改变草稿语义时逐dialog比较 | 既有行为保留，未新增例外 |
| D03材质 | 当前Base/0x99/0x80及菜单Tint=.65/Luminosity=.45支持参数原样；先修接入false/异常、菜单开着换色、实际回退 | 未请求整窗Acrylic/逐卡玻璃化，无本批待答冲突 |
| D04宿主 | 现有AppWindow＋XAML岛及设置framework Window已存在；替换宿主须先离线兼容样例、视频/DPI/输入/全屏研究 | 保持宿主，未批准迁移 |
| D05桌面通知 | 当前服务通知页不等于Windows通知；尚无明确离窗事件场景 | 暂缓，不新增后台/提示 |
| D06部署 | 保留非打包x64；编译/请求/加载版本分层，Gallery实验依赖不移入 | 未升级或改MSIX；后续升级单独成批 |
| D07布局/命令 | 分页明确按钮候选、原生成组CommandBar候选、既有860/1008/819.2/760/480断点先实测 | 正常等价且符合建议的授权调整可直接做；新增实质代价或推翻偏好先解释确认 |

P1没有需要现在让用户一次回答七项的冲突方案。本次授权是调查和基线，不等于批准改变上述产品选择。首个P2单元建议为主岛/菜单的目标回执与异常释放，以及菜单保持打开时更新自定义Tint/Fallback；补生产控制流测试＋实际同target/回退/重复开关证据。原HostWindow候选应改为“材质失败混入浏览/播放诊断及重试意图”，全仓读者没有据VideoVisible误起播，不能写成播放修复。

P3先选一个真实成组命令面试点；P4先音乐分页、精确断点和深返回稳定性；P5先音乐图片与容器保留，再由计时选瓶颈；P6补音乐UIA/完整键盘和关联动效；P7仅研究有实际收益的条件路线；P8按最终产品批次重验。每项的文件、现有覆盖、新增验证、Root/target和退出条件已在两份清单定位。

## 8. 复现命令与文档验收

在当前工作树根执行，编译后才运行。命令细则以[开发与验证](开发与验证.md#标准命令)为准。这里的命令只启动匿名探针，不使用普通show参数读取真实账号。

```powershell
& 'C:/Users/89400/.dotnet/dotnet.exe' build src/Momoka.Shell/Momoka.Shell.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
./tools/run-regressions.ps1 -Suite Momoka.MusicViewModelTests,Momoka.PageContractTests,Momoka.PerfListDiagnosticsTests
& './src/Momoka.Shell/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Momoka.exe' --probe-shell --probe-section performance --screen 1
& './src/Momoka.Shell/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Momoka.exe' --probe-shell --probe-section shell,settings,library,music --screen 1
py tools/check-winui-p1.py --self-test
py tools/check-main-tree-policy.py --self-test
```

桌面探针串行、持用户级互斥锁；完整主测试也有共享锁，不能与其并行。检查报告须匹配本次PID＋创建时间＋scope及二进制hash，目录名不能仅靠PID。UI路径出现外部遮挡时保存红回执。音乐临时观察不设永久CLI/产品入口；需要复查时按所存源文本和当前代码核对后在隔离实验件接入，重新记录仪器diff和源hash，不自动将历史仪器插回产品。

文档验收脚本只检查内容、相对文件/章节、XAML库存、版本证据、红项计数和批准边界；内存正负样例防止把音乐缺口/红探针洗绿。脚本通过不证明应用或服务器通过，不能代替上面的运行账本。
