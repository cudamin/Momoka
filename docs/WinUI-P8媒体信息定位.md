# P8 媒体信息表定位调查

日期：2026-11-13。独立树`codex/winui-p8-info-table`，基线`a7f0cac`。真实自检“10行已渲染7，首行时长”的报告保留；本调查不读取或输出真实媒体、服务器、凭据，只从主控给出的汇总失败与生产源码继续。

## 已追链与未知项

`ShellSelfCheck.ShowInfo`先读取文件页首屏/文件选项，调用正式`DetailPage.ScrollToInfo`。`ReadInfo`从`page.InfoRealised`计数，后者对每个InfoRows索引调用`InfoTable.TryGetElement`。重复最多8眼，每眼少行又调用一次`ScrollToInfo`；报告仍要求实际已实现数等于模型行数且第一段含标签。

`ScrollToInfo`在Body.UpdateLayout后读`InfoPanel.TransformToVisual(Body.Content).Y`，直接ScrollTo该内容坐标。InfoPanel是Body纵向StackPanel内的正文块，包含其他信息牌子、媒体信息副标题，再接垂直StackLayout的InfoTable。表格前几行没有实现可能是目标上沿/后续布局漂移、原生锚定或实际viewport问题；源码不足以归因，不能直接把expected换成7或把虚拟化关闭。

## 可审阅匿名复现

新增检查台`InspectInfoTable`和`InspectInfoTableFailure`。通过正式`ItemDetail.MediaInfo`固定构造10行匿名影片（文件/格式/大小/时长/分辨率/码率/色深/帧率/音频/字幕），真实DetailPage/ScrollView/ItemsRepeater；访问只交给匿名FakeTransport和本地生成图片，拒绝所有写入/播放。

1280×900、760×760、760×560每档先记录原位置，再按正式ScrollToInfo执行8眼；`info-table-geometry.json`保存extent、offset、viewport、panel/table→content及viewport坐标、table/content高度、realized索引和每行actual bounds。只记录匿名标签，不读或打印真实源内容。实际成功仍严格全部10行和表格在视口，不加缓存、不强制GetOrCreateElement。

故障入口将真实InfoTable用TranslateTransform移到视口外，保持现有计数读取并验证视口检查失败；不是伪造expected或人工少计。离线共享fixture校验正式MediaInfo确有10行、首标签、音频/字幕，并验证少首3行、乱序、无首标签必须拒绝；并入现有KeyboardJourneyTests使当前总10项，全部通过、0失败0跳过。该通过不等于真实复现或产品修复完成。

## 下一步

根代理需在匿名检查台当轮运行成功/预期失败，并返回几何JSON。先比较每眼panelContentY是否变化、offset是否准确、tableViewportY与realized首索引，再决定是否修正文定位/锚定或采用有证据的逐段可达验收。原自检10行判据与历史报告不变；得到原生证据前没有生产修复或“已洗绿”结论。

## 原生复现后的最小定位修复

根代理`190016656-31692`匿名运行在1280×900的全部9眼固定10行；760×760首两眼尚未滚动，look1实际offset1346（ScrollableHeight1346），panelContentY1364、tableContentY1468、tableViewportY122，10行全部渲染且行坐标122–320。look2–7所有几何与offset不变却realized清空。不是视口太短，也未证明与真实自检“10/7”同根因。

正式ScrollToInfo原来每眼发目标1364，超过实际可滚上限1346；即使已经到达页尾也重开原生view-change。固定框架`693f472`的`Repeater/ViewportManagerWithPlatformFeatures.cpp`在`OnEffectiveViewportChanged`里对未完成ScrollPresenter correlation提前返回；实际几何未动不等于effectiveviewport更新。此源码说明了反复超范围事务为何值得消除，但没有把框架内部状态猜测写成已测根因。

最小修复只将目标限制到`[0, Body.ScrollableHeight]`，距离当前offset少于0.5 DIP时不发重复请求，保持原信息块上沿语义、正常滚动、虚拟化和10行判据。编译时严格提取真实ScrollToInfo方法，匿名控件边界覆盖1364/1346、连续8眼只发一次、短内容不发事务、正常上沿/负上沿与缺内容；缺失/重复源码边界使构建失败。专项扩到12项，仍需根实际同路径原生复核；旧失败保留。
