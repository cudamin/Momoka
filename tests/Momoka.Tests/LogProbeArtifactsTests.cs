using System.Globalization;
using Momoka.Diagnostics;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 钉住 <see cref="LogProbeArtifacts"/> 的保留口径：3 天、每类最新一次、不认识的条目一律不碰。
/// 只用临时目录和合成目录名，不读、不写真实 <c>%LOCALAPPDATA%\Momoka\logs</c>。
/// </summary>
internal static class LogProbeArtifactsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    public static void Register()
    {
        Test("探针产物：超过保留期的运行目录整棵删除，保留期内的不动", () => InTempDirectory(root =>
        {
            var stale = NewRun(root, "shell-probe", Now.AddDays(-4), 100);
            var fresh = NewRun(root, "shell-probe", Now.AddDays(-1), 200);

            Assert.Equal(1, LogProbeArtifacts.Prune(root, Now, TimeSpan.FromDays(3)));
            Assert.False(Directory.Exists(stale), "超过 3 天的运行目录该整棵删掉");
            Assert.True(Directory.Exists(fresh), "保留期内的截图和测量文件不能连坐");
        }));

        Test("探针产物：每类探针保留最新一次，即使它也已经过期", () => InTempDirectory(root =>
        {
            var newestOld = NewRun(root, "player-motion-probe", Now.AddDays(-10), 1);
            var older = NewRun(root, "player-motion-probe", Now.AddDays(-20), 2);
            var otherNewest = NewRun(root, "subtitle-probe", Now.AddDays(-9), 3);

            Assert.Equal(1, LogProbeArtifacts.Prune(root, Now, TimeSpan.FromDays(3)));
            Assert.False(Directory.Exists(older));
            Assert.True(Directory.Exists(newestOld), "每类最后一份证据要留下");
            Assert.True(Directory.Exists(otherNewest), "别的探针也各留最后一份");
        }));

        Test("探针产物：独占播放报告使用当前探针的三天保留规则", () => InTempDirectory(root =>
        {
            var expired = NewRun(root, "standalone-playback-probe", Now.AddDays(-5), 1);
            var latest = NewRun(root, "standalone-playback-probe", Now.AddDays(-4), 2);
            Assert.Equal(1, LogProbeArtifacts.Prune(root, Now, TimeSpan.FromDays(3)));
            Assert.False(Directory.Exists(expired));
            Assert.True(Directory.Exists(latest));
        }));

        Test("探针产物：不认识的名字、日志文件和用户保留的证据一律不碰", () => InTempDirectory(root =>
        {
            Directory.CreateDirectory(root);
            var gallery = Directory.CreateDirectory(Path.Combine(root, "gallery-frontend-source-20261114-741e")).FullName;
            var evidence = Directory.CreateDirectory(Path.Combine(root, "sidebar-preview-evidence-20261114-ed02")).FullName;
            var malformed = Directory.CreateDirectory(Path.Combine(root, "shell-probe-broken")).FullName;
            var stampOnly = Directory.CreateDirectory(Path.Combine(root, "shell-probe-20260101-000000000")).FullName;
            var shortStamp = Directory.CreateDirectory(Path.Combine(root, "shell-probe-20260101-00000000-7")).FullName;
            var logFile = Path.Combine(root, "app-20260101.log");
            File.WriteAllText(logFile, "真正的日志");

            Assert.Equal(0, LogProbeArtifacts.Prune(root, Now, TimeSpan.FromDays(3)));
            Assert.True(Directory.Exists(gallery));
            Assert.True(Directory.Exists(evidence));
            Assert.True(Directory.Exists(malformed));
            Assert.True(Directory.Exists(stampOnly));
            Assert.True(Directory.Exists(shortStamp));
            Assert.Equal("真正的日志", File.ReadAllText(logFile));
        }));

        Test("探针产物：trailer-preview 只回收过期子目录，最新一份和容器本身留下", () => InTempDirectory(root =>
        {
            var container = Directory.CreateDirectory(Path.Combine(root, "trailer-preview")).FullName;
            var stale = NewProfile(container, Now.AddDays(-5));
            var fresh = NewProfile(container, Now.AddDays(-1));

            Assert.Equal(1, LogProbeArtifacts.Prune(root, Now, TimeSpan.FromDays(3)));
            Assert.False(Directory.Exists(stale));
            Assert.True(Directory.Exists(fresh));
            Assert.True(Directory.Exists(container), "容器留给下次预告");
        }));

        Test("探针产物：删不动的目录不影响其他回收，解锁后下一轮补删", () => InTempDirectory(root =>
        {
            var recent = NewRun(root, "shell-probe", Now.AddDays(-1), 9);
            var locked = NewRun(root, "shell-probe", Now.AddDays(-5), 10);
            var deletable = NewRun(root, "shell-probe", Now.AddDays(-6), 11);
            var hold = new FileStream(Path.Combine(locked, "screenshots", "shot.png"), FileMode.Open, FileAccess.Read, FileShare.None);
            try
            {
                Assert.Equal(1, LogProbeArtifacts.Prune(root, Now, TimeSpan.FromDays(3)));
                Assert.True(Directory.Exists(locked), "被占用的目录这一轮删不掉");
                Assert.False(Directory.Exists(deletable), "不能因为一个失败就停下");
                Assert.True(Directory.Exists(recent));
            }
            finally
            {
                hold.Dispose();
            }

            Assert.Equal(1, LogProbeArtifacts.Prune(root, Now, TimeSpan.FromDays(3)));
            Assert.False(Directory.Exists(locked));
        }));

        Test("探针产物：单次上限按最老的先删，剩余的下次继续", () => InTempDirectory(root =>
        {
            var runs = Enumerable.Range(0, 5)
                .Select(index => NewRun(root, "shell-probe", Now.AddDays(-10 - index), 100 + index))
                .ToArray();

            Assert.Equal(2, LogProbeArtifacts.Prune(root, Now, TimeSpan.FromDays(3), maximumDeletes: 2));
            Assert.False(Directory.Exists(runs[4]), "最老的先删");
            Assert.False(Directory.Exists(runs[3]));
            Assert.True(Directory.Exists(runs[2]));
            Assert.Equal(2, LogProbeArtifacts.Prune(root, Now, TimeSpan.FromDays(3), maximumDeletes: 2));
            Assert.Equal(0, LogProbeArtifacts.Prune(root, Now, TimeSpan.FromDays(3), maximumDeletes: 2));
            Assert.True(Directory.Exists(runs[0]), "最新一次永远保留");
        }));

        Test("探针产物：目录还不存在时什么也不做", () =>
        {
            var missing = Path.Combine(Path.GetTempPath(), "Momoka-日志-不存在-" + Guid.NewGuid().ToString("N")[..8]);
            Assert.Equal(0, LogProbeArtifacts.Prune(missing, Now, TimeSpan.FromDays(3)));
        });
    }

    private static string NewRun(string root, string prefix, DateTimeOffset started, int pid)
    {
        var name = $"{prefix}-{started.UtcDateTime.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture)}-{pid}";
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(path, "screenshots"));
        File.WriteAllText(Path.Combine(path, "screenshots", "shot.png"), "png");
        return path;
    }

    private static string NewProfile(string container, DateTimeOffset lastWrite)
    {
        var path = Path.Combine(container, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(path, "profile"));
        File.WriteAllText(Path.Combine(path, "profile", "index.html"), "<html></html>");
        Directory.SetLastWriteTimeUtc(path, lastWrite.UtcDateTime);
        return path;
    }

    private static void InTempDirectory(Action<string> body)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Momoka-探针产物-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            body(directory);
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
