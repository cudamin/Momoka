# 共享入口成员联审清单

基点 `6ba7a7f99e2b3840f68d63ff1c705b44659e7697`。以下是实际声明归属盘点，不代表相应业务已审完。原始成员表保留基点；当前R01–R04状态见增量和报告，后续主责分区未完成；具体文件逐行记录在 [文件台账](file-ledger.jsonl)。行号只指当前基点。

| 文件 | 成员与主责 | 联审 |
| --- | --- | --- |
| `src/Momoka.Core/Configuration/AppSettings.cs` | R02：AppSettings 的 schema/device/server/account/Remember/EnsureDeviceId、ServerProfile、AccountProfile；各设置对象的持久化与迁移 | R03 身份、R04/R05 协议；音视频等字段语义按下列成员分工 |
| 同上 | R01：UiSettings.AutoCheckUpdates:1454；R06：RecentSearches/PageSize/LastLibraryId/Sort/Filters/Views/Display/ShowHomeBanner/HomeLibraryPlacement/CarouselSource/CarouselMedia/CarouselCount/CarouselSeconds/HomeRows/HomeRowsIdentity/HomeRowsByIdentity；R07：SeriesCards/EpisodeCards/MovieCards | R02、R15；首页与卡片联 R16 |
| 同上 | R11：MpvSettings.Backend/Pipeline/CustomConfigEnabled/CustomConfigPath/ExecutablePath/EnableIpc、所有 Screenshot 字段、ShortcutSettings.Bindings、PlaybackSettings.AutoFullscreenOnPlayback/ResumeAfterWindowResize/PinWindowTopmost | R02、R10、R12、R15 |
| 同上 | R10：PlaybackSettings.ReportProgressToServer/ResumeFromSavedPosition/MarkWatchedPercent/DonghuaMarkWatchedPercent/StopReportEnabled/ProgressReportIntervalSeconds、Seek*、ResumeRewindSeconds、SkipSections、AutoPlayNextEpisode、AudioLanguages/AudioFormatRules/VideoFileRules/MediaSourceOrder | R02、R04/R05、R06、R11/R12/R15 |
| 同上 | R13：PlaybackSettings.SubtitleLanguages/SubtitleTitleRules/SubtitleMode/SubtitleFallbackToDefault、SubtitleFontFamily 至 StretchWideImageSubtitles、字体大小/比例常量与 ClampFontSize | R02、R10、R12、R15 |
| 同上 | R12：VideoSettings 全部、AudioSettings 全部、ShaderAutomationSettings 全部、MpvSettings.SvpEnabled/SvpVapourSynthPath | R02、R10、R11、R15；HdrSubtitleNits/HdrImageSubtitleNits 联 R13 |
| 同上 | R08：MoviePilotSettings 的连接/搜索站点与 MoviePilotSearchSitePreference；R09：AniBtChanged/AniBtEnabled；R16：UiSettings.Theme/AnimationsEnabled/ControlBackgroundOpacity/BackdropOpacity/UseAcrylicControls/UseGaussianBackdrop/AlwaysImmersive/ImageCacheMegabytes | R02、R15；R08/R09 双向联审 |
| 同上 | R03：UiSettings.ServerSortBy/ServerSortDescending/SidebarMode/WindowLeft/WindowTop/WindowWidth/WindowHeight/WindowMaximized；R15：CompactMode | R02、R11、R15/R16 |
| `src/Momoka.Core/Emby/EmbyClient.cs` | R04：构造、Connection/ApiBase/Context；R05：_fnNasAdapter/FnNasMedia/_plexAdapter/PlexMedia/FnNas/FnNasConnection/Plex/PlexConnection，以及全部成员内 FnNas/Plex 分支 | R03；业务语义仍由下列分区负责 |
| 同上 | R06：GetViewsAsync/GetItemsAsync/GetFnNasFilterOptionsAsync/GetFilterValuesAsync、QueryResumeAsync/GetResumeAsync/GetLatestAsync/GetRandomAsync/WithCarouselArtworkAsync、QueryNextUpAsync/GetNextUpAsync/GetMovieRecommendationsAsync/GetSeasonsAsync/GetEpisodesAsync/GetCollectionsAsync/GetSimilarAsync | R04/R05、R07/R10；GetEpisodesAsync 联 R09 |
| 同上 | R07：GetItemAsync/ApplyItemFileSize、MarkPlayedAsync/MarkUnplayedAsync/SetFavoriteAsync/GetFnNasUserDataAsync/HideFromResumeAsync；GetItemJsonAsync/UpdateItemAsync/CreateCollectionAsync/AddToCollectionAsync/RefreshItemAsync/ValidateItemRefreshAsync/ScanLibraryAsync/GetExternalIdInfosAsync/SearchRemoteAsync/ApplyRemoteSearchResultAsync/GetRemoteSearchImageAsync/ResetMetadataAsync/DeleteItemAsync | R04/R05、R06、R09、R10 |
| 同上 | R07：GetRemoteImagesAsync/ApplyRemoteImageAsync/GetRemoteImageBytesAsync/DeleteImageAsync/UploadImageAsync/ImagePath/DownloadToFileAsync；R16：GetImageBytesAsync；R11：GetChapterImageBytesAsync | R04/R05、R06、R16 |
| 同上 | R09：GetMissingEpisodesAsync；R10：ReadSkipMarkersAsync/ReadVersionFilesAsync、ReportPlaybackStartAsync/ReportPlaybackProgressAsync/ReportPlaybackStoppedAsync/StreamUrl；R13：SubtitleUrl、SearchSubtitlesAsync 两重载、SubtitleItemId/DownloadSubtitleAsync/DeleteSubtitleAsync | R04/R05、R06、R07、R11 |
| 同上 | R15：GetNotificationServicesAsync/GetNotificationTypesAsync/GetNotificationsAsync/GetNotificationDefaultsAsync/SaveNotificationAsync/DeleteNotificationAsync/TestNotificationAsync/GetNotificationUsersAsync/GetNotificationLibrariesAsync/GetNotificationDevicesAsync/GetSystemInfoAsync | R03/R04/R05 |
| `src/Momoka.Core/Emby/EmbyServerGateway.cs` | R04：构造、Anonymous/GetPublicSystemInfoAsync、DetectAsync 两重载/DetectEmbyFamilyAsync、GetPublicUsersAsync/AuthenticateAsync/AuthenticateByNameRequest；R05：PlexCall 与所有适配分支 | R03 身份创建、R02 凭据、R15 公开信息 |
| `src/Momoka.Shell/Composition/ShellServices.cs` | 唯一方法 Build:44 分注册项：R03 容器/BuildServiceProvider/DeviceIdentity/EmbySession/IServerCapabilities/IUiDispatcher/IClipboard/ISystemLauncher/ShellPage/IShellActions/PlayerViewModel；R02 paths/DPAPI/SettingsStore/AppSettings/CredentialVault/ISettingsService；R01 UpdateClient/IStartupRegistration | R17 selfCheck 条件与设置隔离；各实际服务生命周期按所属区联审 |
| 同上 | Build 内 R06 BrowseSnapshotStore；R07 RemovedMedia；R08 MoviePilotClient/MoviePilotProbe/MoviePilotCredentials/MoviePilotService；R09 AniBtClient/AniBtService；R10 PlaybackPlanner/PlaybackService；R11 PlaybackBackendFactory；R12 ShaderGroupResolver/ShaderStaging/AudioDeviceCatalogue；R13 SubtitleTranslationCredentials/Client/Controller；R14 DandanPlayBuiltInCredentialSource/Client；R16 DetailHeroCache/EmbyImageStore/FontLibrary | R02 保存闭包、R03 生命周期、R17 allowPlayback/selfCheck |
| `src/Momoka.Shell/ViewModels/SettingsViewModel.cs` | R15 主责 SettingSection、分类/目录/选择字段与方法、Attach:658/RefreshServerAdministrationAsync:717/Activate:807/Cancel:814/Dispose:822、ReloadAsync/AddCards/ShowCategory、保存状态与通用行工厂 Pick/Options/Choice/Mpv/ColorRow/Toggle/Number/Slider/UnitSlider/ParseUnit/Annotate/Font/Text/Fact/PathBox/List/LanguagePriority/KeywordRules/Split | R03 活动期、R02 保存、R04/R05 管理能力、R16 控件；不因消费方多重复审查工厂 |
| 同上 | R01 _updates/_updateRow/_checkingUpdates/CheckForUpdatesAsync:2490/OpenReleasePage:2534、StartupRow:2257；R11 PlayerCard/RefreshPlayerBackend/CustomMpvConfigCard/RefreshCustomMpvConfig/ScreenshotCard/RefreshScreenshotCard/PushScreenshotOptions/PushScreenshotOptionsAsync/ScreenshotDirectory/AnnounceScreenshotDirectory | R15 真实界面、R02/R03 自启、R10/R12 播放 |
| 同上 | R10 PlaybackSections/ResourceFilterCard；R12 RefreshAudioDevicesAsync/FillAudioDevicesAsync/DeviceChoices/AudioSections/DeviceRow；R13 FontsReady/MeasureFontPicker/FillFontsAsync/SubtitleSections/BackColorNote/Live/PushSubtitleStyleAsync | R02、R11、R15、R16；视频/翻译/弹幕 partial 另有逐文件归属 |
| 同上 | R06 HomeSections/HomeRows/HomeRowsNote/IsPinnedHomeRow/CurrentHomeRows/RefreshHomeRows/MeasureHomeRows；R08 MoviePilotCard/SyncMoviePilotSites/RefreshMoviePilotSites/TestMoviePilotAsync；R16 ImageBudget/ImageCacheSummary/MeasureImageBudget/MeasureImageCacheAsync/UseHeroCacheClear/ClearImageCacheAsync/DescribeImageCache | R15；R08 联 R09 |
| 同上 | R02 RestoreDefaultsAsync/BackupAsync/RestoreFromFileAsync 及其对话文案；R15 ReapplyAndReloadAsync:2897/ReapplyPlaybackPreferencesAsync:2914；R17 ProbeSubtitleRestoreAsync/ProbeSubtitlePushFailureAsync | 恢复后联 R01 自启、R11 截图、R13 字幕、R14 弹幕、R16 主题；R15 InterfaceCard/AboutSections/OpenDirectoryAsync 按实际调用联审 |
| `src/Momoka.Core/Mpv/MpvOutputOptions.cs` | R12 视频/音频/HDR/补帧 choices、Build:453 视频467–541/音频543–583、AddQualityPreset/AddDither/AddDeband/ResolveSync/Describe/Add；R13 SubtitleUnitMinimum/Maximum/ClampSubtitleUnit、SubtitleCodepages/BackStyles/StyleScopes、SubtitleAppearance/SubtitleStyleOptions/AddSubtitleStyle/ToMpvColor、Build subtitles 参数和585行调用 | R10 起播/切片、R11 后端、R15 设置 |
| `src/Momoka.Shell/ViewModels/PlayerViewModel.cs` | R10 _playback/_session/_lifetime/_startIntent/_pendingPlayback/_playbackAttempt/_generation/_episodeLookup/_episodeSwitchTargetId、构造/Connect/Shutdown/ShutdownAsync/ShutdownPlaybackAsync/PreparePresentation/PrepareStopAsync、播放事件/当前条目/队列/版本/跳过与 Status/Settings/PlayingNow | R03、R06/R07、R11–R15；partial 的方法由 owning partial 审 |
| 同上 | R11 seek/scrub/speed/chapter/shortcut/window/pipeline 状态与属性；R12 shader/output/surface、Volume/VolumeLabel/VolumeMaximum/SoundGlyph/AudioDelay/MeasureSurface/MeasureRefreshHz；R13 Tracks/SubtitleDelay/_tracksRevision/_pushing；R16 CoverBackdrop/Frame/_coverGeneration；R17 ProbeHoldPlayback/ProbeForcePipeline/ProbeForceBackend | R02 持久化；ShutdownPlaybackAsync 必联 R12 音量、R14 任务清理、R11 窗口；此处 Subtitle 是标题副行，不能误归字幕轨道 |

