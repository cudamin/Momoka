# 字幕翻译运行链离线回归

假播放器与假翻译委托执行当前 Core Runtime；不使用真实字幕、密钥、服务器或用户配置。覆盖关闭/缺密钥/缺图层、单句去重、内存缓存、旧句取消与迟到回复、注销、失败脱敏及独立图层清除。另检查设置备份/恢复与译文外观归一。

先用本机 .NET 10 SDK Release 编译当前项目，再 run --no-build。Lua 原生检查：py -3 tools/test-subtitle-translation.py --output <本树唯一证据目录>。最终交付仍需真实 WinUI 设置页与本地 mpv 播放画面验证。
