# P6 海报到详情关联动画

日期：2026-11-13。本单元在动效生命周期P6 `b11174f` 及P7隔离研究之后实施，保持独立提交。只有现已显示的同视频海报参与，不改变音乐、真实播放、服务器命令或页面权限。

## 原生流程与归属

主页卡片和媒体库实际PosterCard调用时，对其原Poster Image执行ConnectedAnimationService.PrepareToAnimate。前件是Bitmap尺寸已知、图在视口可见、类型为Movie/Series/Season/Episode，捕获来源仍是当前身份。媒体库只找已经实现的当前CardItem对应PosterCard，不为动效GetOrCreate或重建整个列表。

`CardDetailTransition` 的票据按Frame隔离，源PosterCard与源Page只弱引用；记录完整Item IdentityKey和UserStateOwner，避免同ID跨账号复用。Shell导航只在票据与DetailRequest匹配或明确返回原页时接管淡入。DetailViewModel.Attach重置完成后，等待当前CurrentItem、Loaded/Layout及真实可见PosterImage，再TryStart。等待最多700ms，缺图、窄窗隐藏、账号失效、平台失败或取消都回退普通呈现；不等待动画再导航。

成功时停止这个PosterImage的ImageArrivalFade，并抑制BrowsePresentation的整页透明淡入，首帧图片门控保留。失败恢复原180ms淡入；其他图片仍沿自身到达淡入，不停止设置或其它Frame。快速重入、Frame离树、目标卸载、导航到不相关页面、系统/应用关动画均取消；Completed解除活动系统/应用订阅及native动画。普通返回只有原弱源Page确实重入且原图仍存在并可见才TryStart，不强制realize。

## 源码与API证据

Gallery固定 `6de91feccadfbb0a796b71caedead5a2284f730e`：ConnectedAnimationPage在导航前Prepare、用SuppressNavigationTransitionInfo抑制重复过渡；SamplePage1/2在目标页GetAnimation/TryStart；ItemsRepeater样例明确其无ListView自动关联接口，需手动找已经实现目标。已读这些XAML及code-behind，不引入Gallery的 `2.6.6-experimental` 包。

本机恢复包WinUI2.3.9提供PrepareToAnimate/TryStart/Cancel/Completed；Shell实际编译验证这些签名。框架源码支持证据为microsoft-ui-xaml `693f472aeaf89c6dff66256ee6fcd453e5d553b8` 的 `ConnectedAnimationService_partial.cpp`，GetForCurrentView取得当前DXamlCore服务，Prepare操作CUIElement。该源码没有普通Window前件，仍不能替代当前二进制在HWND XAML岛的实际运行结论。

## 离线结果与运行入口

原样生产CardDetailTransition专项13/13，0失败/0跳过，含错账号同ID、缺/隐藏源目标、启动拒绝/异常、取消异常、超时、减少动画、旧Completed、Frame不相关导航、原弱源返回与队列拒绝。BrowsePresentation扩22/22，证明成功抑制重复整页动效而首帧门控保持，失败恢复普通淡入，先成功后Loaded不再重播。DetailImageArrival20/20、NavigationState17/17、HomeDirectory82/82、UiMotion34/34、PageContract207/207；当前各项目先编译且0警告0错误。专项已登记统一manifest与sln，runner自测22/22。

匿名真实实验入口 `--probe-shell inspect --screen 1` 的 `InspectCardDetailConnected`。它使用真实XAML岛、现有PosterCard、真实详情页、固定本地生成图片和`.invalid`匿名传输；记录成功前进、原源返回、动画关闭、缺源及离页取消，保存 `card-detail-connected.json` 与 `card-detail-connected-forward-screen.png`。启动失败必须记录红项，不能因为API存在就宣称成功。没有真实服务器、媒体文件、用户设置或播放。

本子代理禁止驱动桌面，真实前进/返回动画与帧、来源回收/快速反向实屏、Narrator和触摸未执行，协调会话负责同轮运行并将事实补到主树报告。本单元源码与离线通过不等于P6完整流程或正式交付通过。

## 系统事件异常补修

同轮复核发现系统AnimationsEnabledChanged退订异常会打断Dispose的普通页面回退，准备阶段add失败后清理也可能把异常带回卡片导航。现在每次观察登记自己的偏好代次委托，先撤销本地资格再尝试native退订；失败不阻止取消native动画与BrowsePresentation回退，保留旧系统委托也不能取消后来票据或新返回。Frame的Stopped/Failed在票据层立即取消匹配准备，不等待700ms超时。

新增add前后/remove前后失败、保留委托迟到、完成后旧事件、返回重新观察失败、Stopped/Failed即时取消与同ID重登/卡片复用测试，关联专项22/22；BrowsePresentation22/22继续通过。测试明确保留remove前失败的模拟订阅以确认「仅本地失效」，不会冒称native已经释放。

进一步复核补祖先ScrollView/ScrollViewer可视区域交集，root内但横带/外页视口外的已实现图片不能作动画源。新增嵌套视口用例后当前23/23。原0334a8f生产文件配新增22项的独立负例13通过9失败（前几项残留异常也影响后续），保留原异常，恢复当前后全部通过；负例不作为产品构建前提。

## 活动原生动画完成兜底

准备目标700ms等待保持；原生TryStart成功后的活动观察增加1200ms兜底，完成事件缺失仍取消native动画及观察。前进保留弱返回线索，返回从准备阶段重开兜底，超时彻底取消票据。每次超时委托有本地代次，旧Tick不能处理新原生动画；重复CompleteReturn只排一次。新增缺Completed、返回未投递超时与旧Tick、重复完成合并三项，当前26/26。该兜底只取消可选动效，不延迟页面导航或服务器请求。