R01 的更新请求关闭行为交 R15 实际窗口核验：静态可达旧请求完成后更新缓存行/调用确认，但用户主动、与账号无关的有限任务是否应取消尚无明确产品缺陷证据，不计入缺陷表。

## R02执行增量（2026-11-06）

R02已完成24项主责全文阅读；AppSettings按上表只确认持久化/迁移/身份字段，各业务语义联审仍待对应区。ShellServices的设置注册、SettingsViewModel的Save/SaveDeferred/Flush/Retry/SyncSaveState、RestoreDefaults/Backup/RestoreFromFile及共同尾部已读；MoviePilotCard新增联审发现R02-003，字幕Credentials→SecretRow新增R02-004。Program数据迁移、App/HostWindow正常关闭排空、SettingsWindow/SettingsPage隐藏与重开仅审关联成员。完整结果见[R02](R02.md)，不将这些成员阅读计作R03/R08/R13/R15整区完成。

## R03执行增量（2026-11-06）

32项主责全文已读，登录/恢复提交、Scope所有权、默认选择、同身份权限刷新、DI和UI队列、Frame与页面重入、主窗口退出链已联审。身份状态归属表及6项未修缺陷见[R03](R03.md)。AppSettings身份/排序/侧栏/窗口字段、SettingsViewModel管理来源消费者、HomeRequest与HomeVM目录消费已按成员记录；Player/翻译只检查关闭等待，媒体业务未审。

