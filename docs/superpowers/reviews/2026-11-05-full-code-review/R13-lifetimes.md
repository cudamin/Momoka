# R13 字幕资源、场次与写入边界

本树8e8619e；联[R10](R10-lifetimes.md)、[R11](R11-lifetimes.md)、[R12](R12-lifetimes.md)。

| 路径 | 所有者、取消和恢复 | 本轮结论及限制 |
| --- | --- | --- |
| 语言/forced/foreign规则 | 最终显式音轨优先；Any仅字幕候选；可加载轨参与自动 | Forced未知Off、明确关闭和默认回退已有纯规则/真实Service夹具；未真实服务器播放 |
| 外挂起播轨 | Planner按map全部URL及预计sid；文件失败不占native id | R13-003；真实DLL临时静音WAV/SRT+null输出，不能证明Emby/Plex线上URL行为 |
| 主次轨状态 | TrackState每场次持有实际main-selection、ff-index/external-filename；初读revision拒旧 | 7规则+15真实VM回读；只修上报不修外挂选轨；集成无当前次字幕设置入口 |
| 字幕外观组 | wanted快照、局部gate/revision、先读默认后写；service generation守卫 | R13-002缺固定native generation；最新组和新票之间亦要结算，不能只补after |
| 集成显式选轨/延迟 | 菜单捕交互，VM普通setter和本地缓存 | R13-004/005；未实际点击控件，错误确认/新file守卫需修时验证 |
| 拖入批次 | WinRT前捕page visit/VM/interaction，取Path和每笔前后重核，deferral finally | 默认Platform当前正式74过；真实Windows拖放/native事件未跑 |
| ServerSubtitles | 原scope/item/source/path/full track identity，delete确认前GET重核 | R13-001 scope可用误default；R04-001目标不一致仍红；GET/DELETE非原子 |
| Dialog写/确认读 | mutating阻止关闭/重写；未知回执立即NeedsRefresh；手动恢复只GET | 诊断37过，正式项目缺_blurCancel仍编译红；不能冒真实WinUI关闭验证 |
| 翻译运行 | coherent provider/language/endpoint/model/key snapshot；单HTTP/15秒deadline、256内存cache；旧句取消 | Client119/Runtime19；同服务旧失败仍有退避意义，撤回无条件丢失败建议 |
| 翻译图层 | 独立Lua ASS overlay，原cue/sid/time/revision；TranslationSession固定service+native file generation | 正式Lua9+payload2；R13-007编码预算错配，命令接受非显示回执 |
| 翻译设置/退出 | appearance仅替换presentation，request identity重启；old clear结束后启动新run；Stop取消并await | final UI第二排队丢run身份R13-008；无真实关窗/HTTP费用验证 |
| 主树增量 | shader批次ReadNativeText抽取保留读数/释放；详情更新为外观 | 固定diff阅读无本区主责漂移；不借本树结果声称master交付 |

不读真实设置/密钥，不调用真实字幕写或收费服务。临时WAV/SRT只在本树ignored目录，批准native hash已核；无用户媒体、观看历史、服务器负载或实机可见字幕证据。
