using Momoka.Shell;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class CompositionPositionReadinessTests
{
    public static void Register()
    {
        Test("合成探针首位置：零秒是有效位置，不额外延迟", () =>
        {
            var reads = 0;
            var result = CompositionPositionReadiness.WaitAsync(_ => { reads++; return Task.FromResult<double?>(0); },
                () => false, CancellationToken.None);
            Assert.True(result.IsCompletedSuccessfully);
            Assert.Equal(0d, result.Result);
            Assert.Equal(1, reads);
        });
        Test("合成探针首位置：null/NaN/负值后读取实际有效值", () => Run(async () =>
        {
            var values = new Queue<double?>([null, double.NaN, -1, 0.04]);
            var actual = await CompositionPositionReadiness.WaitAsync(_ => Task.FromResult(values.Dequeue()),
                () => false, CancellationToken.None, TimeSpan.FromSeconds(2));
            Assert.Equal(0.04, actual);
            Assert.Equal(0, values.Count);
        }));
        Test("合成探针首位置：永远未知必须截止失败", () => Run(async () =>
        {
            try
            {
                await CompositionPositionReadiness.WaitAsync(_ => Task.FromResult<double?>(null),
                    () => false, CancellationToken.None, TimeSpan.FromMilliseconds(20));
                throw new AssertionException("未知位置不能记为就绪");
            }
            catch (TimeoutException) { }
        }));
        Test("合成探针首位置：挂起getter也必须截止，不能绕过deadline", () => Run(async () =>
        {
            var blocked = new TaskCompletionSource<double?>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                await CompositionPositionReadiness.WaitAsync(_ => blocked.Task,
                    () => false, CancellationToken.None, TimeSpan.FromMilliseconds(20));
                throw new AssertionException("挂起getter不能记为就绪");
            }
            catch (TimeoutException) { }
            finally { blocked.TrySetResult(null); }
        }));
        Test("合成探针首位置：调用方取消优先且不再读取", () => Run(async () =>
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            var reads = 0;
            try
            {
                await CompositionPositionReadiness.WaitAsync(_ => { reads++; return Task.FromResult<double?>(3); },
                    () => false, canceled.Token);
                throw new AssertionException("取消不能返回位置");
            }
            catch (OperationCanceledException error) { Assert.Equal(canceled.Token, error.CancellationToken); }
            Assert.Equal(0, reads);
        }));
        Test("合成探针首位置：getter等待期间取消优先于就绪", () => Run(async () =>
        {
            using var canceled = new CancellationTokenSource();
            try
            {
                await CompositionPositionReadiness.WaitAsync(_ => { canceled.Cancel(); return Task.FromResult<double?>(3); },
                    () => false, canceled.Token);
                throw new AssertionException("迟到有效值不能盖掉取消");
            }
            catch (OperationCanceledException error) { Assert.Equal(canceled.Token, error.CancellationToken); }
        }));
        Test("合成探针首位置：EOF前后均失败，真实结束不能当就绪", () => Run(async () =>
        {
            var reads = 0;
            try
            {
                await CompositionPositionReadiness.WaitAsync(_ => { reads++; return Task.FromResult<double?>(0); },
                    () => true, CancellationToken.None);
                throw new AssertionException("EOF不能通过");
            }
            catch (InvalidOperationException) { }
            Assert.Equal(0, reads);
            var ended = false;
            try
            {
                await CompositionPositionReadiness.WaitAsync(_ => { ended = true; return Task.FromResult<double?>(0); },
                    () => ended, CancellationToken.None);
                throw new AssertionException("getter途中EOF不能通过");
            }
            catch (InvalidOperationException) { }
        }));
        Test("合成探针首位置：真实getter错误保留，不吞成未知重试", () => Run(async () =>
        {
            var reads = 0;
            var expected = new IOException("fixture read failure");
            try
            {
                await CompositionPositionReadiness.WaitAsync(_ => { reads++; return Task.FromException<double?>(expected); },
                    () => false, CancellationToken.None);
                throw new AssertionException("读错误不能变就绪");
            }
            catch (IOException actual) { Assert.True(ReferenceEquals(expected, actual)); }
            Assert.Equal(1, reads);
        }));
        Test("合成探针首位置：实际caller先等首位置，后续严格采样不变", () =>
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Momoka.sln"))) folder = folder.Parent;
            Assert.NotNull(folder);
            var source = File.ReadAllText(Path.Combine(folder!.FullName, "src/Momoka.Shell/CompositionPlaybackProbe.cs"));
            var ready = source.IndexOf("await CompositionPositionReadiness.WaitAsync(", StringComparison.Ordinal);
            var advance = source.IndexOf("await AdvanceAsync(\"起播\", 2)", StringComparison.Ordinal);
            Assert.True(ready >= 0 && advance > ready);
            Assert.Contains("handle.GetPositionAsync, () => playbackExit.IsCompleted, token", source);
            Assert.Contains("? value : throw new InvalidOperationException(\"无法读取有效播放位置\")", source);
            Assert.Contains("delta > 0.25", source);
        });
    }

    private static void Run(Func<Task> run) => run().GetAwaiter().GetResult();
}
