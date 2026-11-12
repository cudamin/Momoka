# P4 音乐墙分页入口

日期：2026-11-13。开始版本：`29e9188b15c8914607b4a4dfdb517773dc128238`。独立开发树：`codex/winui-p4-music`，与主控/主树分开写入、构建和测试。本单元完成实现及离线覆盖，最终主树整合、原生布局/键盘/截图和发布由主控串行补齐。

## 缺口与选择

`MusicLibraryPage.xaml` 的 `MoreButton` 只存在于 `MusicRows.Footer`；`MusicLibraryViewModel.LoadAsync` 与 Core `MusicCatalog.Query` 仍然每页80条、根据总数设置 `MoreAvailable`。专辑、艺术家、歌单转到 `WallScroll → MusicWall` 后，VM有后页但页面没有任何读取它的入口。P1记录的第81项不可达由这个实际树关系复核确认，不把它归因于尚未量测的ItemsView虚拟化。

采用显式原生“加载更多”按钮，和现有曲目分页保持同样请求/末页/失败重试语义。滚动增量加载需要新触发时机、输入反馈和取消策略，不能让一个纯分页入口修复顺带改变网络请求节奏。本方案在 `WallHost` 的 `Auto` 行放按钮，列表占 `*` 行；用户无需先滚完80张封面才能请求下一页。墙区域随 `WallVisibility` 整组切换，按钮按 `MoreAvailable` 实时显隐；现有曲目 Footer 保留。两个入口都有自动化ID和名称。

分页沿用正式 `OnMore → MusicLibraryViewModel.LoadMoreAsync`，没有在页面增加HTTP/身份规则、替换集合或复制分页业务。读取过程保留进度线；失败保留已有80项、错误提示与重试入口；最后一页隐藏按钮。悬浮音乐栏的避让也覆盖新按钮，停止后恢复原有空隙。

## 上游依据及版本边界

已读取Gallery固定提交 `6de91feccadfbb0a796b71caedead5a2284f730e` 的 `WinUIGallery/Samples/ItemsView/ItemsViewPage.xaml` 和 `.xaml.cs` 可执行实现。样例 `SwappableSelectionModesItemsView` 使用 `Grid` 的 `*` 内容行与 `Auto` 输出行，`ItemsView` 设置有限高度；页面处理器通过 `ItemsView.ScrollView.ScrollTo` 归零及响应其 `ViewChanged`。本单元参考有限内容区域与独立操作行的组合，不复制样例数据、事件或实验依赖。

该Gallery提交的 `standalone.props` 为 `2.6.6-experimental`；Momoka沿用当前 split 包，WinUI `2.3.9`、Runtime `2.5.1`。本次只使用既有原生Grid/Button/ScrollViewer/ItemsView和绑定模式，未升级依赖。外层ScrollViewer和ItemsView内层滚动的具体viewport/回收效果仍属P5实测范围，保留原管线，不以布局形状宣布虚拟化已改善。

## 覆盖与证据

`Momoka.MusicViewModelTests`由16项扩充为34项。新增18项从本树XAML读取实际入口、绑定和布局关系，并在编译时严格提取正式 `OnMore`、`Show`、`SetPlayerClearance`；缺失/重复边界直接失败。正式VM和Core分页/会话链仍被编译链接和引用，控件值、图片IO与匿名HTTP为替身。

三种墙分别验证81/100/160条（包括原80+20边界），从正式页面处理器读取第81项及末页；确认集合与首项引用稳定、仅追加、不再发末页之后的请求。其他场景覆盖曲目/收藏、加载中重复点击、第二页失败重试、前一分区迟到响应、释放再附加新身份、空第二页以及播放栏避让/恢复。艺术家夹具按正式 `EmbyClient.MusicItems` 的 `/Artists` 路由造类型，未错误地把缺少 `IncludeItemTypes` 当作歌曲查询。

实际运行：用户级SDK `10.0.400`，Core、专项及Shell Release/x64编译均0警告0错误；34项通过、0失败、0跳过；9个本次文本文件的行尾、末尾换行、BOM与空白检查通过。主树最终原生验收结果由主控在整合后追加。专项命令：

```powershell
./tools/run-regressions.ps1 -Suite Momoka.MusicViewModelTests
```

匿名HTTP仅接受 `music-vm.invalid`，凭据读写被拒绝，没有读取真实设置、访问真实服务、启动原生播放器或推远端。离线入口/绑定关系检查不证明真实WinUI键盘焦点、滚动位置、截图、文字放大或DPI；这些是主控尚需完成的验收。每个墙在600/760/1280DIP、页尾与悬浮音乐栏出现/关闭时应检查按钮可达，第81项与末项应通过实际UI入口取得。
