# 第十二批必要修复台账

96项必要问题修复已从 `bb28b67` 正常快进合入 master；匿名设置验收补件 `cace457` 已挑入为 `672d8b3`。当前处于主树最终验收，尚未更新桌面交付。阶段证据与最终交付分开记录。

## 数量与唯一处置

本批仍成立的问题共96项，按实际责任归组：foundation25、media21、shell24、root26。133是历史累计发现数；94是初期两项由视觉替代时的待修数。用户撤回另一视觉批后R07-009及R16-001重新纳回，最终96；不再把原视觉批验收当本批修复验收。

| 组 | 唯一ID | 根因与行为 | 合流/来源 |
| --- | --- | --- | --- |
| foundation25 | R01-005；R02-005/006；R03-002/004/006；R06-001至011；R10-001至007；R13-003 | 包身份ADS解除、旧持久化/迁移夹具；权限与主页恢复/退出；列表原始消费/缓存/删除/轮播/字段；队列跨剧父对象/跨版游标、显式起点/失败回滚、播放上报顺序；外挂字幕按实际新增轨选择 | 基础来源93611e0,a32bb17,43f45ea,0ea2845,f7d5e44,6fe4aae,c77f8c5,bacbadb,c1c0654,786a232,c906e6b,473aa90,0bac816,278d9a8；基础整合2795818。播放e7bc307、4d185ef、3074e20。匿名UI补1e66570→78cdc3d |
| media21 | R07-002至009、R07-T01；R08-002至006；R09-003至006及008；R11-001/004 | 详情实际DTO/主体/祖先删除、字段状态共享、无源显式从头；识别关闭/迟到读、飞牛失败保稿、权限重核及超限图片错误；显式80/443、MFA、完整订阅身份、站点保稿/AniBT显隐；回执/季集/实际源和删除记忆；零模糊及实际场次暂停确认/统计滚动 | fa5c025、5a6f8d1、dbd766b→ce8b243；ad15a83/e50cdfc/2193ac7→44106ec。旧等价端口夹具f11cbd3→ce6e8cd；匿名UI补97c37c2→f4488aa |
| shell24 | R14-001至006；R15-001至004及006/007；R16-001至004；R17-004至008、012、014、015 | 已拒凭据不复活、关联失败回执、同片草稿/PanelCTS/来源刷新；WebView最新令牌、通知确认/关闭、原生保存正文/说明/HDR显隐；图片坏缓存/备用/拼图归属与零档材质；报告/清理失败退出、服务检查、逐采样点归属、Inspect失败、实际窗口存档、临时页退订、诊断当前标题、指针前提 | 24来源提交最终d203dcd；primary整合a06b82a。无窗口阶段回执remaining-shell-tools/artifacts/shell-tools-handoff/receipt.json |
| root26 | R11-002/003；R12-001至005；R13-001/002/004至008；R15-005/008；R18-001至010 | 同窗输入及UTF8固定长度配置；HDR适用性、音量绝对重置/读回归属、截图事务；字幕来源/命令/显隐/延迟、翻译载荷/队列和夹具；字体在输状态及上游按钮显隐；隐私、共享目录、地址遮蔽、退役凭据/搜索/服务/播放/结构/技能链接文档 | 101e404,07a8102,395ead3,2584fa2,84d682c,f971f5a,e9945d1,6a70531等，整合e6e1de8与2fcb56e；R18来源74d990e及后续遮蔽/登记3be506d,d33d762；根组26逐项来源已核为master祖先，完整SHA回执保留final-combined-readonly/receipt.json |

96计数只涵盖原必要问题。联合时序补修、末端6个夹具兼容提交及随后2个测试项目接口补齐、4+2+2匿名GUI验收补件不重复新增发现数。

## 本批独立阶段证据（有重叠，不累加唯一覆盖）

