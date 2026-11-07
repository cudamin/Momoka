# 第四批 Plex 元数据修复

用户要求有下一批就继续，自行选择必要修复，不按任务暂停。本批仅重核并修复 R05-004、R05-005，基点 c253b00；原历史累计数量不能当作当前剩余问题数。

| 问题 | 产品提交 | 修复与已执行专项 |
| --- | --- | --- |
| R05-004 | 6e37ffd | Media 与 Stream 的 kbps 转共享 bit/s，宽类型乘法检查整数边界；沿用原整数解析与零/负值显示抑制。旧代码新专项167项原11败，修后167/167。 |
| R05-005 | 295c207 | 剧/季按有效整集统计判已看完，填未看集数；空或未知、负数、小数、矛盾和超范围统计保守处理，电影/单集保持viewCount。旧代码新专项207项原16败，修后207/207。 |

本批新增59项纳入既有 Plex 和完整主测试注册；专项与完整测试重叠，不累加。码率检查实际 ItemDetail 输出和缺轨道码率回退，观看检查实际 ItemMenu 反向命令与 GetItemAsync/SetWatchedAsync 的匿名内存 HTTP 回读。当前开发树 Tests/Core Release/x64 全部零警告零错误。

## 来源证据

固定官方 Plex Web 4.163.0 / 8246fd2：`https://app.plex.tv/desktop/js/main-8792-0fadf59092c873ab638b-plex-4.163.0-8246fd2.js`。

SHA256：`BDB3E4CE5D49B5EB6BE48E11A4AEA0F33A357561F8BF1E1303D31255F701CD97`，与原 R05 清单及本次证据副本完全一致。观看审查者重新取回原件；码率审查读取固定本地原件。没有把固定版本称为最新版本。

实际可执行源码链：convertMediaItems → parseInteger → 媒体信息追加kbps（UTF-16偏移约201079/525626/772841）；scrobble成功回调按容器更新viewedLeafCount（约279820）、show/season容器类型表（约561000）、unviewedLeafCount差值（约198851）。这是静态协议证据，不是真实服务器响应。

## 最终主树验证与交付

复用托管 review-recovery-batch3 工作树，最初快进到最新已交付主树后开发。当前原红/绿和固定官方源码保存于 artifacts/review-batch4。第一次定向空白检查指出两个新测试文件的对象初始化换行格式，已仅调整空白，原退出2日志保留。

一次独立复审在本轮范围内未发现必要缺陷。主树快进整合到4a6f1e4后，Tests/Core/Shell Release/x64零警告零错误；完整主测试4059/4059、Plex207/207，零失败零跳过。10个变更文件文本检查、定向Core与Tests空白检查通过。没有修改基线或弱化守卫。

最终发布到 `C:\Users\89400\Momoka\artifacts\publish\win-x64\Momoka.exe`，版本0.2.5.0，546文件、300301695字节，载荷/着色器/许可校验通过。发布前确认旧进程已退出，不需终止用户程序。桌面Momoka.lnk的目标及工作目录均为该目录，参数为空；没有生成新ZIP/MSIX或上传。

测试提前使用发布相同的DebugSymbols=false、DebugType=None。测试Core、当前构建Core与实际发布Core的SHA256均为 `F6EAA1CBE933A57F2D91FAAC8F3B50B9022AE394904C8BD4A2C4624107DF2C74`；当前构建/实际发布Shell均为 `14A583773E20D098D661F27649E55CCC2D25001C17289DF844762B501420B7E5`。沿用逐字节一致的最终测试结果，没有复制DLL或发布后冒用旧测试。

主树证据：`artifacts/review-batch4-master`。delivery-receipt.json记录文件/版本/哈希/快捷方式，full-tests.log、plex-tests.log、build-*.log、publish.log记录实际运行；preserved中的17份开发树原红/绿、格式失败及固定官方源码逐文件核SHA256一致。两个产品提交已进入master，没有待合或待交付代码；主树写入占用已释放。复用树属于本聊天primary工作树，沿用已记录的原生保护，保留而不手工删除。

未读真实凭据、登录/播放/写服务器、改 UI 布局、新增依赖、改版本或上传。实际 WinUI 展示与真实 Plex 服务仍未覆盖；静态官方来源与匿名HTTP测试不能代替实服验证。最终记录提交仅改文档，不重新构建或发布程序。
