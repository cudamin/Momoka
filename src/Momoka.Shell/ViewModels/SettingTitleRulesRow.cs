using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.Playback;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 字幕标题、音轨格式、视频文件名共用的有序关键词表。屏上越靠上优先级越高；
/// 拖动和上下按钮修改同一份稳定集合，保存后的顺序直接供 KeywordFilter 使用。
/// </summary>
public sealed partial class SettingTitleRulesRow : SettingRow
{
    private readonly Action<List<KeywordRule>> _write;
    private readonly Action _save;
    private List<TitleRuleChoice>? _dragOrder;

    internal SettingTitleRulesRow(string label, string? note, Func<List<KeywordRule>> read,
        Action<List<KeywordRule>> write, Action save) : base(label, note)
    {
        _write = write;
        _save = save;
        foreach (var rule in read()) Adopt(rule.Term, rule.State);
        MarkPositions();
        Rules.CollectionChanged += OnRulesChanged;
    }

    public ObservableCollection<TitleRuleChoice> Rules { get; } = [];

    public string PriorityHint => "拖动调整顺序，越靠上优先级越高；默认不参与筛选";
    public string NewTermPlaceholder => Label switch
    {
        "音轨格式筛选" => "自定义词，如 Atmos、TrueHD、7.1",
        "视频文件名筛选" => "自定义词，如 REMUX、2160p、HDR",
        _ => "自定义词，如 中英、SDH"
    };
    public string NewTermName => $"{Label}：新增关键词";
    public string AddName => $"{Label}：添加关键词";

    [ObservableProperty]
    public partial string NewTerm { get; set; } = string.Empty;

    private void Adopt(string term, TitlePreference state)
    {
        Rules.Add(new TitleRuleChoice(term, state)
        {
            Changed = Commit,
            Remove = row => Rules.Remove(row),
            Shift = Shift
        });
    }

    [RelayCommand]
    private void Add()
    {
        var term = (NewTerm ?? "").Trim();
        NewTerm = string.Empty;
        if (term.Length == 0 || Rules.Any(rule => string.Equals(rule.Term, term, StringComparison.OrdinalIgnoreCase))) return;
        Adopt(term, TitlePreference.Neutral);
    }

    private void Shift(TitleRuleChoice rule, int offset)
    {
        if (_dragOrder is not null) return;
        var from = Rules.IndexOf(rule);
        var to = from + offset;
        if (from >= 0 && to >= 0 && to < Rules.Count) Rules.Move(from, to);
    }

