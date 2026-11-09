# 页面动画与渐进图片加载实施计划

> 实施使用 executing-plans / subagent-driven-development；按生产源码回归先红后绿，最后统一运行验证。

**目标：** 保留点击后的页面动画，先显示文字与基础界面，后到的封面、背景与徽标淡入。用户已明确该顺序，不再以取消动画充当性能优化。

**实现：** 保留 Frame 的直接导航和两页有限缓存；Shell 管一次 180 ms 的纯透明度入场，不加旧页离场等待，不整体平移以免污染背景和滚动坐标测量。导航建一个访问所属的图片门控，Loaded 后首 Rendering 再经 UI 队列放行图片工作；已有缓存图保持可见。图片新请求继续用原有取消、来源与访问守卫；晚到图单独约 220 ms 淡入，自动轮播仍保留原交叉切图节奏。

技术栈为现有 WinUI 3 / .NET 10 / XAML Storyboard，不升级依赖。平台依据为本机 WinUI 源码 693f472aeaf89c6dff66256ee6fcd453e5d553b8 的 ThemeTransitions.cpp 和 Frame_Partial.cpp；原 Entrance 先留出 150 ms 旧页退出。新方案不能据此保证点击当帧，构造、绑定和布局仍需实屏测量。

- [ ] ShellPage + BrowsePresentation：不隐藏整个首屏的短入场、首帧门控、快速导航/离页/关动画清理；专用源码链接测试。
- [ ] CardItem + BannerSlide：未缓存图片工作等待门控，旧请求取消后不能重启；PageContract 实际生产源回归。
- [ ] DetailViewModel + DetailPage + ImageArrivalFade：文字 Preview 和 GET 不等图，图批次等待首帧；背景和四类装饰图按 Source 到达淡入，离页/禁动画落定；源码链接测试。
- [ ] HomeBanner + PosterCard：暖返回当前已有图不闪空，真正晚到的图仍淡入；BannerReturn 和图片到达回归。
- [ ] 整合后检查源码/测试、Release 构建和完整主测试，运行受影响 performance/shell/playback 导航路径、宽窄截图、延迟图片下动画录屏。Rendering 只计内部阶段，不当作实屏验收。
- [ ] 合入主树后重新验证，更新实际发布目录和快捷方式，记录结果及严格网页对照尚未覆盖部分；不推送 origin。

当前主工作树 7187 从 master 6e53dbf 继续，其他聊天在主树的 code-review 技能删除不改动。并行写入使用独立应用工作树，父会话串行进行界面验证和发布。

原生验证纠正：2026-11-09 19:49 的匿名真实 Shell 记录 Loaded 时 gatePending=True、imageCalls=1，表明 AsyncLocal 没有可靠跨过 WinRT 回调。改为以控件所属 Frame 查询门控并显式将 Task 传给图片调用；首次赋卡/轮播绑定尚未挂树时等待 Loaded 再请求，回收幻影因 XamlRoot 仍存在而不阻断。门控不得用全局当前窗口代替来源归属。

旧 CardItem.TryPresentPoster 只记录卡片是否展示过，不能表达晚到的新图应淡入，本批删除该标记及对应模型测试；由实际 ImageArrivalFade 完整源码测试覆盖同 Source 不重播、晚到 Source 淡入、离树缓存保持可见。原 NeutralFeedback 构造提取用 SyncPoster 作边界，方法删除后已改为稳定事件声明边界，原指针/焦点断言保留。
