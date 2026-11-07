using System.Net;
using Momoka.Emby;
using Momoka.Shell;

namespace Momoka.Tests
{
    internal static partial class Program
    {
        private static void RegisterCommandReads()
        {
            foreach (var status in new[] { HttpStatusCode.InternalServerError, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden })
                Test("R17-005-initial-query-" + status, async () =>
                {
                    var services = new CommandServiceProvider { Session = new() { Failure = new EmbyApiException("anonymous", status), FailAt = 1 } };
                    var result = await CommandReadsFixture.ProbeCommandsAsync(services);
                    Ensure(result is { Ok: false }, "failed prerequisite must not count as verified success");
                });
            Test("R17-005-later-timeout", async () =>
            {
                var services = new CommandServiceProvider { Session = new() { Failure = new TimeoutException("anonymous"), FailAt = 2 } };
                var result = await CommandReadsFixture.ProbeCommandsAsync(services);
                Ensure(result is { Ok: false }, "failed service step must not count as verified success");
            });
            Test("R17-005-no-fixture", async () =>
            {
                var result = await CommandReadsFixture.ProbeCommandsAsync(new CommandServiceProvider());
                Ensure(result is null, "no suitable fixture is explicitly unexecuted");
            });
            Test("R17-005-success", async () =>
            {
                var services = new CommandServiceProvider { Session = new() { HasFile = true } };
                var result = await CommandReadsFixture.ProbeCommandsAsync(services);
                Ensure(result is { Ok: true }, "successful service reads retain pass");
            });
            Test("R17-005-season-failure", () =>
            {
                SeasonReadsFixture.FailFromCurrentSource();
                var failures = 0;
                SeasonReadsFixture.ReportSeasons(new(), (_, ok, _) => { if (!ok) failures++; });
                Ensure(failures > 0, "failed season probe is not converted into skipped information");
                return Task.CompletedTask;
            });
        }
    }
}

namespace Momoka.Shell
{
    internal sealed class CommandServiceProvider : IServiceProvider
    {
        internal CommandSession Session { get; init; } = new();
        public object? GetService(Type serviceType) => throw new InvalidOperationException("only explicit session boundary is used");
    }
    internal sealed class CommandSession
    {
        internal bool IsSignedIn => true;
        internal bool HasFile { get; init; }
        internal int FailAt { get; init; }
        internal Exception? Failure { get; init; }
        private int _calls;
        internal Task<T> ExecuteAsync<T>(Func<EmbyClient, CancellationToken, Task<T>> read, CancellationToken token)
        {
            if (++_calls == FailAt) return Task.FromException<T>(Failure!);
            object result = typeof(T) == typeof(ItemsResult)
                ? new ItemsResult { Items = HasFile || FailAt > 0 ? [new() { Id = "anonymous", Type = EmbyItemType.Movie }] : [] }
                : typeof(T) == typeof(List<EmbyItem>) ? new List<EmbyItem>()
                : typeof(T) == typeof(RemoteImageResult) ? new RemoteImageResult()
                : throw new InvalidOperationException("unexpected fixture response " + typeof(T));
            return Task.FromResult((T)result);
        }
        internal Task ExecuteAsync(Func<EmbyClient, CancellationToken, Task> read, CancellationToken token)
        {
            if (++_calls == FailAt) return Task.FromException(Failure!);
            return Task.CompletedTask;
        }
    }
    internal static partial class SeasonReadsFixture
    {
        private sealed record SeasonState(string? Skipped, bool CrossOk = false, string Cross = "", bool? PickerOk = null, string Picker = "");
        private static SeasonState? _seasons;
        private const string Category = "fixture";
        internal static void FailFromCurrentSource() => _seasons = Failed(new TimeoutException("anonymous season read"));
    }
}
