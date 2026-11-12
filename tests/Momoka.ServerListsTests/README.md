# 服务器清单界面回归

链接本树真实 ServerListsService、ServerListsViewModel、PageViewModel 与当前 MVVM 生成器，只替换 WinUI 的枚举边界。匿名 HTTP 验证重复媒体成员身份、排序读回、写成功后刷新失败、离页迟到结果、账号撤销、只读与缺成员标识显隐、跨账号拒绝和空视频清单创建。

先使用项目用户级 SDK restore/build 当前 Release，再 `dotnet run --project tests/Momoka.ServerListsTests -c Release --no-build`。原生窗口、控件选择、命令栏宽窄与保存快照由 `--probe-shell --probe-section server-lists` 或检查台 `InspectServerLists` 检查；此控制台测试不代表 WinUI、真实服务器或播放通过。

另覆盖创建已确认但目录刷新失败后禁止重复创建，以及Plex最后一次权限核对降级时目录/成员仍只读、读回恢复后再开放。同步集合通知、账号切换、迟到异常及多页完整性由相邻 `Momoka.ServerListsOwnershipTests` 执行同一生产实现。