- foundation：独树69自然caller、24reports、41外挂字幕、381既有playback、232Core权限显示、182存储、33导航、70退出、57轮播折叠；新GUIprobe只无窗口读数45/45（有效旧红11）与SettingsContracts33/33，真实UI由父线程跑。基础17旧交接PROGRESS与artifacts/review-batch12属于阶段证据，不能替代最终25合流。
- media：`docs/superpowers/reviews/2026-11-05-full-code-review/MEDIA-TWELFTH-REPAIRS.md`；独树`artifacts/media-recovery`及`artifacts/player-input-recovery`。MediaRecovery66、Site14、AniConfirm58、订阅VM75、Missing59、MediaDialog8、真实DetailVM最终38、FnNas32、Library39、Delete21、ItemState73、PlayerInput18全部定向通过；stats真实随包LuaJIT7/7（假OSD/时钟，无窗口媒体）。原红和过程失败全部保留。
- media UI补：独树`artifacts/media-ui-recovery`，Shell当前0警告错误、完整source-linked MediaDialog12/12；正常与pending截图失败各流程实际执行，format/9文本通过；独立shell-tools只读复核zero。真实GUI未在子代理启动。
- shell：d203dcd收据Core/Shell同源hash，Pointer15、Dashboard70、Danmaku60、Page152、Appearance59、Finalization95、Rows44、Boundary23、Applicability32、Uosc51。没有真实WebView/剪贴板/指针/凭据/播放，不能外推。
- root：primary`artifacts/review-batch12-playback`中current-core35/35、translation119/119、font2/2、dialog48/48、权限232/232、web-skin匿名DOM26/26；其余具体最终专项从父线程当前日志补。Web皮肤DOM不是真WebView，图片/设置VM不是真像素。
- 末端夹具漂移：source `fixture-drift-final` dd84931..d2bb240六提交25测试文件，primary分别4d738fc/af29582/eaf4e86/e98ffcf/62c265f/bb28b67。`artifacts/fixture-drift-final/receipt.json`记R04842、Subtitle50、Search43、Perf73、ServerSettings实际6metadata、Natural69及10严格反例；19pins/83members。19当前哈希已只读独立匹配，仅4pins对应已读3074e20..dd84931变化，15未改。权限/Cancel/EOF原样提取，旧断言未删。独立media只读复核zero；没有代跑。

## 联合验收待填与原红

- 当前primary快照bb28b67已含修复及补件，但master合流已确认，尚未完成全方案最终验收、全部适用专项、全部适用GUI、发布目录和桌面shortcut；独立树绿不代表主树交付。
- primary已有`artifacts/review-batch12-playback/full-current-tests.log`：4181项4180过1败，MoviePilot身份旧夹具把显式80当省略port。ce6e8cd仅将等价输入改显式3001，全部JWT断言保留；media已重编主Tests后只调用实际RegisterMoviePilot7/7。原full红保留，父线程必须跑最终full，不以7绿冲销整体红。
- 用户撤回界面合流62b2201/dd84931，保留96缺陷修复；草稿和当前开发文档头部可能仍有历史视觉说明，最终不得声称季度列表/全局材质仍交付。
- 不改/接受整份基线洗绿。缺运行路径必须明示，不用测试数量推导唯一覆盖。

## 精确主屏串行匿名命令与回执

当前源码自动分区18个：shell,settings,shortcuts,shaders,svp,library,search,moviepilot,anibt,accounts,covers,favorites,playback,metadata,performance,materials,plex-state,jellyfin-latest。
`search-preview`只独立手动试用，不属于自动全量；`inspect`不能与probe-section组合。无分区表示全自动，源码逐await串行并持桌面诊断mutex。

父线程当前Release/Shell确认后，单独执行：

```powershell
$probe = Start-Process -FilePath 'C:\Users\89400\.codex\worktrees\review-recovery-batch3\Momoka\src\Momoka.Shell\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\Momoka.exe' -ArgumentList @('--probe-shell','--screen','1') -WorkingDirectory 'C:\Users\89400\.codex\worktrees\review-recovery-batch3\Momoka' -WindowStyle Hidden -PassThru -Wait
if ($probe.ExitCode -ne 0) { throw ('Shell probe failed: ' + $probe.ExitCode) }
```

Hidden仅隐藏启动辅助窗口，探针真实HostWindow仍Show。每次独立目录为 `%LOCALAPPDATA%/Momoka/logs/shell-probe-<UTC>-<PID>/logs`。不加self-check/play/menu/osd，不指定伪data-dir；路径由Program隔离，无生产设置复制。完整读`shell-probe.json`与`shell-probe-scope.txt`，区分全部/未执行、每个Passed、正常退出与必需图片落地；inspect退出0不是案例通过。

若只媒体阶段，`--probe-section metadata,favorites,library,moviepilot,anibt,settings,shortcuts,performance,covers,playback`是可复用并集；所有分区仍执行该分区全部具名case，不能称本组全部新触发都覆盖。统一96优先全自动一次。

## 媒体21实际UI覆盖与必须截图

