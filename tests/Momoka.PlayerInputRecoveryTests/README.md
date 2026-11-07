# 播放拖边与统计输入恢复回归

此项目每次编译以唯一首尾原样提取当前 `PlayerViewModel.Interaction` 的交互/冻结/统计方法，以及 `PlayerPage.ClientRect` 的真实暂停出口与完整拖边冻结/释放和 `PlayerPage.Input` 的快捷键/统计队列方法。只有控件值、窗口、播放端点替身，不运行WinUI；18项验证首次UI发布、实际场次/原生文件变化、停止、原暂停、恢复偏好、慢暂停命令顺序、暂停拒绝、统计启闭及迟到读取，并保留旧ResizeFreeze规则/接线检查。ServiceBoundary执行真实Core新文件范围命令，假handle在执行点拒旧文件，普通命令调用数必须为0。

按项目Release/x64、单节点、禁共享编译与nodeReuse构建后运行本项目DLL。不是完整主套件或实际拖动/键盘/GPU验收。Lua另用`py -B -X utf8 tools/test-stats-input-recovery.py --output <本树全新目录>`，核准随包libmpv，真实stats.lua与LuaJIT/JSON工具，假30个pass、属性、时钟和OSD；不加载媒体、用户配置、窗口或网络。7项验证尾部滚动、关闭恢复、直接页及文件边界。

原红：resize-red5项3失败，stats-red2 7项5失败；最终input-receipt11/11和stats-receipt7/7。初Lua options缺假script-opts、Service夹具缺active及接口成员等过程失败保留，不计产品红。Shell实际最终Release/x64编译0警告0错误，尚未真实WinUI运行或主树交付。

自审新增慢暂停命令500ms实际端点等待，旧190ms状态轮询会先恢复、晚暂停（pause-order-red3 17项1红）。冻结与恢复现在等待file-scoped命令确认，拒绝不取得恢复责任；不能将等待状态轮询当命令顺序。最终18/18、Shell pause-complete-shell-build.log 0警告0错误。老源码接线检查因出口变更原16项1红，改为真实file-scoped出口且保留规则，全部通过。
