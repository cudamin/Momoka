using System.Text;

namespace Momoka.Mpv;

/// <summary>
/// Switching the 着色器档位 while a film is playing: which mpv properties to set, and to what. Pulled out of
/// <c>PlaybackService</c> so that the one place this is decided is also the place a test can reach —
/// 任务书 5.5 asks for a contract test over exactly this seam, and a test that re-implemented the rule instead
/// of calling it would pass while the real code drifted.
/// <para>
/// The rule in one sentence: <b>every name any chain can touch goes back to what this playback started with,
/// then the new chain writes its own.</b> The launch value rather than mpv's factory default, because 去色带 is
/// a visible setting and a chain switch must not quietly reset it — that is what
/// <see cref="ShaderGroupCatalog.NeutralOptions"/> is a list of names for rather than a list of values.
/// </para>
/// </summary>
public static class ShaderSwitch
{
    /// <summary>
    /// A restricted runtime config, so mpv coalesces UPDATE_VIDEO notifications for the whole chain.
    /// mpv's fixed-length quoting counts UTF-8 bytes and preserves quotes, # and Windows paths verbatim.
    /// Config parsing is line-based; never permit another option or section inside a value.
    /// </summary>
    public static string Configuration(IReadOnlyList<KeyValuePair<string, string>> options)
    {
        if (options.Count != ShaderGroupCatalog.NeutralOptions.Count
            || options[^1].Key != "glsl-shaders"
            || !options.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal)
                .SetEquals(ShaderGroupCatalog.NeutralOptions.Select(pair => pair.Key)))
            throw new ArgumentException("批量切链必须包含完整且唯一的受控画质参数", nameof(options));
        var text = new StringBuilder();
        foreach (var (name, value) in options)
        {
            if (value.IndexOfAny(['\r', '\n', '\0']) >= 0)
                throw new ArgumentException("画质参数不能包含换行或空字符", nameof(options));
            text.Append(name).Append("=%").Append(Encoding.UTF8.GetByteCount(value)).Append('%')
                .Append(value).Append('\n');
        }
        return text.ToString();
    }

    /// <summary>
    /// The properties to set, in a stable order, to put <paramref name="group"/> in force — or to take the
    /// chain off entirely when it is null.
    /// </summary>
    /// <param name="launch">
    /// The options this playback was launched with, in order, <b>including</b> the ones the launch chain
    /// contributed at the end.
    /// </param>
    /// <param name="launchChainOptions">
    /// How many entries at the end of <paramref name="launch"/> came from the launch chain. Everything before
    /// them is the baseline, the 画质预设 and the settings page — which is what a switch falls back to.
    /// </param>
    public static IReadOnlyList<KeyValuePair<string, string>> Options(
        IReadOnlyList<KeyValuePair<string, string>> launch,
        int launchChainOptions,
        ShaderGroup? group,
        string shaderRoot,
        string? profilesJson = null,
        IReadOnlyDictionary<string, string>? defaults = null)
    {
        var baseline = MpvProfiles.Expand(launch.Take(Math.Max(0, launch.Count - launchChainOptions)).ToArray(), profilesJson);
        var applied = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var (name, neutral) in ShaderGroupCatalog.NeutralOptions)
        {
            if (name != "glsl-shaders")
                Set(name, LaunchValue(name) ?? (defaults?.TryGetValue(name, out var fallback) == true ? fallback : neutral));
        }

        foreach (var (name, value) in group?.Options ?? []) Set(name, value);
        Set("glsl-shaders", group is null ? "" : MpvListValue.JoinFiles(group.ResolveShaderPaths(shaderRoot)));

        return [.. order.Select(name => new KeyValuePair<string, string>(name, applied[name]))];

        void Set(string name, string value)
        {
            if (!applied.ContainsKey(name)) order.Add(name);
            applied[name] = value;
        }

        // The value that was in force before the launch chain applied: the *last* entry for the name in the
        // part ahead of it, because that part is layered last-wins exactly as mpv resolves it. The old code in
        // PlaybackService took the first, which is only the same answer while nothing before the chain sets a
        // name twice — true today (the 画质预设 stopped naming the three scalers), and not a thing to rely on.
        string? LaunchValue(string name)
        {
            for (var index = baseline.Count - 1; index >= 0; index--)
            {
                if (string.Equals(baseline[index].Key, name, StringComparison.Ordinal)) return baseline[index].Value;
            }

            return null;
        }
    }
}