全部范围以file-ledger逐项状态为准；关联全文/成员阅读不把其主责分区提前勾选。已读Shell的MoviePilot/AniBt partial仅核生命周期调用，协议和业务仍交R08/R09，不声称上游同步完成。未把IsAvailable统一换成IsCurrent；原来源在线的已绑定操作仍可继续。

本轮主树接入bc3f62d，原始blob台账不重写；54项差异见[file-delta-20261106](file-delta-20261106.json)，后续活源码/测试按[current handoff](master-handoff-20261106.md)。R03全部主责未漂移；主页目录消费的当前行号由R03报告说明。R01/R02已确认发现不因这次外观回退而解决，历史日志不改写。

## R04执行增量（2026-11-06）

15项主责文件全文已读，包括共享EmbyClient全部协议入口；其他分区业务语义仍按上表分工。R04-001字幕文件目标由R13联审，R04-003最新剧集分组由R06引用，R04-004字幕204回执由R13引用；R03身份发现保留原编号。HTTP可选正文IO已采证但字幕调用者有不确定状态恢复，不直接报重复写；用户状态消费者交R07。实时寿命、重连、消息预算及HomeVM事件合并已读，真实Emby端点别名与完整跨版本服务端链仍有缺口，见[R04](R04.md)。

R04收尾另一会话已带入不透明度提交：HEAD e72ec27、产品与master5951bba一致；[新增19项差异](file-delta-20261106-r04.json)与R04影响已独立核对。原文件blob/hash、54项历史主树增量保持不变。R04主责及全文联审文件未变；DetailViewModel背景属性变化未触及字幕夹具抽取六段。R02两文件的schema27增量待复核，不把旧勾选当新迁移已审；外观交R07/R16，探针/测试交R17。R18接Jellyfin旧路由“全部移除”和开发文档实时api_key旧说明的纠偏，R17接测试覆盖与运行边界；本区不会自动修复这些文档。