- metadata新增2具名case：Identify真实搜索→选择→Apply及Remove，各失败保稿/在飞Hide拒关/成功确认/迟到搜索；FnNasMetadata主按钮、Close/Hide、失败原JSON/输入和重试。必须检查6张真实实屏：`identify-apply-failed-screen.png`、`identify-apply-pending-screen.png`、`identify-remove-failed-screen.png`、`identify-remove-pending-screen.png`、`fnnas-metadata-failed-screen.png`、`fnnas-metadata-pending-screen.png`。正文/错误和被保护按钮完整可见，不能靠截图存在即验收。
- favorites复用DeletePreservesList/DeletePagingRace/DeleteContainer/DeleteDetailCard；更新后主体删除要求CurrentItem/PlayTarget/TitleLink null、EpisodeShelf0及明确通知。实屏`delete-after-screen.png`；`delete-before.png`/`delete-after.png`是RenderTarget回归图，不冒实屏。
- library复用DetailWrite、DetailPicker、真实高斯`CheckGaussianBackdropAsync`的100→45→0→45，0强度像素纹理恢复。必须查看`detail-customization-{Series,Episode,Movie}-backdrop-clear.png`（均SaveProbeScreen）及`detail-gaussian-<Type>-<当前存活theme>-screen.png`/沉浸实屏，配`detail-customization-pages.txt`；宽窄主题操作与材质由此批已有覆盖复用。
- moviepilot复用SearchSites、SubscriptionIdentity/FilesRecovery/Review、MediaManagement/MediaCards、DeleteRefresh。实屏`search-sites-picker-screen.png`；`mp-media-delete-confirm.png`/`mp-media-delete-receipt.png`、`mp-smart-series-confirm.png`/receipt、`mp-media-cards-result-{success,partial,unknown}-screen.png`；`mp-delete-refresh-{all,destination}-{before,after,reloaded}.png`都是实屏。`mp-subscription-files-recovery-{600,760}.png`是SaveSettledFrame渲染图，只证明布局与绑定，不叫实屏；其它旧主题宽窄图按当前分区实际生成查看。
- anibt复用Search/EpisodeSearch/Scrape与Download confirm；实屏`anibt-download-confirm.png`、`anibt-scrape-candidates.png`、`anibt-scrape-selection-restored.png`及宽窄/主题已有图。R09完整源/每文件范围主要在真实VM无窗口反例确认，不能只用旧确认框绿说明新范围都被实际点击。
- settings复用SettingsMoviePilotAniBt、ReviewSettingsAvailability、父线程foundation新增显隐；R08关闭AniBT同页入口恢复与偏好复用。实际各宽窄恢复图以最终case输出核对。
- performance复用真实DetailShape通知去重/绑定（`performance-detail-shape-{preview,corrected}-<1280/760>-screen.png`具体scenario以当前方法为准）、MatchClose迟到读资源；covers复用写关闭/迟到选择/完整预览；playback/shortcuts复用假handle布局/菜单事件及队列。

R08显式80/443、MFA分类、完整订阅身份/站点草稿、R07权限重核与64MiB读取前拒绝等仍以当前Core/VM/实际命令无窗口专项覆盖，不启动真实账号/文件picker制造匿名截图。
R11暂停/恢复与stats Lua：probe-shell假handle/键盘入口不覆盖真实拖边命令、原生pause顺序或实际集成stats尾部滚动；PlayerInput18和Lua7是无窗口证据。需要在当前交付授权内另用隔离本地motion/实际stats运行或明确缺口，不能拿普通GUI分区绿替代。不得真实服务器播放、凭据或写入。

## 最终父线程必须补的字段

最终产品/工具/文档commit、master SHA；每个最终构建/测试suite实际数与skip/失败；GUI PID/运行目录/18分区总数/图片阅验；必要本地native probes及不可覆盖边界；publish/载荷验证/desktop shortcut路径与hash；原红保存位置和代价；未合/未验/未交付明确列出。禁止汇总独立suite计数为唯一测试覆盖，禁止把阶段文档默认“本轮未修”当current。

## 2026-11-07 最终合流更新

- master a0c7a45 已包含全部96项及两组匿名GUI补件；3258ffc仅补Danmaku/Subscription旧测试依赖，原生产方法与旧断言不删，原CS1739及5个CS0103保留。独树73/73和原3身份+1当前/旧/离页通知归属通过，主树统一最终专项待执行。
- 133-96=37唯一ID逐项已与前十一批及基点既有修复核对，无重复、无不在FINDINGS的编号。R10-008由b61fd63/d2f1232、FINAL-T02由8f7063a此前合入，不计本批；R17-010亦为基点既有处理，保留当前fixture执行证据与历史覆盖差异，不宣称本批新修。
- 新settings cace457→672d8b3、通知/诊断270bde9→a0c7a45只增强运行验收；子代理互审无必要遗漏，Native GUI由统一会话串行执行。
