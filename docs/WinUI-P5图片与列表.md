# P5 音乐图片与列表

日期：2026-11-13。开发树：`winui-p5-images/Momoka`，分支 `codex/winui-p5-images`，
输入提交 `29e9188b15c8914607b4a4dfdb517773dc128238`。本单元是P5中P1已证实的音乐缺图链路；
媒体库、首页货架、长集表和 ItemCommands 服务化不以本单元冒称已重构。

## 问题和改动

`MusicLibraryPage.xaml` 的曲目与方墙模板均只绑定 `CardItem.RowPoster`；
`MusicLibraryPage.xaml.cs` 没有领取或读取入口，VM只造CardItem和离页Abandon。
因此即使元数据含Primary标签，模板首图也一直为null，P1的零GET与此链路一致。

两个现有原生Image现在用 `CardArtwork.Item` 附加行为承接图片需求。
首次绑定离树只记项，Loaded且有XamlRoot才Claim/Ensure，传控件所属BrowsePresentation门控；
换项放开旧持有者再领取新项；回收幻影仍有XamlRoot时忽略卸载；真离树释放并停止晚到图回填。
随后实际量测证实仅看XamlRoot不足：同属主回收池仍在树中。当前实现另用Realized状态，
音乐墙的内部Repeater在ElementClearing结束需求、ElementPrepared恢复，
曲目用ListView的InRecycleQueue；绑定换项和幻影Loaded不能绕过已回收状态。
卡片自己判断当前持有者，旧容器迟到释放不能清掉新容器图。重新进树同步复用有限PosterCache。
局部复用ImageArrivalFade，离树/移除行为释放其订阅；没有新增逐帧布局或窗口级订阅。

保留RowPoster的OneWay绑定、曲目44 DIP/方墙170 DIP、方形比例、UniformToFill、ListView与ItemsView/UniformGridLayout。
PosterLoader使用已有Logical解码宽，服务请求使用既有RequestWidth的2倍/80阶梯，
没有改变图片全局缓存、协议、身份守卫或现有分页。音乐墙分页由P4独立单元实现。
WallScroll由滚动容器改为有限高度Grid，MusicWall自身ScrollView为唯一竖滚宿主，
其Padding承接音乐栏避让；P4加载更多仍在独立Auto行，不挪回列表内部。

## 上游证据

已读取Gallery固定提交 `6de91feccadfbb0a796b71caedead5a2284f730e` 的
`WinUIGallery/Samples/ItemsView/ItemsViewPage.xaml` 与 `.xaml.cs`：
ItemContainer内原生Image、UniformGridLayout与稳定ObservableCollection；
Gallery模板图源已是ImageLocation，与Momoka需异步读取CardItem不同，不能复制它的单次赋值就认为图片已加载。
Gallery同提交使用实验SDK，本单元保留Momoka现有WinUI2.3.9/Runtime2.5.1。

补读 `microsoft-ui-xaml` 固定提交 `693f472aeaf89c6dff66256ee6fcd453e5d553b8` 的
`controls/dev/ItemsView/ItemsView.cpp::OnItemsRepeaterElementPrepared/OnItemsRepeaterElementClearing`：
框架订阅内部Repeater，回收时清选择与自动化位置；这是框架回收实现证据，
不证明当前音乐墙的外层ScrollViewer已提供有限viewport，也不替代Momoka图片属主。
当前包编译证实ItemsView无TryGetElement，运行量测从实际内部ItemsRepeater调用TryGetElement。
同提交 `controls/dev/Repeater/ViewManager.cpp::ClearElementToElementFactory` 先发ElementClearing、
仅在MustClearDataContext为真时清DataContext，再调用工厂回收；
`RecyclingElementFactory.cpp::RecycleElementCore` 将元素按reuse key和parent留入池。
所以树内容器、layout realized和当前图需求必须分开读，不能把单次Unloaded当全部释放的证明。
同提交ViewManager::GetElementFromElementFactory先Recycle/ProcessBindings处理x:Bind；
只在普通Binding分支置MustClearDataContext=true，解释了音乐x:Bind池项为何不会清掉附加Item。
`ItemsView.xaml`将ItemsView.Padding绑到内部Repeater.Margin；本单元保留平台模板并从ItemsView接避让。
`dxaml/xcp/dxaml/lib/ListViewBase_Partial_ContainerPhase.cpp::RaiseContainerContentChangingOnRecycle`
在模板Recycle前以InRecycleQueue=true发事件；afterPrepare将其置false且先保证模板量测，
生产回归覆盖这两种容器输入的需求收放，不复制框架算法。

## 测试和运行入口

`Momoka.MusicArtworkTests` 编译链接完整生产CardArtwork/CardItem和真实Core，
29项涵盖两模板、四音乐类型、首帧/读取/尺寸、旧项与旧容器、真离树与幻影、源撤销、404与坏解码恢复、
100/1,000/10,000元数据和20个模拟容器。控制台边界不冒充实际WinUI、WinRT解码或GPU显存。
新增6例保留Tree/XamlRoot/x:Bind项的同owner池、prepared无Loaded、首次订阅池区分、
ListView回收和旧持有者/重入事件；新增3例预算/页面入口，拒绝真实超预算读数反例。
当前Release：新专项29/29、音乐VM与P4分页39/39，失败/跳过均0，各自构建0警告错误；
原页面契约207/207是上一单元的覆盖，本修复不复用其日志称已重验。
加入坏解码项的首轮因测试只交付一次坏响应，
真实Store正在重取而非已回传失败，17项中5项失败（后续4项为前例未清边界）；
已按真实Store的坏图恢复语义交付第二次失败并finally清理，保留断言后重新17/17。

