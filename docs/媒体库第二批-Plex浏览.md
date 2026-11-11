# Plex 媒体库筛选、排序与字母跳转

第二批沿用正式媒体库页面。Plex 的选择单独存为 `PlexFilterSelection`，按来源账号与媒体库记忆，不借 Emby `ItemFilters`。本批不升级版本，不写入 PMS，不包含云人物服务或嵌套 AND/OR 编辑器。

## 实现

- 读取 `/library/sections` 的当前 section 与原始 `filters` 声明，再读取 `/library/sections/{id}?includeDetails=1&includeAdvanced=1` 的 `Type/Filter/Sort` 目录。只展示本批支持且当前目录实际声明的媒体类型、题材、年份、已看状态和分辨率。合法媒体类型前缀与原始值保留。
- 选项地址只用于核对服务端目录。请求由固定 section、类型、字段构造，不跟随任意服务器 URL。查询键和值分别转义；显示名称和原生值分开。不同字段并列组合，同字段多个值使用 Plex 的逗号语义。
- 排序使用目录里的 `key` 和 `descKey`，保持服务器默认方向。目录没有排序时收起按钮；现有不带目录的普通浏览仍保留原有限排序路径。
- 字母索引读取同 section、媒体类型和筛选条件的 `/firstCharacter`。按 `Directory` 顺序累加正整数 `size` 得到绝对索引，再调用现有窗口分页；没有桶就没有跳转结果。此处是 Momoka 的局部实现，不宣称复刻官方网页的全部字母栏交互。
- 保存条件不再有效时保留条件并反馈，可重试或清除；不默默扩大查询。切条件、重入、离页和账号失效均守卫异步返回；刷新目录使用每 section 代次，旧请求不能覆盖新结果。快照键包含 Plex 选择；单集和随机顺序不使用稳定窗口分页。
- 筛选面板使用正式 `ComboBox`、稳定集合和命名控件。失效区域收起，进度、失败与恢复操作仍可达。实际像素、宽窄窗口和真实控件操作由批次主交付会话另验。

## 固定上游源码

本轮实际读取官方 Plex Web **4.163.0 / 8246fd2** 的发布 JavaScript；不是最新版结论。原始源为 `https://app.plex.tv/desktop/js/main-8792-0fadf59092c873ab638b-plex-4.163.0-8246fd2.js`，文件 **3,508,034 字节**，SHA256 **bdb3e4ce5d49b5eb6be48e11a4aea0f33a357561f8bf1e1303d31255f701cd97**。本轮核对本机存档哈希一致，未把二手审查结论当作代码证据。下面位置为原始 JS 从零开始的字符索引，不是 UTF-8 字节偏移。

| 模块与位置 | 实际行为 |
| --- | --- |
| 18374，字符166672起 | `LibraryDetailsModel` 请求原 key 加 `includeDetails=1`，可加 `includeAdvanced=1`；读取 Type 与 FieldType。 |
| 20181，字符168472起 | Type 的 `key/type/subtype/title/context`；读取 Filter、Sort、Field。 |
| 54142，字符169510起；177310附近 | SectionMetadataListModel 取类型、筛选与排序参数；section 原始 `filters` 被转换成 `allowAdvancedFilters`。 |
| 55459，字符164380起；56009，字符178511起 | 旧模型分别读取 section `/filters?includeAdvanced=1&type=...` 与 `/sorts?type=...`。本批采用 details Type 目录，不为缺失目录伪造回退能力。 |
| 34792，字符178851起 | Sort 的 key、descKey、defaultDirection/default、firstCharacterKey；选方向取真实 key/descKey。 |
| 29853，字符165302起；61100 内 If，字符3164514起；Of，字符3166155起 | 选项读取 Directory；field/value、fastKey/type/key 与普通 key 三种形状；读取筛选目录提供的 key。 |
| 54142，字符172079附近；61100 内 Rv，字符3178430附近 | FirstCharacterModel 读取 key/title/size；集合请求 section `/firstCharacter`，带同类型与当前条件。 |
| 14056，字符1912270起 | 官方的复合规则编码 and/or、push/pop；本批不引入这个编辑器。 |

## 离线验证

在本树先 restore，使用用户级 .NET 10 SDK，编译 Release/x64 Core、Tests、Shell 后运行：

```powershell
& "$HOME/.dotnet/dotnet.exe" tests/Momoka.Tests/bin/x64/Release/net10.0/Momoka.Tests.dll plex-protocol-fixture
& "$HOME/.dotnet/dotnet.exe" tests/Momoka.Tests/bin/x64/Release/net10.0/Momoka.Tests.dll plex-library-vm-fixture '<本树当前x64 Release Momoka.dll>' '<本树绝对根>'
node tools/scan-automation-ids.js
```

协议回归包含库/类型隔离、目录缺失/失败/刷新竞争、合法选项与编码、多字段组合、失效值拒绝、排序方向、字母计数和保存条件归一化。实际 VM 夹具加载当前 Shell 程序集，使用正式 LibraryViewModel 与单线程调度、匿名 HTTP，验证显隐、选择写入、查询/快照身份、字母落点、失败恢复、失效选择和迟到响应。不创建 WinUI 窗口、不读取凭据、不写设置、不播放。

没有真实 PMS、真实账号、服务器返回能力或 WinUI 实屏结果。本树通过只能称为离线协议和实际 VM 回归；主树整合、原生 UI 检查与桌面发布由主交付会话完成。
