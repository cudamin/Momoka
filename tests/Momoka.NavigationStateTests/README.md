# 导航目的地恢复回归

构建时严格提取当前生产 `MusicLibraryPage`、`ServerListsPage` 的导航/释放方法，以及 `ShellPage.GoTo`、`Open`、`RememberTrail`、`RestoreTrail`、`GoBack`、`GoForward`、`AdoptContentTag`、音乐容器入口与服务器目录入口。开始/结束哨兵缺失或重复都使构建失败，不退回旧提取内容。`LibraryRequest`与清单模型直接编译链接，Core来自同树。

音乐根页恢复请求Tag，容器保留空Tag；服务器清单根页用`server-lists`，带Item的访问保留空Tag。测试执行实际页面访问和Shell方法，验证音乐根页返回/前进后再次GoTo不重建、深层页仍导航回根、根/容器同页重入不会保留旧Tag、页面释放仍取消已有活动。服务器清单账号入口继续每次显式刷新，此项目不添加同目标守卫。

Frame的页面交接/历史栈、页面ViewModel、选择控件、标题/侧栏呈现和MP历史筛选是边界替身；不证明实际WinUI缓存、渲染、焦点、键盘、滚动或native Frame事件顺序。Core会话只构造在新测试输出目录，HTTP一律拒绝，不登录或读取真实设置/日志/凭据，不启动播放。真实音乐/服务器清单导航由主控在最终整合代码上运行相关shell/music/server-lists分区补齐。

使用项目用户级.NET10 SDK运行`tools/run-regressions.ps1 -Suite Momoka.NavigationStateTests`；独立开发时先restore/build当前测试项目再直接运行其Release DLL。`-p:NavigationSourceRoot=<fixture>`只用于负向提取边界检查，默认读取本树生产源文件。