## R05执行增量（2026-11-06）

飞牛15与Plex7主责文件全文完成，报告见[R05](R05.md)，完整逐文件范围及当前哈希见[R05验证](R05-verification.json)。共享EmbyClient/Gateway适配分支、源/权限、状态写读、分页与播放映射独立联审；全文联审不会自动完成其主责分区。

| 成员组 | 本轮处置 |
| --- | --- |
| FnNasMediaAdapter.GetItemAsync / Sources、PlaybackPlanner.Plan | R05-001：区分原始条目时长与默认版本展示时长，未知版本不可借用；R10接续 |
| PlexMediaAdapter.SetWatchedAsync、Session.ExecuteWithSourceAsync、ItemCommands/DetailVM已看 | R05-002：写确认和补读分离；R07接续 |
| PlexMediaAdapter.GetNextUpAsync、ResumeSlot/ResumeRow | R05-003：全服分页再过滤的后页漏项；R06接续 |
| PlexMediaAdapter.Models.Sources/AddStreams、ItemDetail.MediaInfo | R05-004：kbps转bits/s；R16/R10接续 |
| PlexMediaAdapter.Models.Item、ItemMenu/CardItem/DetailVM | R05-005：show/season的viewedLeafCount；R06/R07接续 |
| AppSettings.ServerProfile/AccountProfile | 仅协议来源字段联审，不包含schema27；R02历史增量仍待审 |

R03身份/权限发现沿原号，FnNas未知管理员与默认权限门控有现有实际VM覆盖。未取得飞牛原始官方前端，不把菜单权限候选定案；Plex单位和容器状态已核固定4.163.0官方JS，真实服务覆盖仍空缺。历史54/19项台账不改，当前R05无新增产品漂移。

## R06执行增量（2026-11-06）

82项存活主责全文完成，历史删除1项单列；Home/Library/Detail交接和四来源搜索例外见[R06](R06.md)。EmbyClient浏览16组成员、AppSettings首页/浏览字段、ShellServices快照注册、SettingsViewModel首页建卡、Shell导航、Detail来源与成功删除回调已成员联审，完整逐文件哈希见[R06验证](R06-verification.json)。主责/联审阅读互不冒充完成。

新增11项P2均未修：R06-001游标与裁剪不一致；002/003搜索查询代次和输入词；004收藏缓存来源；005 NextUp错误被当空；006表格空列；007推荐删除残卡；008单张转多张停钟；009轮播从属行；010固定库标题死入口；011标签未入指纹。007交R07、009交R15、008/010/011联R16；已有R03-004/R04-003/R05-003/005沿原号。R02 schema27待审和历史54/19项增量原样保留。

## R07 本轮成员联审（2026-11-06）

56项主责全文及当前哈希见[R07验证](R07-verification.json)，必要缺陷见[R07](R07.md)。本轮额外24项全文联审、33项成员联审，不把共享文件剩余范围标为主责完成。

