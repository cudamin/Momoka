# 第八批必要修复：Jellyfin 最新剧集不分组

基点 de16a66；仅处理 R04-003。用户已授权自主选择必要修复并连续完成。

## 修复与覆盖

EmbyClient.GetLatestAsync 原来将 groupItems=false 编为 null，EmbyUrl 省略该键；两版 Jellyfin 控制器默认 true，同剧多集可能返回父 Series。现在 false 明确发送 false，true 保持 true。货架、路由政策、过滤与集合分组参数不变，不新增依赖。

固定上游：10.10.7/aefb9b2cffca3ccb7a95d89af0d9fb2040de6ed3 的 UserLibraryController:534/619、UserViewManager:210、Episode:60；12.4/83d0d0e7f76e23e2e085c8ff67d28b772d35967d 的 UserLibraryController:540/563/580、UserViewManager:237、Episode:62。默认 true、false 逐项返回、Episode 容器为 Series 已读取。12.4 仍有标 obsolete/IgnoreApi 的旧用户路由，不声称物理删除。

JellyfinProtocolTests 注册实际 Core HTTP 回归：两版同剧/不同剧 false、默认/显式 true、电影 false、ParentId/Limit/Fields/类型/IsPlayed/用户域；共享 Emby 路径补开关回归。正式 Release LibraryViewModel 的推荐页夹具验证同剧两张 Episode 卡及来源，不以手工 URL 拼接代替消费者。

匿名真实 LibraryPage 探针沿用现有框架，独立 jellyfin-latest 区域验证正式导航/建议标签、两版货架与1320/760宽窄。只允许内存 `.invalid` GET、假身份、隔离设置；页面释放与探针结束走既有生命周期。分组模型是固定源码的边界替身，不是官方服务器运行。

## 当前证据

- 开发树当前 Tests/Core 编译零警告错误。修复前协议111项中104过7失败，全部遗漏false；正式VM两版均把episode-1/episode-2折为series-a，0/2通过。修复后协议111/111。
- 带新探针的旧Core实际页面 PID2468 同样失败「最新剧集把同剧两集折成了父剧卡」，前件正常，应用正常退出；原日志保留在 artifacts/review-batch8/gui-red。旧程序完整目录 gui-red-build 保留。
- 开发完整4134/4134、VM2/2、协议111/111；最终匿名页面PID2508两版/两宽四回执四截图通过。首轮绿色运行PID14624旧版两宽已过，但新版UserViews假回答缺失导致恢复失败，已补本地前件；原日志和两图保留，未当作产品红。一次独立复审零必要发现。主树整合/交付仍在进行。

## 范围与决策

不重开 R04-001/002/004、R05-001～005、第六批R17项；不复审全库。此次仅协议修复，无UI设计改变，匿名页面用于验收。已授权主树整合及日常桌面交付，不推送、上传或生成新ZIP/MSIX。真实服务器、凭据、播放及观看记录不在本批范围。

其它聊天仍有在途UI工作，主树新增选色/显示模式75c9a2b将保留。此前PID24320终止授权不用于新进程；当前诊断退出后才串行运行本批。

## 主树最终验证与交付

产品0b5e234，经9fc4f6e保留主树50489f5外观/两模式代码，392ab80保留7b696a3文档收尾；主树快进至392ab80，只有PROGRESS冲突且双方记录都保留。最终主树Tests/Core/Shell Release/x64零警告错误，完整4135/4135、协议111/111、正式推荐VM2/2零失败跳过；专项与完整套件有重叠不累加。两版真实页面PID2520四回执四实屏通过，实际发布程序PID13140再获四回执四图。发布重生Shell字节后重新跑正式VM2/2和发布GUI。

主树实际交付artifacts/publish/win-x64/Momoka.exe，0.2.5.0、546文件300344711字节、286.4MB，14GLSL/4HOOK、mpv清单及许可校验通过；快捷方式目标/工作目录正确，参数空。已测/发布Core SHA256 0B8480303A00E09AC34558686661D9BFAC231D25568AEC698682EA2F5F20BA5D；最终Shell 622682F641059AAEBEF8B042150B54FD11093202307E98427832C4C09FEC1EB7。

14变更文本/定向空白/76相对文档链接通过。format首次报匿名回执排版，已定点修正；最终exit0但加载工作区提示警告，日志保留，不冒全方案零诊断。没有修改基线。证据artifacts/review-batch8-master（full-tests/protocol/shelf/gui-master/gui-published/publish/delivery-receipt/tested-source，35份开发证据逐SHA保存）。仅jellyfin-latest自动区域执行，其余176检查未执行，不称全探针通过。

没有真实Jellyfin登录、入库、服务器写入或播放验证，无新依赖、ZIP/MSIX、push/上传。本批无待合/待交付产品；本会话释放主树写入与发布。复用受保护primary树保持可恢复证据，没有新临时分支需删；纯文档收尾不重建或重发。
