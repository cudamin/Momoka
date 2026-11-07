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

主树证据目录为`artifacts/recovery-fixes-master`，隔离树证据已复制到其`preserved/accounts`（31文件）与`preserved/subtitle`（18文件），逐文件SHA256一致。主树专项、Shell、实际UI和本地交付已全部完成，最终记录见下。

## 验证边界

未使用真实配置/凭据，不实服登录、播放或写入，不push/上传。关闭方法夹具不是实际HostWindow慢关闭/25秒超时/窗口闪回验证。Shell工作树附加格式检查有默认Debug缺Core DLL的加载警告，不当作完整格式通过，也不为此扩大成Debug/工具链修复；本批按日常闸门执行，并核所有变更文本编码/行尾/空白。

初次实际登录VM检查被严格Core哈希守卫拒绝：默认Tests的bin/Release Core与Shell的bin/x64 Core不同，尚未进入VM用例，日志signin-vm.log原样保留。统一Tests为x64 Release重新构建并核对两Core实际SHA后重跑，不放宽校验或复制DLL求绿。

## 主树最终验证与发布前验收

主树x64 Release Core/Tests的3993项全通过，实际Shell登录VM105项全通过；HomeDirectory31、SubtitleDialog48、SignInInput128均在主树新编译后全通过。Shell Release/x64零警告错误，测试与Shell的Core逐字节SHA一致。开发程序四分区实际UI53/53、退出0，字幕1100/760及误报成功反例实屏已目视核对。范围/报告和源码及DLL哈希见ui-development-receipt.json，路径artifacts/recovery-fixes-master。开发bin不含native音频枚举载荷，相关警告不冒充真实设备验证；当前五项不修改音频输出。实际交付发布与发布件账号/字幕运行已完成，最终记录见下。

## 最终本地交付

- 产品/工具来源提交：`5a2ed21900288db9e8eac96083a11bd04c649920`。实际程序`C:/Users/89400/Momoka/artifacts/publish/win-x64/Momoka.exe`，版本保持 **0.2.5.0**。546文件、286.4MB，批准mpv/运行时/着色器/许可完整校验通过；桌面`C:/Users/89400/Desktop/Momoka.lnk`已实核指向它。未生成ZIP/MSIX、未上传。
- 发布件DLL SHA256：`FC390A990FA69BC5A40DF83A293701C664C025C8ECB619589088F660A5445152`，Core：`3D1A05EB11BAED61D04540D1BEA0BB1DA3190F020A17B025891BC228CE07946D`；UI源码SHA与开发验证一致，发布导致元数据/载荷环境变化后又运行实际发布件。
- 发布件accounts/metadata **13/13、0失败、退出0**，PID408；当前启动日期加PID对应日志为`C:/Users/89400/AppData/Local/Momoka/logs/shell-probe-20261107-024823408-408/logs`，不能拿重复PID的旧日期目录充当前结果。宽窄字幕与误报成功反例实屏已核对。`ui-published-receipt.json`保留实际源DLL/源码哈希、分区范围、正常退出。
- 开发程序shell/accounts/settings/metadata **53/53、0失败、退出0**，PID6540；完整x64主测3993/3993、实际登录VM105、独立Home31/Subtitle48/SignInInput128全通过，零失败零跳过。两个UI运行有重叠，不合计成66个独立检查；其余122个UI检查未执行，不称全量探针通过。
- 本批两树`review-account-lifetime`（原f8fb55e）与`review-subtitle-receipt`（原b092aae）已原生归档，应用附件已变archived_worktree且目录已移除；原红/绿/DLL/源码审计已先保存并逐文件核SHA。Git patch等价已核，字幕工具仅被主树新增且已实际验证的负向对照替代。临时字幕分支已删，本地只留master。旧聊天主工作树的原生保护仍保留，不绕过删除；其他聊天树未动。

完整证据入口：`artifacts/recovery-fixes-master/main-x64-build.log`、`main-x64-full.log`、`signin-vm-x64.log`、各专项build/run日志、`ui-development-receipt.json`、`publish-final.log`、`ui-published-receipt.json`、`archive-receipt.json`。纯记录提交不重建或重发程序。

另一个聊天已获用户授权的轮播实装提交`6f93eb3`留在其`a56e/Momoka`树，由该聊天接管后合主树、重验和交付，本批不夹带该功能。当前五项交付已完成，不以另一个待合功能掩盖本批状态。