| 共享入口 | 本轮明确范围 | 交接 |
| --- | --- | --- |
| EmbyClient / EmbyHttp | 条目读取、状态写、完整元数据JSON、合集/识别/重置/删除、图片与下载；下载落地/可选正文 | R07-001；下载版本及2xx正文确认语义保留缺口，联R04/R13 |
| AppSettings / ShellServices | 三种详情卡偏好与背景/按钮档；RemovedMedia注册 | schema27仍交R02；外观本轮读最新增量，真实画面交R16 |
| CardItem / DetailViewModel / ShellPage | 共享字段组、Apply/Toggle/Remove/Capture回调；播放转交与返回刷新 | R07-002/003/004；R05-002/005、R06-007沿原号 |
| ServerCapabilities / EmbySessionScope | 当前权限修订/在线身份/执行寿命 | R07-007是本地命令守卫缺口，不推断服务端越权 |
| DialogDismiss / Styles / Resources | 识别表标准关闭和点外Hide、无写中保护 | R07-005；原样方法四场景采证，不是UI输入 |
| FnNasUserStateWriteTests | RegisterDetails/PrepareDetail/State/匿名传输 | R07-T01正式入口29过3败，Apply后对照不洗绿 |

R10接缺源起点与删除旧播放目标；R16接ContinuePlay通知/InfoPanel布局有界缺口；R17接测试前提及历史计数。原成员清单和既有结论保留。

R07收尾增量：另一会话20项背景处理变更已登记[file-delta-20261106-r07.json](file-delta-20261106-r07.json)，其中7项本区主责复核完成，最终b8c1d61产品与masterac4e3fa一致。新增GaussianBackdrop/AppearanceOpacity/HeroPictureLoader全文联审，R07-009零强度模糊反例确认；schema27和真实UI/并行像素分支仍由所属分区接续。

## R08 本轮成员联审（2026-11-06）

89主责全文13,346行，连接/资源/订阅/视图四组已读；完整范围及当前hash见[R08验证](R08-verification.json)，上游版本/界面对照见[R08上游](R08-upstream.md)。

| 共享成员 | 结论与交接 |
| --- | --- |
| ShellServices MoviePilot单例、Service配置与资源Cookie | R08-001跨端口凭据；按传输/身份所有权修正，保留现有其他后端 |
| AppSettings MoviePilotSettings/站点偏好、SettingMoviePilotRow、SettingsVM MoviePilotCard/测试/站点同步 | R08-002显式默认端口；R08-003 MFA错误提示；R08-005失败草稿取消；R02-003空密文字段仍沿原号，设置联R15 |
| ShellPage订阅导航、MediaVM/Page AniBT快照、Service.AniBtAvailable | R08-006双向显隐/恢复；执行守卫仍在；联R09/R16 |
| Files.Parse、SubscriptionsVM两读、LibraryPlayback.Find | R08-004同ID不同完整身份混用；联R10，不删正常默认组数字唯一回退 |
| LibraryPage搜索四来源及释放、Sidebar、Dialog样式 | 保留固定四来源例外；源码不等于真实窗口/输入通过，联R16/R17 |

30全文联审及24成员联审按验证JSON逐项记录，不将其主责分区提前完成。10入口674次含重叠执行全部通过，6组缺陷反例成立；旧红项/增量保留。

## R09 联审成员与交接（2026-11-06）

44主责全文8,120行，20全文联审及31成员联审逐项记录在[R09验证](R09-verification.json)，共更新95行原台账；历史基点和证据保留。

| 共享成员 | 结论与交接 |
| --- | --- |
| TransferSubmit/History/FileItem 与 AniBT封存计划 | R09-001实际同源清理范围超出所选logid，两位独立复核；R09-002证明本地归属的匹配端也要核存储；联R08/R17 |
| AniBT Submit尾扫/Collect每叶编号/确认行 | R09-003–005分别保留回执、当前季集及实际源文件名；联R10/R15 |
| ShellPage缺集补读、MissingEpisodeRow与开关事件 | R09-006动画资格与能力混用，专用事件缺监听；执行端仍拒绝联网；R08-006另有回调快照根因，联R16 |
| HistoryMatch.Dependents存储身份 | R09-007异存储同路径误阻断；缺字段回退需维持保守边界 |
| Shell删除刷新、RemovedMedia、LibraryVM Reload | R09-008确认删除后旧索引卡重现；不同于R06-007/R07-002，联R06/R07 |
| EmbySession current/SettingsWindow来源变化 | AniBT旧确认来源A→B→A有未决可达性，保留隔离窗口验证缺口；不能改变固定scope后台任务语义 |

9入口683执行含重叠通过，17生成绑定另计；8组反例不是产品修复。R02 schema27及历史红项仍未解决。

