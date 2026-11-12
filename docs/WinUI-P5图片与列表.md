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
卡片自己判断当前持有者，旧容器迟到释放不能清掉新容器图。重新进树同步复用有限PosterCache。
局部复用ImageArrivalFade，离树/移除行为释放其订阅；没有新增逐帧布局或窗口级订阅。

保留RowPoster的OneWay绑定、曲目44 DIP/方墙170 DIP、方形比例、UniformToFill、ListView与ItemsView/UniformGridLayout。
PosterLoader使用已有Logical解码宽，服务请求使用既有RequestWidth的2倍/80阶梯，
没有改变图片全局缓存、协议、身份守卫或现有分页。音乐墙分页由P4独立单元实现。

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

## 测试和运行入口

`Momoka.MusicArtworkTests` 编译链接完整生产CardArtwork/CardItem和真实Core，
17项涵盖两模板、四音乐类型、首帧/读取/尺寸、旧项与旧容器、真离树与幻影、源撤销、404与坏解码恢复、
100/1,000/10,000元数据和20个模拟容器。控制台边界不冒充实际WinUI、WinRT解码或GPU显存。
最终当前Release：新专项17/17、页面契约207/207，失败/跳过均0，各自构建0警告错误；
CardArtwork产品和probe当前包编译已通过。加入坏解码项的首轮因测试只交付一次坏响应，
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

量测不能预写提升比例。先在当前同条件记录数值，再判断有限viewport与保留增长；
无GPU/功耗测量，不能由PixelWidth/Height元数据推导解码纹理峰值。
修复前是零图片请求，首图时长不能被当成前后速度改善。

本子任务不驱动共享桌面、不发布、不推远端；主树最终编译/回归、运行成功与失败路径、
实屏和发布由协调会话完成。缺少实际运行证据时本报告不声明P5退出条件已经满足。
