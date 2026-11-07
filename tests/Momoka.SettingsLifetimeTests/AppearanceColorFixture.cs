namespace Momoka.Tests;

/// <summary>Real Shell colour rows across two sections; no picker, theme preview or settings IO.</summary>
internal sealed class AppearanceColorFixture
{
    private static readonly string[] OriginalColors = ["#808080", "#FFFFFF"];
    private static readonly string[] DraftColors = ["#123456", "#A1B2C3"];
    private readonly object _vm;
    private readonly object _sections;
    private readonly List<(object Section, object Rows, object Row)> _colors = [];
    private int _writes;
    private int _saves;
    private int _finishes;

    internal AppearanceColorFixture(object vm)
    {
        _vm = vm;
        var runtime = ShellRuntime.Current;
        _sections = runtime.List(vm, "Sections");
        for (var index = 0; index < OriginalColors.Length; index++)
        {
            var row = runtime.New("SettingColorRow", "匿名外观颜色 " + index, null!, OriginalColors[index],
                (Action<string>)(_ => _writes++), (Action)(() => _saves++));
            runtime.Invoke(row, "ConfigureConfirmation", OriginalColors[index],
                (Action<string?>)(_ => { }), (Action)(() => _finishes++));
            var rows = Array.CreateInstance(runtime.Type("SettingRow"), 1);
            rows.SetValue(row, 0);
            var section = runtime.New("SettingSection", "主页", "匿名颜色卡 " + index, "仅验证取消", rows);
            runtime.List(vm, "Sections").Add(section);
            _colors.Add((section, runtime.List(section, "Rows"), row));
            runtime.Invoke(row, "BeginEdit");
            runtime.Set(row, "Color", DraftColors[index]);
            Assert.True(runtime.Get<bool>(row, "IsEditing"));
            Assert.Equal(DraftColors[index], runtime.Get<string>(row, "Color"));
        }
        Assert.Equal(0, _writes);
        Assert.Equal(0, _saves);
    }

    internal void AssertCancelled()
    {
        var runtime = ShellRuntime.Current;
        Assert.True(ReferenceEquals(_sections, runtime.List(_vm, "Sections")), "取消不替换设置卡集合");
        for (var index = 0; index < _colors.Count; index++)
        {
            var (section, rows, row) = _colors[index];
            Assert.True(ReferenceEquals(rows, runtime.List(section, "Rows")), "取消不替换颜色行集合");
            Assert.True(ReferenceEquals(row, runtime.List(section, "Rows")[0]));
            Assert.Equal(OriginalColors[index], runtime.Get<string>(row, "Color"), "释放必须恢复确认前颜色");
            Assert.False(runtime.Get<bool>(row, "IsEditing"), "释放必须结束颜色草稿");
        }
        Assert.Equal(0, _writes, "取消不能写入颜色设置");
        Assert.Equal(0, _saves, "取消不能保存");
        Assert.Equal(2, _finishes, "每个正在编辑的颜色只结束一次");
    }
}