新增独立 `ShellNavigationProbe.MusicArtwork.cs`，自动music分区与检查台 `InspectMusicArtwork` 都可执行。
真实MusicLibraryPage的Rows直接注入100/1,000/10,000条匿名元数据，四分区各走首屏→末尾→首屏；
仅假HTTP返回固定400×400方图，分页接口返回空，避免把网络页数与容器量测混合。
每组追加输出 `music-artwork-performance.json`，包含元数据构造/首图时间、GET数、
实际Image.Source数、内部TryGetElement/列表ContainerFromIndex、树保留容器、持有RowPoster数、
CardItem当前wanted和实际live holder数、
viewport/DIP/scale、帧p95/max与工作集。只有tracks/albums100各一张实屏，共2张。
反例移除一个可见图需求必须被实际入口检查拒绝，再恢复原图；离页所有CardItem首图为null。
首尾各确认原expected项ID、聚焦与实际可见bounds；x:Bind模板身份从CardArtwork.Item读，
不把DataContext为空误当缺项。仪器同步分区选中态、总数与“匿名音乐容器量测”标题，
直接注入元数据仍不冒称网络分页或物理键盘输入。

## 实际基线、修复预算和视觉边界

协调会话2026-11-13读取的`173627596-7556`运行最终共有12组。
Tracks 100/1000/10000返回首屏Realized19，但Wanted/LiveHolders可21、HeldPosters可21；
Albums/Artists/Playlists的100/1000/10000条内视口分别5036/50576/505976 DIP，外视口均557，
返回首屏Realized21而图片/需求25–26。realized最高26，证明列表已在虚拟化，
缺口是滚动宿主边界与池图片需求，不能改写成“全部10000项都建了”。

新预算以这次1100×760/scale1基线制定：实际layout项最多34（原26加8个明确平台cache/首测波动），
图片、CardItem持图、wanted和live holder都不超过当时realized，池额外需求容许0；
有限Grid实际高度与内部viewport差不超过1 DIP。
上述预算是此受控窗口的验收，不推广为全部页面/文字缩放/显示器的万能上限。
首图原读数约176–227ms；未给帧、工作集预设性能提升指标，也没有GPU显存/功耗测量。

首轮通过Hidden启动再ShowWindow拍出的两张空客户区图保留为失效证据，不能算视觉通过。
协调会话已用正常可见启动的`175841122-13352`检查台看到标题、封面和滚动区，
原黑图与该辅助显示方式有关，不据此宣称产品黑屏。
本次finiteviewport/explicit回收的最终正常可见运行仍须协调会话重验。

finite产品首次原生检查`190016656-31692`：Albums100初始realized/held/wanted=21/21/21、
宿主/内部viewport=589/589，证明两条修复已落实；首/末项检查仍红：
图片末项ID99正确且Focus=true，但Y4807/高229/viewport589，分页末项80为Y10120/229/549。
原probe只受理ScrollTo后立即Focus，没有等待关联完成，不能把此结果直接判产品不可达。
当前工具统一用既有ScrollProbeAsync等真实correlation及offset、Idle和帧，
再等Focus引出的BringIntoView关联和Idle后读同一严格bounds。
`music-scroll-*.json`保留requested/前后offset、presenter、extent和viewport，
`music-focus-*.json`保留焦点前后bounds、原expected ID和bring/completed关联；
没有放宽可见判据，也不从Source非null推断实屏已呈现。

## 曲目原生绑定时序

固定源码`ListViewBase_Partial_ContainerPhase.cpp`同步Raise CCC之后才ProcessBindings phase0；
首帧完成后的BrowsePresentation门控已经完成，立即开启需求确能对池里旧项发GET。
曲目处理现在先结束需求，再RegisterUpdateCallback默认下一phase，
回调只认同container、同准备代次、同args.Item和实际CardArtwork.Item；离页或回收拒绝。
`ContainerContentChangingEventArgs_Partial.cpp`默认注册m_phase+1；框架回收清callback/wants，
执行阶段先清delegate再invoke，没有重新登记就结束，不引入第二个Dispatcher等待队列。
新增3例模拟精确“CCC旧绑定→新x:Bind→下一phase”，旧图tag换成无缓存标识确保检测额外GET；
旧回调、新准备、回收和离页均拒绝，当前有效回调恢复读取。该源链和回归不代替实际WinUI phase运行。

量测不能预写提升比例。先在当前同条件记录数值，再判断有限viewport与保留增长；
无GPU/功耗测量，不能由PixelWidth/Height元数据推导解码纹理峰值。
修复前是零图片请求，首图时长不能被当成前后速度改善。

本子任务不驱动共享桌面、不发布、不推远端；主树最终编译/回归、运行成功与失败路径、
实屏和发布由协调会话完成。缺少实际运行证据时本报告不声明P5退出条件已经满足。
