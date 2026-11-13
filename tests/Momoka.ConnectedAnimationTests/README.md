# 海报到详情与演职人员到人物页的原生关联动画回归

2026-11-13：按 WinUI Gallery `6de91feccadfbb0a796b71caedead5a2284f730e` 的 `ItemsRepeaterCollectionPage.xaml.cs` → `DetailedInfoPage.xaml.cs` 保留原生准备／Suppress导航／目标图片启动顺序。前进默认 Gravity，返回 Direct；同 Primary 源图立即交接，不等高清请求，已有高清图不降级。当前 48 项，0 失败 0 跳过：海报→详情的身份、目标、超时、取消、减少动画、返回与平台事件异常继续覆盖；新增人物卡→人物页 8 项，覆盖可见／当前账号／Primary 前件、目标照片交接、人物身份与目标可见性回退、导航停止／失败、应用与系统动画关闭、快速重入和高清不降级。

原样链接 `Views/CardDetailTransition.cs` 与 `Views/PersonTransition.cs`，只把 XAML 树、实际图片、ConnectedAnimation API、Frame、源身份和系统事件替换为可控边界。人物票据只做前进：详情页没有缓存，返回时人物页和演职人员排都已重建，没有可靠来源；不为了动画缓存详情页或强制创建源卡片。原生退订抛错时仅确认本地资格撤销与普通回退，不能称 native 资源已释放。

真实匿名入口为 `--probe-shell inspect` 的 `InspectCardDetailConnected`，使用正式缓存 Page 导航和 Frame.GoBack，分别记录 returnTryStart/Completed/Timeoutfallback，覆盖真实详情前进、返回、图版本漂移前进回退、缺返回源、动画关闭和取消，保存 `card-detail-connected.json` 与实屏。人物页票据的真实 XAML 岛运行证据仍需在交付验证时补；该测试不证明 XAML 岛原生合成与视觉连续性。
