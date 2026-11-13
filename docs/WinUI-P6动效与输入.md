# P6 动效与输入实施记录

日期：2026-11-13。基线：`29e9188b15c8914607b4a4dfdb517773dc128238`。独立树：`C:\Users\89400\.codex\worktrees\winui-p6-input-20261113\Momoka`，临时分支 `codex/winui-p6-input`。本记录对应独立实现单元，主树最终整合、实屏与交付由协调会话完成。

## 实际缺口与改动

`SurfaceMotion` 原来只在 Enter/Exit 时读取动画开关，进行中关闭系统或应用动画仍继续。完成后又将 `_board` 置空，保留 HoldEnd 时钟。现在只在活动动画期间观察两种偏好，UI 线程立即写终值；退出落定回调兑现一次，反向/取消使旧回调失效；完成与失败均 Stop 并解除订阅。零时长直接落定。

`GlyphPulse` 原来每次创建新的 Storyboard，无旧实例归属、离树清理与偏好变更。现在每个目标只保留一个脉冲，连点先取消旧钟，完成/离树/减少动画时复位缩放并摘订阅。未量到尺寸及平台拒绝启动时保持可用的普通动作。

`HomeMotion` 原来应用关闭可停入场，系统运行期变更无监听。现在每个实际入场只在播放期间监听系统事件，完成/离树解除，旧完成不干扰后来入场。缺模板目标继续正常跳过，不为了动效实现视口外容器；动画关闭不登记等待 Loaded 的新入场，零时长和启动失败恢复可读内容。

`ShelfStrip.QueueFocusRetry` 原来在 Focus 持续返回 false/容器创建失败时，低优先级回调会立刻再排自身，形成无界 dispatcher 工作。现在一次回调失败后只保留待聚焦索引，等待后续 Loaded/SizeChanged；队列拒绝恢复标志，换数据或离树的代次使旧回调失效。Home/End、PageUp/PageDown 与带内左右边界的原有语义保持。纯 UI 焦点逻辑就近保留，不为局部呈现行为搬入 Core。

## 官方源码依据与选择

已读 Gallery 固定提交 `6de91feccadfbb0a796b71caedead5a2284f730e` 的 `WinUIGallery/Samples/ConnectedAnimation/ConnectedAnimationPage.xaml.cs`、`SampleSupport/SamplePages/SamplePage1.xaml.cs`/`SamplePage2.xaml.cs`、`Samples/ItemsRepeater/ItemsRepeaterPage.xaml.cs` 及 `standalone.props`。

Gallery 在导航前 PrepareToAnimate，目标页 GetAnimation/TryStart，导航用 SuppressNavigationTransitionInfo。Repeater 示例用 TryGetElement、GetOrCreateElement、StartBringIntoView 和焦点事件，realized 容器由 ElementPrepared/ElementClearing 登记。Momoka 的有限焦点恢复继续使用这些原生路径；横带本地滚动保留以避免外层页面跟着 BringIntoView 下跳。Gallery 依赖 `2.6.6-experimental`，本批保持 Momoka 已锁定稳定依赖。

本动效生命周期单元开始时，生产代码尚无 ConnectedAnimation 入口；先修取消与输入缺口，未把普通入场当作关联动画通过。后续独立单元已经实现来源/目标/返回的身份桥接及自身 Primary 海报关联，并取得 HWND XAML 岛的真实 Started/Completed 和普通回退证据，范围见 [关联动画](WinUI-P6关联动画.md) 与 [执行记录](WinUI-P4-P8执行记录.md)。本节历史测试记录不替代最终整合版验收。

## 当前测试

各项目均先 restore，再以 SDK10.0.400、Release、单节点、BuildInParallel=false、UseSharedCompilation=false、MSBuildNodeReuse=false 编译当前源码。构建均0警告0错误。

| 范围 | 结果 | 边界 |
| --- | --- | --- |
| UiMotionTests | 34通过，0失败，0跳过 | 原样三动效文件及原样提取货架焦点逻辑；假 XAML、时钟、系统事件 |
| PageContractTests | 207通过，0失败，0跳过 | 生产卡片、图片、焦点宿主与选择输入 |
| HomeFoldCompletionTests | 66通过，0失败，0跳过 | 生产翻档/返回/回收；新增空轮播 Ambient 发布 |
| SettingsProbeContractTests | 36通过，0失败，0跳过 | 原样 HomeMotion/设置控制流与本树真实 Shell VM |
| BrowsePresentationTests | 19通过，0失败，0跳过 | 生产导航首帧门控、取消、关动画与失败 |
| SettingsRowsMotionTests | 46通过，0失败，0跳过 | 行删改/反向/减少动画/离树 |
| SettingsPriorityMotionTests | 24通过，0失败，0跳过 | 排序、拖拽、打断与队列 |
| PlayerInputRecoveryTests | 17通过，0失败，0跳过 | 假后端的冻结/快捷键恢复；未真起播 |
| Shell Release | 0警告，0错误 | 最终产品源码编译；未在独立树发布 |
| 自动化定位扫描 | 普通控件0缺失句柄 | 模板/复用范围仍需实际UIA |

HomeFold 初次编译报2错：当前 HomeBanner.Apply 已新增 AmbientChanged/Transparent，旧夹具缺其边界。本批补最小事件/颜色边界并测试空清单确实发布透明环境色，保留其余65项。测试生成器第一次结束标记与单行注释不匹配，修正为当前唯一标记；没有放宽断言。

独立原始生产负例使用同一34项：13通过、21失败、0跳过，证实关动画不落定、旧脉冲未停、持续拒焦形成无界队列；原始复制项目另有2条未用字段警告，仅属负例。恢复当前生产文件后34/34。负例文件仅在本树 ignored work 下，不进入产品或构建前提。

## 主树运行验收与仍未覆盖

适用现有 `--probe-shell` 分区为 settings/search/shortcuts/shell/library/playback，选择规则见[开发与验证](开发与验证.md#标准命令)。其中 CardFocusAndSettingNamesAsync 读取真实卡片宿主与设置 peer 名称，InputFocusControlsAsync 核对输入焦点，SettingsEntranceAsync 与 HomeNavigationReturnAsync 核对实际动效与返回；原生批量菜单完整 Escape 回焦可用 inspect 的 InspectLibraryBatchKeyboard，按 `batch-commandbar-keyboard-stage.txt` 给真实按键。

本子任务禁止驱动桌面，以上实际UI检查未执行。完整匿名登录→切来源→搜索→浏览→详情→设置→返回的纯键盘任务链、运行中关动画真实帧、Narrator 读出、触摸/笔输入、高对比度实际像素及关联动画均未覆盖。代码/假时钟通过不能代替这些验收，也不等于 P6 已完整交付。无真实服务器、用户设置、实际播放、注册表、远端推送或公共发布操作。
