# 全局控件材质回归

此项目编译链接正式 `GlobalControlAppearance`、`ThemeHost`、`InputControlTheme` 和 `ShellPrefs`，仅替换 WinUI 画刷、资源字典与元素对象边界。检查三态浓度、独立染色/明度、色值预览、主题通知前重放、输入和卡面隔离、系统高对比资源及 XAML 背景引用。

```powershell
& 'C:\Users\89400\.dotnet\dotnet.exe' run --project tests/Momoka.GlobalControlAppearanceTests/Momoka.GlobalControlAppearanceTests.csproj -c Release -p:Platform=x64
```

结果证明正式应用逻辑与静态资源连接，不证明 Windows 原生亚克力渲染、真实省电切换或运行中控件模板。实际界面检查由根交付会话串行完成。
