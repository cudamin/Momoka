# R12 选项、资源与任务边界

对应本树11921ff，整批切链增量另取master f71883d；联[R10](R10-lifetimes.md)、[R11](R11-lifetimes.md)。

| 路径 | 所有者/有效值/释放 | 本轮证据及限制 |
| --- | --- | --- |
| 视频/HDR/音频偏好 | 全局设置保存，下次播放生效；临时菜单不保存；音量记忆例外 | 生产模型→migration/preferences→card→Planner→OutputOptions已联审 |
| Planner选项层 | Baseline→profile/视频/HDR/音频/字幕→shader前置→内置pipeline；custom配置后置并有host保护 | 内置集成D3D11/gpu-next、独占gpu-next跟随接口、外部用户renderer分列；R12-001为gpu专属目录缺过滤 |
| 本场shader方案 | Settings.Shaders.Snapshot→resolver；Prepare/Launch/Adopt采用固定Monitor目标，Output记录Active | 真实VM32过；不证明实际显示器/GPU成本 |
| ShaderSwitch | 收齐profile/default/旧值→前置→glsl列表，失败回滚；service/native file generation分别守卫 | 本树49过；masterbatch改为受控UTF8配置＋全读回/全回滚，59过 |
| master整批配置文件 | native RunFor内创建随机临时文件，完整写后只读lease给mpv，finally删；白名单8名/UTF8长度/拒CRLF/NUL | 真实managed handle+假config parser端点；真实DLL33方案仅配置读回，不运行GLSL |
| 每个shader的纹理/阶段 | libplacebo按hook列表执行，各shader自己的pass_textures保存其看到的输入；缺BIND跳过 | clean1dd0226源码支持CfL后放/SSSR前序尺度；dirty补丁未核 |
| CPU补帧 | FrameInterpolationDecision不写回偏好，svp优先→HDR/高位深/fps门限→copy或software；vf完整合并 | 45规则、23回归、native184；未知元数据/帧输出/性能缺口 |
| SVP库/目录lease | IsEnabled只内置且无有效custom；准备占进程许可、验证本地x64/API4.1；module进程保留；handle销毁后释放目录与许可 | fake API/匿名IPC和native24过；没有加载用户VSScript/Python/Manager，不证明真实SVP |
| AudioCatalogue | 只共享在途读取，完成每轮刷新；设置展示Known不是起播在线判定；内置每次核名单，外部不借随包名单 | 当前AudioOutputRegister/原生共享零信号采集；强制失效设备no sound预期保留 |
| 音量/延迟 | Service局部串行→命令确认/读回→VM代次→VolumeMemory pending版本→VM自有结算延时；退出Flush | 35真实VM检查及R12-002/003采证；旧status与relative读回缺共同采样顺序 |
| 截图实时组选项 | 当前逐项重捕handle，无组revision/串行；setter失败被吞 | R12-004/005；完整Service/共用VM尾/正式假handle采证，未执行编码/真实媒体 |
| 脚本/媒体/设备 | 本轮native无媒体probe禁脚本/vo/ao；音频技能例外只用内置anullsrc零信号与共享或null AO | 不登录、不播放真实媒体库、不改观看记录；无独占听测/真实设备插拔 |

R12-004局部修正应保持“无在播无事可做”；R12-005必须保留聚合错误，使已有VM恢复尾能够拒绝Success。字幕与截图共享恢复入口，但字幕多步事务自身交R13，不从本轮截图证据推断所有设置有同一缺陷。
