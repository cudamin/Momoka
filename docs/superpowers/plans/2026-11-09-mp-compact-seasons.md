# MP 详情紧凑布局与季区域材质

> **For agentic workers:** 使用 executing-plans 按本计划实施；用户已撤销 Demo 前置规则。

**Goal:** 封面下方紧接简介，季标题去掉首播日期和评分，入库摘要移到爱心左侧，整个季区域保留亚克力背景。

**Architecture:** 复用 BodyPrimary 作为 HeroRow 左列，让 HeroSurface 与 HeroOverview 自然垂直排列。右资料卡保留全部字段与自然高度，下方季和相关列表保持全宽。SeasonPanel 从沉浸模式共用透明画刷清单移出，沿用 EgBodyPanelStyle 的主题亚克力与高对比度回退。季摘要仍绑定原 HeaderStatus，命令和数据链不变。

**Tech Stack:** WinUI 3 / C# / .NET 10.0.400，既有主题资源和匿名原生探针。

**Spec:** 用户本聊天四条截图修改要求。历史授权已在聊天 01a1b4ec-c2e3-7432-ad3e-3ec9ced2481d 核对用户原话“不要demo”“去掉这个规则”；无需再次确认。

## 范围与检查

- [x] 修改既有页面结构回归与原生几何/材质/季摘要检查，验证旧布局不能满足新要求。
- [x] 修改 MoviePilotSubscriptionPage.xaml、MoviePilotSubscriptionPage.Showcase.cs、MoviePilotMediaSeasonsView.xaml；简介空标语不占额外一行。
- [ ] 当前树 restore，Release/x64 编译 Tests/Core/Shell；运行主测试、订阅页面专项、相关原生宽窄与主题截图。
- [ ] 独立复审、差异检查、提交；安全整合主树后重新验证并发布桌面交付目录，不上传。

重点：长制作公司资料不裁切；长简介/长入库摘要窄窗可换行，爱心可达；切换模式季底不消失；电影无季不留空板；季默认收起及订阅状态更新不变。

## 执行记录

- 基点 ca812b6，e859 已是隔离工作树，初始干净。主树三个既有技能删除不属于本任务。
- 方案取舍：不限制右卡高度、不加内部滚动；正文分列只限封面和简介，季与相关列表仍全宽。
- 尚未验证、提交、合并或发布。

- 官方源码：Frontend v3 609ea05eac7efc7c2cb31edcf5364d26c9c592f1 的 MediaDetailView.vue 季标题无首播/评分、状态在爱心左；Backend v3 6d9cf06085a8cbef0f3a7ed73aff99ba68d209c9 的7个相关文件与前次55da2238一致，无协议变化。未核对部署版本。
- 初始当前Tests/Core和Shell均0警告0错误；页面结构红35/36（仅新布局），修改后36/36。完整测试首次4219/4235，16项全部为另一聊天自检锁占用，失败日志保留。
- 独立复审零必要缺陷，补空Tagline隐藏与检查；最终重新编译中。

- 最终Shell编译0警告0错误，页面专项36/36；完整测试重跑4235/4235，0失败0跳过。差异编码检查通过，局部format退出0但报告工作区加载警告，未宣称完整格式门禁通过。原生与整合交付待桌面/主树上一聊天释放。
