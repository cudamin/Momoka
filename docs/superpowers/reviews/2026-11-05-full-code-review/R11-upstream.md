# R11 固定上游对照

固定归档和已读文件hash见[R11验证](R11-verification.json)，本轮从公网读取源码，没有执行下载的脚本或构建上游。

| 来源 | 固定版本 | 已读实现与结论 |
| --- | --- | --- |
| mpv-player/mpv | 270367e8af7e2f49fc3d3f8da03c985f5ae3b402，匹配批准DLL版本 | input/ipc-win.c537、ipc.c416、parse_configfile.c174、stats.lua1825全文；client/node/command/loadfile/VO/input/Lua按完整相关成员读取 |
| tomasklaen/uosc | 5.12.0，748bb238a962f8aecd0af51ee4b46651b58f6c51 | main/Menu/cursor/Controls/TopBar/Timeline/Volume/menus/utils完整差异；Speed/text/fzy字节相同；辅助lib与locale/字符映射完整差异 |
| Windows App SDK InteractiveExperiences | 本机固定包2.1.9 | include/Microsoft.UI.Input.InputCursor.Interop.h全文：IInspectable继承及CreateFromHCursor槽6/IID吻合本地InputCursors；仅包header证据，不是Windows实机调用 |

mpv zip SHA256 `a6a1953ee661ed5312440dc4e6344f3b4de7782477adde6e43494e4a4c24f30c`；uosc zip `ebb115729486ab2093625a848ac66c9c7b22d609c932b9c6ca629ab636c87efa`。uosc tag及peeled commit经git ls-remote核实。

mpv成员阅读：client.c424-538/672-697/894-959/997-1023/1107-1166/1401-1504；client.h626-857/1481-1570；m_option.c1381-1439/3951-4065；lua.c325-440/defaults.lua165-275/410-560；loadfile.c1786-1820/2109-2195/2271-2332；command.c6480-6516/8207-8240；playloop.c1042-1127、video.c99-174、w32_common.c542-558/1546-1640、input.c574-692。没有声称整份command/client全文。

固定长度引用是正式mpv语法，本地include预处理遗漏为R11-003。mpv loadfile卸载清交换链仍保留VO，窗口capture不是文件生命周期资源，因此本地uosc必须自行取消旧控件拖动，R11-002。早期ready事件在主脚本完成之后消费、input保存按下命令用于松手，这两条候选据源码撤回。

本地stats主要字段/公式保持，但独立overlay、空数据反馈、分页裁切、三态循环和start-file清理已有行为改动；README“只改中文”不再准确，交R15/R18。集成统计尾部无输入是本地组合缺陷R11-004。

12份locale共982键，完整解析无重复key/非字符串/printf占位差异；9份与固定源码相同，其余it/pt/zh-HK该提交无同名文件。char-conv403拼音组、26721字符/26102unique与上游字节相同，539多组字属于多音映射，未逐字校读。

MaterialIcons本地字体与固定src/fonts/uosc_icons.otf同字节，仅改名；本地纹理字体与固定src/fonts/uosc_textures.ttf不同（cmap相同），历史dyphire整合包的确切对应未取得，不声称来源已完全核实，也无功能故障证据。许可正文/hash已核，发行合规整体由R18继续。
