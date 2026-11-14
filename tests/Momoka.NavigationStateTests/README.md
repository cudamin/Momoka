# 导航目的地恢复回归

构建时严格提取当前生产 `MusicLibraryPage` 的导航/释放方法，以及 `ShellPage.GoTo`、`Open`、`RememberTrail`、`RestoreTrail`、`GoBack`、`GoForward`、`AdoptContentTag` 与音乐容器入口。开始/结束哨兵缺失或重复都使构建失败，不退回旧提取内容。`LibraryRequest` 直接编译链接，Core 来自同树。

音乐根页恢复请求 Tag，容器保留空 Tag。测试执行实际页面访问和 Shell 方法，验证音乐根页返回/前进后再次 GoTo 不重建、深层页仍导航回根、根/容器同页重入不会保留旧 Tag、页面释放仍取消已有活动。

Frame 的页面交接/历史栈、页面 ViewModel、标题/侧栏呈现和 MP 历史筛选是边界替身；不证明实际 WinUI 缓存、渲染、焦点、键盘、滚动或 native Frame 事件顺序。Core 会话只构造在新测试输出目录，HTTP 一律拒绝，不登录或读取真实设置/日志/凭据，不启动播放。真实音乐导航由主控在最终整合代码上运行相关 shell/music 分区补齐。

使用项目用户级 .NET 10 SDK 运行 `tools/run-regressions.ps1 -Suite Momoka.NavigationStateTests`；独立开发时先 restore/build 当前测试项目再直接运行其 Release DLL。`-p:NavigationSourceRoot=<fixture>` 只用于负向提取边界检查，默认读取本树生产源文件。