## R10 完成后的共享成员交接

28主责全文已读，26全文联审与40成员联审，精确范围/哈希见[R10验证](R10-verification.json)。每次异步调用、场次代次与资源释放者见[R10生命周期表](R10-lifetimes.md)，报告见[R10](R10.md)。本次联审不提前完成R11–R14。

| 共享链 | 结论/接续 |
| --- | --- |
| Transport下一项、版本、父级→Planner | R10-001跨剧父信息；002用户队列槽位；003本次实际起点；联R06/R07/R12 |
| Start准备失败→旧影片/页面恢复 | R10-004结束决策、005展示/Attach恢复；联R03/R11 |
| Core初始报告、手动补报、暂停位置 | R10-006起停排序、007同场暂停顺序；联R04/R05/R17 |
| VM章节回填→Core Merge | R10-008两类服务器段提前返回；联R11/R17 |
| 音量服务队列与native Swap | 人工边界可写B，但当前独占输入绕过此队列，撤回缺陷；R11/R12若入口变化再核 |
| 章节菜单/飞牛季集SeriesId | 分别缺真实Flyout存活、完整FnNas到caller采证，R11/R17继续；未包装成确定缺陷 |

旧R05-001/004、R07-004、R08-004、R09回执/编号沿原号；设置/技能“本季末退出”旧说明交R15/R18，当前源码按全剧连播。仅提交文档，按用户要求暂停R10。

## R11 完成后的共享成员交接

144主责全部处置，44全文与14成员联审，精确路径/hash见[R11验证](R11-verification.json)。Backend/IPC/native gate、交换链/VO、页面拖边与脚本手势的责任见[R11边界表](R11-lifetimes.md)。本轮不提前完成R12–R18。

| 共享链 | R11阅读及处置 |
| --- | --- |
| VM字段/Events/Transport → Core Lifecycle | 首次UI发布与实际场次区分，R11-001；R10已登记问题沿原号 |
| AppSettings/OutputOptions → factory/backend | MpvSettings/输入PlaybackSettings及Build依赖成员，主语义交R12/R15；内置/外部声明分开 |
| SettingsVM/SettingsPage/ShellServices | PlayerCard/Custom/Screenshot及Attach实际委托、Build44-217；include入口可达R11-003；截图多步写失败反馈交R12 |
| EmbyClient章节图片 | 974-991明确FnNas/Plex拒绝；图片index按服务器章节，不借mpv列表索引 |
| Lua/uosc与native retain/Swap | R11-002旧Speed/Volume手势；Button10ms新片adopt未决；R10人工音量gate候选不重开 |
| stats → Shell键盘 | 完整stats与fixed上游读；集成input-vo-keyboard关闭，R11-004；独占Native DOWN单独有效 |
| 输入/几何正式测试 | 19Register和18Playback方法已读与运行；Lua原review8项换运行时采证，原wrapper前置未满足 |
| Windowing/Interop | 主责全文；真实窗口未运行。Watchdog仅多余ticker观察；Pulse切线/纹理来源交R16/R18 |

原1876条基点与R01–R10证据保留，主树12文件其他区变化见[file-delta](file-delta-20261106-r11.json)。每区暂停，R12未开始。

## R12执行增量（2026-11-06）

45主责全部处置，36全文和42成员联审，原123条台账追加本区证据。准确路径/行范围及hash见R12-verification.reviewedFiles；执行链见[R12边界](R12-lifetimes.md)，shader逐pass见[R12-shaders](R12-shaders.md)。

