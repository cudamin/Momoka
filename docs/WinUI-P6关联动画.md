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
