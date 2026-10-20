namespace Momoka.Playback;

/// <summary>
/// 一个关键词的三种态度（「双语／特效这类词单独设 优先／默认／候补」，2026-09-22；「候补」原名「排除」，是软排除，
/// 名字改成候补更贴切）。<see cref="Neutral"/> 是 0，所以设置文件里没写这个字段、或写了个不认识的值时，落到「不
/// 生效」这一档，不会平白把一条顶上去或压下去。
/// <para>
/// 这套关键词优先级是通用的：字幕按<em>标题</em>筛（<see cref="TrackLanguagePriority"/> 之后那一关），视频版本按
/// <em>文件名</em>筛（多版本条目默认播哪一个），音轨按格式筛。三处同一套 <see cref="KeywordRule"/>／<see cref="KeywordFilter"/>。
/// </para>
/// </summary>
public enum TitlePreference
{
    /// <summary>默认：这个词不参与筛选。</summary>
    Neutral = 0,

    /// <summary>优先：含这个词的排到前头。</summary>
    Prefer = 1,

    /// <summary>
    /// 候补：轮到这条规则时，含这个词的候选排到后面（软排除 —— 同一批全是候补时仍留一条，不至于没得挑；
    /// 它只管自己这条规则，不抵消上方的优先词）。
    /// </summary>
    Exclude = 2
}

/// <summary>
/// 一条关键词规则：一个词加它的态度。存进设置文件、也是设置页那张表里的一项。用类而不是 record，是因为设置页
/// 要就地改它的 <see cref="State"/>，而 record 的值语义在这儿只会添乱。字幕标题、音轨格式、视频文件名三处共用。
/// </summary>
public sealed class KeywordRule
{
    public KeywordRule() { }

    public KeywordRule(string term, TitlePreference state)
    {
        Term = term;
        State = state;
    }

    /// <summary>要找的那个词，如字幕的「双语」「特效」，或文件名的「REMUX」「枪版」。</summary>
    public string Term { get; set; } = "";

    /// <summary>对含这个词的那一条的态度。</summary>
    public TitlePreference State { get; set; } = TitlePreference.Neutral;
}

/// <summary>
/// 由上到下比较关键词规则；第一条能区分两个候选的有效规则决定先后。
/// 默认词不参与，优先词让命中的候选胜出，候补词让未命中的候选胜出。
/// 下方规则只负责破同分，不能靠命中更多词越过上方规则；全是候补时仍保留最佳候选。
/// 字幕标题、音轨格式与视频文件名共用这套优先级。
/// </summary>
public static class KeywordFilter
{
    /// <summary>正数表示左边更优，负数表示右边更优；零交回调用方按默认轨／服务器次序定夺。</summary>
    public static int Compare(string? left, string? right, IReadOnlyList<KeywordRule>? rules)
    {
        if (rules is null) return 0;

        foreach (var rule in rules)
        {
            if (!IsEffective(rule)) continue;
            var leftMatches = Matches(left, rule.Term);
            var rightMatches = Matches(right, rule.Term);
            if (leftMatches == rightMatches) continue;

            var preferred = rule.State == TitlePreference.Prefer ? leftMatches : !leftMatches;
            return preferred ? 1 : -1;
        }
        return 0;
    }

    private static bool IsEffective(KeywordRule? rule) =>
        rule is { State: TitlePreference.Prefer or TitlePreference.Exclude }
        && !string.IsNullOrWhiteSpace(rule.Term);

    private static bool Matches(string? text, string term) =>
        !string.IsNullOrEmpty(text) && text.Contains(term, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 在用户规则末尾补上候补词；不重复、不覆盖用户态度，优先级也不越过用户显式规则。
    /// 字幕在简体中文优先时用它将繁体标题留作兜底。
    /// </summary>
    public static IReadOnlyList<KeywordRule> WithExclusions(IReadOnlyList<KeywordRule>? userRules, params string[] terms)
    {
        var rules = new List<KeywordRule>(userRules ?? []);
        foreach (var term in terms)
        {
            if (string.IsNullOrWhiteSpace(term)) continue;
            if (!rules.Any(rule => rule is not null && string.Equals(rule.Term, term, StringComparison.OrdinalIgnoreCase)))
                rules.Add(new KeywordRule(term, TitlePreference.Exclude));
        }
        return rules;
    }

    /// <summary>保留优先级最高的所有候选及原有次序；没有有效规则或仅有一个候选时原样返回。</summary>
    public static IReadOnlyList<T> Rank<T>(IReadOnlyList<T> pool, IReadOnlyList<KeywordRule>? rules, Func<T, string?> textOf)
    {
        if (rules is null || !rules.Any(IsEffective) || pool.Count <= 1) return pool;

        var bestText = textOf(pool[0]);
        var top = new List<T>(pool.Count) { pool[0] };
        for (var index = 1; index < pool.Count; index++)
        {
            var text = textOf(pool[index]);
            var comparison = Compare(text, bestText, rules);
            if (comparison < 0) continue;
            if (comparison > 0)
            {
                top.Clear();
                bestText = text;
            }
            top.Add(pool[index]);
        }
        return top.Count == pool.Count ? pool : top;
    }
}