    private void OnRulesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        MarkPositions();
        if (_dragOrder is null) Commit();
    }

    private void MarkPositions()
    {
        for (var index = 0; index < Rules.Count; index++)
        {
            Rules[index].Position = index + 1;
            Rules[index].CanUp = index > 0;
            Rules[index].CanDown = index < Rules.Count - 1;
        }
    }

    // WinUI 的内建拖拽换位会 Remove + Add。中间那份缺少一条的集合不能写盘，
    // 完成／取消拖拽后只按最终顺序提交一次；没换位不写盘。
    internal void BeginReorder() => _dragOrder ??= [.. Rules];

    internal void EndReorder()
    {
        var before = _dragOrder;
        _dragOrder = null;
        MarkPositions();
        if (before is not null && !before.SequenceEqual(Rules)) Commit();
    }

    private void Commit()
    {
        // 拖拽的完成事件没来（Esc 取消、拖出窗口）时 _dragOrder 会一直挂着，之后改态度、加词、删词都会被吞掉。
        // 任何非拖拽编辑都说明那一趟已经结束：先结束它，再把当前顺序连同这次编辑一起落盘。
        if (_dragOrder is not null)
        {
            _dragOrder = null;
            MarkPositions();
        }
        _write([.. Rules.Select(rule => new KeywordRule(rule.Term, rule.State))]);
        _save();
    }

    /// <summary>隔离假表覆盖增删、态度、箭头和 WinUI 的两步拖拽提交，不读取或修改真实设置。</summary>
    internal static (bool Ok, string Detail) Probe()
    {
        List<KeywordRule> stored = [new("双语", TitlePreference.Neutral)];
        var saves = 0;
        var row = new SettingTitleRulesRow("探针", null, () => stored, value => stored = [.. value], () => saves++);
        static string Dump(IEnumerable<KeywordRule> rules) => string.Join(",", rules.Select(rule => $"{rule.Term}:{rule.State}"));
        var seeded = saves == 0 && row.Rules.Count == 1 && row.Rules[0].Position == 1;
        row.Rules[0].SelectedState = null;
        row.Rules[0].SelectedState = "不存在的选项";
        var selectionCleared = saves == 0 && row.Rules[0].State == TitlePreference.Neutral;
        row.Rules[0].SelectedState = "候补";
        var changed = saves == 1 && Dump(stored) == "双语:Exclude";
        row.NewTerm = "特效";
        row.AddCommand.Execute(null);
        var added = saves == 2 && Dump(stored) == "双语:Exclude,特效:Neutral";
        row.NewTerm = " 特效 ";
        row.AddCommand.Execute(null);
        var noDupe = saves == 2 && row.Rules.Count == 2;
        row.Rules[1].UpCommand.Execute(null);
        var moved = saves == 3 && Dump(stored) == "特效:Neutral,双语:Exclude"
            && !row.Rules[0].CanUp && !row.Rules[1].CanDown;
        row.Rules[0].UpCommand.Execute(null);
        var boundary = saves == 3;
        row.BeginReorder();
        var dragged = row.Rules[0];
        row.Rules.RemoveAt(0);
        var noIntermediateSave = saves == 3 && stored.Count == 2;
        row.Rules.Add(dragged);
        row.EndReorder();
        var reordered = saves == 4 && Dump(stored) == "双语:Exclude,特效:Neutral";
        row.BeginReorder();
        row.EndReorder();
        var cancelled = saves == 4;
        row.Rules[0].RemoveSelfCommand.Execute(null);
        var removed = saves == 5 && Dump(stored) == "特效:Neutral" && !row.Rules[0].CanUp && !row.Rules[0].CanDown;
        var reloaded = new SettingTitleRulesRow("重入", null, () => stored, _ => { }, () => { });
        var persisted = reloaded.Rules[0].Term == "特效";
        var ok = seeded && selectionCleared && changed && added && noDupe && moved && boundary && noIntermediateSave
            && reordered && cancelled && removed && persisted;
        return (ok, $"装值={seeded}、临时清选择={selectionCleared}、改态度={changed}、添加={added}、去重={noDupe}、上移={moved}、"
            + $"边界={boundary}、拖拽中不写盘={noIntermediateSave}、最终排序={reordered}、取消={cancelled}、"
            + $"删除={removed}、重入={persisted}；写盘 {saves} 次");
    }
}

/// <summary>一条关键词及其优先／默认／候补态度，附带鼠标拖拽之外的键盘排序入口。</summary>
public sealed partial class TitleRuleChoice : ObservableObject
{
    private static readonly TitlePreference[] Order =
        [TitlePreference.Prefer, TitlePreference.Neutral, TitlePreference.Exclude];

    internal TitleRuleChoice(string term, TitlePreference state)
    {
        Term = term;
        State = state;
    }

    public string Term { get; }
    public IReadOnlyList<string> StateLabels { get; } = ["优先", "默认", "候补"];
    public string UpName => $"上移 {Term}";
    public string DownName => $"下移 {Term}";
    public string RemoveName => $"删除 {Term}";
    internal Action? Changed { get; set; }
    internal Action<TitleRuleChoice>? Remove { get; set; }
    internal Action<TitleRuleChoice, int>? Shift { get; set; }

    [ObservableProperty]
    public partial int Position { get; set; }

    [ObservableProperty]
    public partial bool CanUp { get; set; }

    [ObservableProperty]
    public partial bool CanDown { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedState))]
    public partial int StateIndex { get; set; }

    // ComboBox 在填充或回收 ItemsSource 时会先清空选择。用选项对象绑定并忽略这个 null，
    // 避免 SelectedIndex 的临时 -1 回写，把新加的「默认」误当成排在第一档的「优先」。
    public string? SelectedState
    {
        get => StateLabels[StateIndex is >= 0 and < 3 ? StateIndex : 1];
        set
        {
            if (value is null) return;
            for (var index = 0; index < StateLabels.Count; index++)
                if (StateLabels[index] == value)
                {
                    StateIndex = index;
                    return;
                }
        }
    }

    public TitlePreference State
    {
        get => Order[StateIndex is >= 0 and < 3 ? StateIndex : 1];
        set
        {
            var index = Array.IndexOf(Order, value);
            StateIndex = index >= 0 ? index : 1;
        }
    }

    partial void OnStateIndexChanged(int value) => Changed?.Invoke();

    [RelayCommand]
    private void Up() => Shift?.Invoke(this, -1);

    [RelayCommand]
    private void Down() => Shift?.Invoke(this, 1);

    [RelayCommand]
    private void RemoveSelf() => Remove?.Invoke(this);

    public override string ToString() => Term;
}