| 共享链 | 本轮实际阅读/结论 | 交接 |
| --- | --- | --- |
| AppSettings / Migration / Preferences | Video728–958、Audio960–1059、Shaders1073–1261、SVP与截图字段；Normalize571–630、shader647、补帧/音频夹限；Preferences/Reset全文 | R02原身份/Secret及schema27未决保持；R13字幕亮度联 |
| Settings VM / rows | 建卡/设备1127–1241，截图1449–1538，Audio1806–1912，恢复/导入2758–2845、共用尾2897–2929；Choice219–313/Number433–512/Optional1–81；Applicability全方法 | R12-001外部gpu四曲线未过滤；004组无版本；005确认被吞。R15设置访问/真实UI |
| Planner / OutputOptions / Policy | Planner/Policy/Inline全文；视频/HDR/补帧/音频实际选项和profile优先级，字幕方法入本区全文但业务选择交R13 | 不以build/native接受证明画面；R10生命周期去重 |
| Player VM音量 | Events581–593/595–756/957–1066/1117–1287，Mpv127–208，Transport结算与ViewsRail.RightTapped146–152 | R12-002重置；003旧status和相对读回无共同采样次序；R11独占EOF拖动沿原号 |
| Player VM shader | Mpv298–587完整Prepare/Adopt/Watch/Queue/Change及菜单；MeasureSurface/snapshot/source/Monitor选择 | 本场偏好不变，实际Output记Active；32真实VM过，R11统计滚动未修 |
| Service / native / IPC | Service1–288/391–527，LibMpv初始SVP/选项/事件/gate/setter/read回及毁lease；MpvProcess358–382/IPC227–247 | R12-004/005截图边界与确认；R13字幕多步写进一步审 |
| master切链新增 | ShaderSwitch.Configuration、IShaderBatchPlayerControl、完整新handle batch/ReadNativeText及Service全快照/回滚；真实新正式59项 | 固定f71883d ignored archive审读与运行；不动产品、不替master最终验收 |
| uosc菜单 | main1060–1062/1159–1163/1466–1473及Volume26–58/256–268，原生reset100对照 | R11原全文无变，未重跑uosc；R14消息链 |
| 主Tests / 专项 / tools | 151定向注册、FrameInterpolation全部生产提取/假UI/原生边界、Monitor真实VM；主Program相关显式入口；音频原采集器全文 | R17真实输入/提取器拒绝/runner整体可信度；旧红不删除 |

音频旧“mpv自动回退”及“下混抬对白”注释、shader旧菜单偏好叙述由R15/R18统一文本；没有据此扩成额外运行缺陷。master其他区的38差异清单保留owner，R12唯一主责ShaderSwitch增量及相邻共享/test已在固定snapshot联审，未误标原台账基点hash为新版本。

## R13执行增量（2026-11-06）

21主责全文2770行完成；77全文及48成员联审，146条原台账追加范围/hash/历史，保留1876原项。完整路径与范围在R13-verification.json，不把共享大partial的局部阅读计全文。

| 共享链 | 本轮已核范围 | 处置 |
| --- | --- | --- |
| AppSettings/Migration/Preferences/Output | 字幕语言/模式/外观/颜色/编码/HDR亮度、迁移归一和全样式名单 | 主次/Forced/Foreign/颜色无新增问题；R13-003预测外挂ID |
| Settings/Page/Rows/字体 | SubtitleSections/Live/Push/Fonts/保存恢复共尾，颜色字体预览完整消费 | R05514过；preview NoteVisibility漏绑定交R15/R16，未真实UI |
| Player/Service/native/IPC/uosc | 选轨/延迟/菜单、完整样式组、完整Swap和native gate、主次可见性/映射 | R13-002/004/005；正式批次测试只换不同handle，不覆盖native同窗gap |
| ItemCommands/Detail/Scope/四产品轨 | 固定来源打开管理、父reload守卫、删除前全快照、文本DeliveryURL | R13-001，R04-001/004原号；同ID路径变化有界未知 |
| 翻译配置/DI/Controller/ShellQueue/Lua | coherent请求身份/appearance保持、退出await、实际OSD parser/cue、预算 | R13-007/008；旧cue失败退避和迟到缓存撤回新缺陷 |
| 正式测试/端点/提取器 | 全文夹具和真实源码提取边界；Core/x64Shell/Tests一致 | 正式Dialog缺_blurCancel红；ignored诊断37不能冲销；Drop默认74通过 |

本轮不修产品/正式测试、不整合或发布。mastershader/详情增量关联diff已核，无R13主责漂移，其他域后续owner复核。R13完成后暂停。

## R14 弹幕来源、输入与任务边界（2026-11-06）

24主责3537行全文已读，38全文/25成员联审、87台账追加；精确范围与当前hash见[R14验证](R14-verification.json)，生命周期见[R14任务边界](R14-lifetimes.md)。Client完整operation drain、VM load/search request/context、Lua serial/transfer、native pending/requestId/回执、Panel Close/来源重算与Settings Ready均已沿最终消费核。6P2未修，真实UI/媒体/服务器/部署缺口保留，不提前完成R15–R18。

R10-001旧_parent年份亦影响DanmakuSeriesYear；R11消息解析/输入取消交接已核，C1 parser静默拒绝新号R14-002。R15接空custom从属显隐，R16接真实模板/窗口，R17接标准py环境红及translation夹具revision维护，R18接旧自定义凭据/镜像文案和部署调度未验。master116454f的shader批增量未影响普通Command或弹幕消息，其他39差异另登记不接入。

## R15 本轮成员联审（2026-11-06）

