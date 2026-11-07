# 第二批恢复与反馈修复台账

本批处理五项仍能复现的P2问题。原133项是历史累计，不能据本页推断当前主树剩余未修总数。用户授权自行选择修复且不按分区暂停，实际主写与交付树为`C:/Users/89400/Momoka`。

| 问题 | 主树提交 | 最终行为 |
| --- | --- | --- |
| R02-003 | 0a7bde1 | MoviePilot密文显式null按损坏配置拒绝，保留原文、尝试有效备份；缺键和空串仍有效。 |
| R04-002 | fe7518a | 统一鉴权值只保留可打印ASCII；混合名称保留ASCII部分，全非ASCII名称回退unknown，生成/保存的稳定设备ID不变。 |
| R03-003 | a271874（原0faa517） | 同服另一个账号已确认的产品/API/服务器ID纠正可继续恢复；第三种绑定、撤销和旧代次仍拒绝。 |
| R03-005 | 0749555（原f8fb55e） | 首个关闭await前终止登录、退订回调，迟到入口与来源通知不再重建界面；已合法开始的来源操作保留收尾。 |
| R04-004 | 2138e4f（原b092aae） | Jellyfin204保留未确认状态，列表通过原scope交父刷新；本张表只读刷新/关闭，不误报目标成功或自动重写。 |

账号细节见[Account-Recovery-Fixes.md](Account-Recovery-Fixes.md)，字幕协议与原样基点审计见[Subtitle-Receipt-Fixes.md](Subtitle-Receipt-Fixes.md)。无可靠候选关联ID时，即使Jellyfin列表新增也不能追认所选字幕已安装；关表重开可继续核对。设备头回退只影响服务器侧可发送的显示名称，不改本机名称。

## 红与绿证据

- 配置：原178项172过6败；修后设置专项178全过。
- 设备：原11项3过8败；修后11全过，含两产品实际回环HTTP传输。
- 账号恢复：原完整3972项3960过12败；修后定向115全过。关闭：原31项17过14败；修后31全过，登录输入128全过。
- 字幕：同一最终48项测试配完整08324e3原样源码，0警告错误编译后39过9败；当前48全过。测试夹具缺附加GET及基点漏BannedSymbols文件的初次失败均保留，不算有效行为红，不放宽断言。
- 主树最终Core/Tests Release编译零警告错误，完整主Tests **3993/3993，零失败零跳过**。

主树证据目录为`artifacts/recovery-fixes-master`，隔离树证据已复制到其`preserved/accounts`（31文件）与`preserved/subtitle`（18文件），逐文件SHA256一致。主树专项、Shell、实际UI和交付记录待最终完成后补入，当前不宣称桌面程序已更新。

## 验证边界

未使用真实配置/凭据，不实服登录、播放或写入，不push/上传。关闭方法夹具不是实际HostWindow慢关闭/25秒超时/窗口闪回验证。Shell工作树附加格式检查有默认Debug缺Core DLL的加载警告，不当作完整格式通过，也不为此扩大成Debug/工具链修复；本批按日常闸门执行，并核所有变更文本编码/行尾/空白。

初次实际登录VM检查被严格Core哈希守卫拒绝：默认Tests的bin/Release Core与Shell的bin/x64 Core不同，尚未进入VM用例，日志signin-vm.log原样保留。统一Tests为x64 Release重新构建并核对两Core实际SHA后重跑，不放宽校验或复制DLL求绿。

## 主树最终验证与发布前验收

主树x64 Release Core/Tests的3993项全通过，实际Shell登录VM105项全通过；HomeDirectory31、SubtitleDialog48、SignInInput128均在主树新编译后全通过。Shell Release/x64零警告错误，测试与Shell的Core逐字节SHA一致。开发程序四分区实际UI53/53、退出0，字幕1100/760及误报成功反例实屏已目视核对。范围/报告和源码及DLL哈希见ui-development-receipt.json，路径artifacts/recovery-fixes-master。开发bin不含native音频枚举载荷，相关警告不冒充真实设备验证；当前五项不修改音频输出。实际交付发布与发布件账号/字幕运行仍待完成。
