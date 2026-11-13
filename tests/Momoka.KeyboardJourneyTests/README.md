# 匿名键盘流程工具回归

直接compile-link `ShellNavigationProbe.KeyboardJourneyFixture.cs`，验证正式工具匿名HTTP和最终/焦点/按键读数的成功与拒绝路径。只读两个`.invalid`虚构来源；人工登录固定虚构账号密码，所有其他写入拒绝，没有真实设置、桌面或播放。

```powershell
./tools/run-regressions.ps1 -Suite Momoka.KeyboardJourneyTests
```

此离线测试不创建WinUI窗口，不证明真实Tab/Enter、焦点/peer或框架返回。实际检查台 `InspectKeyboardJourney` 在阶段JSON指定的进程/HWND等待根代理的真实按键；`InspectKeyboardJourneyFailure`用错误字段名测试原生失败路径，预期报红。两条实际运行均由主控串行执行。

P8媒体信息定位另外编译时严格提取真实`DetailPage.ScrollToInfo`，缺失或重复入口使构建失败；匿名范围夹具核对目标1364超出可滚1346时只请求边界一次，后续8眼不重开事务，正常/负上沿和无内容保持原语义。共享MediaInfo10行源及严格渲染反例也在本项目，当前12项。控件端点只记录参数，原生重复请求/effectiveviewport仍由InfoTable检查台实测。

手动入口`InspectKeyboardJourneyManual`提供屏内12步中文提示/取消按钮，manual每段5分钟、检查台20分钟驻留只在点击此入口后启用，普通阶段仍60秒。文案/时限与提示独立行、状态播报、取消令牌/解绑的契约使当前16项；实际屏幕/用户按键不在离线测试边界内。

原生搜索名称另有资源/XAML链契约，当前17项；Library SearchInput与SettingsSearchBox的ownerName/resw提供具体搜索作用，保留WinUI模板的owner→内部TextBox原生复制。真实probe核对这两层名称相等，不能在Focus阶段造fakeName绕过产品缺口。

`InspectKeyboardJourneyManualFromSearch`只允许manual，前六步身份以固定匿名资料播种并逐段标记`resumedFromSearch/seededIdentity`，第7步之后保持真实按键与最终回执检查；不冒称前半手动。两项规则/源码契约覆盖恢复限制和取消继续抛出，实际用户后半链须单列运行证据。

P8返回滚动增加原样提取的`KeepScrollThroughReveal`及其实际事件/计时/取消方法，控件和时钟为窄边界；七项覆盖临时可滚范围恢复、迟到加载、在途correlation去重、用户输入放手、重复登记和释放后迟到回执。另三项链接数字轨迹写入/失败重试与正式自检接线，当前本树31/31。实际WinUI事件与原自检仍由主控串行复验，回归不会启动播放、保存账号或操作桌面。

P8信息表再红的诊断链接真实 `ShellSelfCheck.InfoGeometry` 记录器，仅用几何/FindName/行实现边界，
验证每拍数字索引与坐标保留、10→7不覆盖先前证据、写入失败最终仍红及后拍重试、缺正式目标拒绝和非有限坐标写null。
源接线检查仍保留正式ReadInfo、原定位与返回后读取，不冻结旧通过快照或改变10行判据。
当前38/38、0失败0跳过；真实WinUI几何和原生滚动关联没有在离线夹具中执行。