61主责全文17,524行；73全文/17成员联审及每项hash见[R15验证](R15-verification.json)，151项原台账追加而不改身份/history。SettingSection/分类目录/Attach/Save/重入/所有行工厂与主VM全文完整；业务字段沿原主责。SettingsPage/Window与所有FnNas管理/帐号、通知/Dialog、Dashboard/WebConsole全部全文。当前源、协议/权限/确认顺序/输入与测试边界已联审，八项必要缺陷见[R15](R15.md)，任务归属见[R15边界](R15-lifetimes.md)。原R03-002/R06-009/R02-004/R14-006沿号，R13示例说明未编号缺口合R15-006。标准托管354次含重叠与DOM14/25分列，无真实WinUI/WebView/服务器；旧红/历史增量保持。固定master4b1a506主责无漂移，41其他差异登记未接入，审查文档留def8待全计划整合。R15后暂停，R16未开始。

## R16 本轮共享机制与消费交接（2026-11-06）

59主责全部处置，58文本全文10,298行与PNG分列；81全文/34成员联审范围/hash见[R16验证](R16-verification.json)，174原台账追加保持身份与全部历史。4P2见[R16](R16.md)，资源/取消/缓存/控件归属见[R16边界](R16-lifetimes.md)。

| 共享路径 | 当前结论 | 接续 |
| --- | --- | --- |
| ControlBackgroundBrushes/主题/详情与主页按钮 | R16-001材料零档错误；稳定微软源码明确blur/noise和实色回退，缺2.3.9精确revision/真实像素 | 修回归应保留master默认半透明与hover反馈，R17接错误零tint断言 |
| BannerSlide/store/WinRT/轮播删除 | R16-002坏盘恢复绕过；两Ensure0GET/2缺图，Clear后1GET成功 | R06消费同根因，非真实轮播截图 |
| CardItem/Library图种候选 | R16-003首404永久缺图；Thumb灰/Primary对照成功 | 只全404才_missing，暂态/取消/权限保持原分类 |
| CardItem拼图/holder/PosterCache | R16-004cache-hit漏wanted；两模型false回填，true/old-holder正常 | 不取消整个旧下载修回收；未测进程峰值 |
| 主树背景相关增量 | 初4b1a506终d984ac9，49总体/7主责差异未接入；新Showcase通知纠正首Hero比例候选 | 原R07-009仍适用；全计划后整合审查文档 |
| Dialog/Focus/HC/滚动 | DialogDismiss先吞按下、Closing/None守约；EgPicker队列/EpisodeInvoke/遮挡仍缺真实前件 | R15-004/R13-006/R15-006/007沿号；真实窗口/UIA/帧时间未验 |

R17接标准入口/提取器/并行数值/取消断言与当前计数（Image49+22/8pin、Page默认137）；R18接旧菜单材质注释、README计数与资产来源。字体R15-005、轮播R06-008/009/010/011等沿原号。本区不修产品、不合主树/推送/发布，完成后自动暂停，R17未开始。

## R17 全部基础设施与跨区边界

781主责处置及准确hash/全文复用见[R17验证](R17-verification.json)。60顶层/6辅助/59登记，所有框架/提取器/匿名传输/报告/取消/清理已核；13P2+2P3本树未修。旧业务按原号，R16-001零tint假透明、R07飞牛、R13Dialog/R14py红仍在。主树53/27固定增量未接入，R046三pin失配；后续六路径只登记。正文与资源归属见[R17](R17.md)和[边界](R17-lifetimes.md)，R18只接叙述/来源/历史剩余。

## R18 文档与公开资料审查补记（2026-11-06）

284主责处置和22联审见[R18](R18.md)及[验证](R18-verification.json)。八入口仅文档/工具：skill-check15（2未校验metadata警告）、skill-test5、support-check/support-test7、policy3文档/21负例、audio-helper14、官网真实main.js匿名DOM17断言、结构/链接检查；正式工具26项与匿名断言分列，不算应用全量或交付。6dist相等、81JSON解析、1386本地链接1359存在/27缺失，历史缺出处保持。

R04实时URL/旧路由、R10全剧连播、R14退役凭据/四卡与R16计数交接已核，必要文案未修；错误参考链接不在主SKILL结构检查范围。R03-006与R17-013同根因，最终只沿R03-006，保留原报告。R02schema27、历次增量和当前master产品仍待全项目最终差异复核。R18完成后自动暂停，不合主树/修产品/push/发布。


## 最终跨区闭合

[最终增量](FINAL-increments.json)记录schema27、R16后新增背景亚克力/浅色动作及R17后六路径；原AppSettings/SettingsMigration待复核现为FINAL-001待修，未改历史结论。八联合场景的已确认问题与最小未决验证分别见[总报告](SUMMARY.md)。原始作用域、报告、失败/未执行和成员历史不删除。
