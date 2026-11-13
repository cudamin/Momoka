# 海报到详情的原生关联动画回归

原样链接 `Views/CardDetailTransition.cs`，只把XAML树、实际图片、ConnectedAnimation API、Frame、源身份和系统事件替换为可控边界。13项覆盖可见视频海报前件、目标就绪与身份、TryStart拒绝/异常、错源同ID、超时、取消、减少动画、原弱源返回、队列拒绝与平台取消异常、快速重入与窄窗/错返回页面。

当前34通过、0失败、0跳过；Release编译0警告0错误。新增真实Primary取图引用限制、图版本漂移、原生Completed add/remove异常、前进/返回启动/完成回执及离页ReleasePoster→原图返回恢复。系统事件故障、取消重入、Stopped/Failed立即退出、重登/源复用、祖先滚动视口裁剪、缺Completed超时及重复返回合并仍覆盖。native退订抛错时仅确认本地资格撤销与普通回退，不能称native资源已释放。运行命令遵循项目单节点/禁共享编译/nodeReuse约束。该测试不证明XAML岛原生合成与视觉连续性。

真实匿名入口为 `--probe-shell inspect` 的 `InspectCardDetailConnected`，使用正式缓存Page导航和Frame.GoBack，分别记录returnTryStart/Completed/Timeoutfallback，覆盖真实详情前进、返回、图版本漂移前进回退、缺返回源、动画关闭和取消，保存 `card-detail-connected.json` 与实屏。主树协调会话负责运行；不得当作真实视频播放或Narrator验证。
