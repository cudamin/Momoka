# R14 弹幕任务、输入与凭据边界

本表对应 `a998403`，精确路径/hash见[R14验证](R14-verification.json)。只读审查与匿名观察，不是实际播放或服务器验收。

| 任务/状态 | 所有者、取消与最终消费 | 本轮结论 |
| --- | --- | --- |
| API获取/搜索/发送 | Client BeginOperation登记所有票，shutdown关闭入场、取消并等idle后释放HTTP/gate | 正式生命周期断言通过；POST不自动重试，结果未知不冒成功 |
| 凭据缓存 | 内置Source gate串行，提前换新与未过期回退；Client401失效后一次GET重试 | R14-001：失效后删盘失败不能重读旧盘；TTL弱窗口单列未确认 |
| 来源选择 | SourceMode＋地址/令牌摘要；Dandan私库与custom episodeId独立缓存 | 请求/缓存隔离有效；R14-004已开Panel来源不重算 |
| 自动匹配 | VM _danmakuSearch、request及InteractionContext；续季/特别篇不猜 | 新结果守卫有效；R14-005旧Panel.Close会取消驻留最新票；旧_parent年份沿R10-001 |
| 面板手动查询 | Panel request/connectionVersion＋VM token；Close/切源调用全局CancelSearch | 旧结果OnLoad拒绝；Close缺本panel取消所有权 |
| 弹幕加载/显示 | VM load票、请求/interaction；Begin/chunk128/commit携Lua serial/transfer | 迟到块拒绝，原始40000与过滤是现有预算；真实渲染未验 |
| 外观刷新 | VM100ms合并style；Lua选项/文本宽缓存、resize one-shot、速度补偿 | 当前正式Lua/实际timer通过；普通命令文件间隙未证新的串弹幕缺陷 |
| 输入草稿与提交 | VM inputContext/requestId集合、send CTS；native pending配correlated result | R14-002 C1无回执卡住；R14-003服务适用性reset清同片草稿 |
| 设置显隐 | SourceReady/IsAvailable与Settings DanmakuReady两套判据 | R14-006空custom地址仍展开无效从属；恢复入口需保留 |
| 退出 | VM取消load/search/style，lifetime结束；await Client.Shutdown再Playback.Shutdown | 真实当前源码与专项匿名drain已核；无真实窗口/native关闭时长 |
| Worker | GET专属路径，AES-GCM nonce12/tag16/AAD kid exp，ASSETS回退 | 4匿名测试＋实际C#跨语言向量；部署/Secret/静态调度未验 |

六项均未修；报告中的源代码、托管边界、Lua以及真实UI/服务状态分开。旧失败与历史差异不能由本表的通过项冲销。
