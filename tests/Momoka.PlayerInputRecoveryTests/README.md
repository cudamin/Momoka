# 播放拖边与统计输入恢复回归

此项目每次编译以唯一首尾原样提取当前 `PlayerViewModel.Interaction` 的交互/冻结/统计方法，以及 `PlayerPage.ClientRect` 的完整拖边冻结/释放和 `PlayerPage.Input` 的快捷键/统计队列方法。只有控件值、窗口、播放端点替身，不运行WinUI；11项验证首次UI发布、实际场次/原生文件变化、停止、原暂停、恢复偏好、统计启闭及迟到读取。ServiceBoundary执行真实Core新文件范围命令，假handle在执行点拒旧文件，普通命令调用数必须为0。

按项目Release/x64、单节点、禁共享编译与nodeReuse构建后运行本项目DLL。不是完整主套件或实际拖动/键盘/GPU验收。Lua另用`py -B -X utf8 tools/test-stats-input-recovery.py --output <本树全新目录>`，核准随包libmpv，真实stats.lua与LuaJIT/JSON工具，假30个pass、属性、时钟和OSD；不加载媒体、用户配置、窗口或网络。7项验证尾部滚动、关闭恢复、直接页及文件边界。

原红：resize-red5项3失败，stats-red2 7项5失败；最终input-receipt11/11和stats-receipt7/7。初Lua options缺假script-opts、Service夹具缺active及接口成员等过程失败保留，不计产品红。Shell实际最终Release/x64编译0警告0错误，尚未真实WinUI运行或主树交付。
