# R16 资源、需求与界面边界

本表对应 [R16报告](R16.md) 与 [验证记录](R16-verification.json)。来源寿命、需求取消、缓存代次和控件回收是不同边界；匿名控制流不能替代真实窗口输入或像素。

| 路径 | 所属/释放者 | 已核守卫与问题 | 实际证据限制 |
| --- | --- | --- | --- |
| EmbyImageStore | 应用store；请求scope/共享需求 | key含源/用户/条目/图种/tag/宽；6执行、48排队键、128需求；最后需求撤销，执行槽等工厂及取消回调结束 | 真Core/匿名HTTP；无实服/用户盘 |
| GetDecodedAsync | 每次decode请求及相同盘字节 | 坏数据/模糊错误一次有界重取；身份/取消/权限/低内存不当坏图；成功才修盘 | ImagePerformance与Page恢复；Banner底图绕过为002 |
| PosterCache/BudgetCache | UI线程静态缓存 | Poster按容量、背景按字节淘汰；缓存保留不等于页面释放 | 004旧卡持图绕开淘汰；未测进程峰值 |
| CardItem首图 | 当前holder/_wanted/resizeWanted | 来源/取消/宽度保持；最后可见需求与临时离树分开 | 003丢候选；实际CardItem配fakeBitmap |
| CardItem拼图 | 同上；DTO页面仍保存 | 下载回填检查wanted，cache-hit漏检；旧holder释放继续守身份 | 六对照确认004；无真实回收事件 |
| BannerSlide | 页面幻灯片、asked/load/effect代次 | Release取消load/effect，StillWanted拒绝迟到；缓存代次防Clear后写回 | 002坏盘失败；真实WinRT/fakeXAML，非画面 |
| DetailHeroCache | 单例session；ticket/owner/tag/宽/半径/算法 | 24MiB预算、原料/位图共计、源退出/Dispose退订、Clear代次贯穿 | 实际cache8专项；主树算法增量只读另核 |
| CoverBackdropImage | 播放封面请求/cover代次 | 两边1280、921600px/3,686,400字节BGRA；后台解码、前后取消、发布回UI | 实WinRT及消息心跳；非真实Frame/DWM、EXIF/ICC未验 |
| 字体目录/解析 | FontLibrary会话单task、immutable目录 | 1MiB name表、512字体、每类64候选、128 UTF16码元；后台scan | 合成目录/14预算；不扫系统字体，R15搜索重置沿旧号 |
| ThemeHost | 应用主题画刷及Changed订阅 | 改对象色保持绑定；Dark/Light都更新、HC字典不改 | 角色/合同，非五主题真实截图 |
| ControlBackgroundBrushes | 每控件副本、页面主题/外观通知 | 资源对象稳定；材料0档错误见001；master默认半透明分支另核 | 参数替身与固定官方源码；2.3.9精确revision/像素缺 |
| DialogDismiss | dialog附加行为；Closed清订阅 | 独立Smoke Popup；先Handled再Hide，None取消，Closing可拒绝 | 标准10只替身Press；不声称真鼠标/不穿透已验 |
| FocusWatch/HoverWatch | 控件attach/detach；当前父子/窗口坐标 | Focus越过自身找Button/ItemContainer；Hover用系统位置、有限timer | 控件边界/源码；真实遮挡和HC/UIA仍缺 |
| EgPicker | 控件Popup与queue | 首开前ShouldConstrain；闭合清Width；排队加宽无状态门 | 用户输入先后未核，不编号 |
| Library/Shelf布局 | 当前页面/scroll/focus/尺寸 | 近底分页门、箭头同目标不重复动画、Anchor代次；不凭LayoutUpdated报卡顿 | 纯布局算术/Page控制流；未运行滚轮帧时或真实文字缩放 |

R03权限、R06–R15业务与关闭问题沿原号。R17接提取器/入口/计数与运行可信度，R18接旧注释/README/资产来源；没有启动后续分区。
