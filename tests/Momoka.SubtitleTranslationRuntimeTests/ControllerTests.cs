using System.Net;
using System.Text;
using Momoka.Configuration;
using Momoka.Playback;
using Momoka.Translation;
using Momoka.Shell.Services;

namespace Momoka.Tests
{
    internal static class ControllerTests
    {
        internal static async Task RegisterAsync()
        {
            foreach (var pending in new[] { false, true })
            {
                Exception? failure = null;
                try { await AppearanceAsync(pending); } catch (Exception error) { failure = error; }
                var recorded = failure;
                TestHarness.Test($"R19 外观编辑保留翻译请求和缓存（pending={pending}）", () =>
                {
                    if (recorded is not null) throw recorded;
                });
            }
        }
        private static async Task AppearanceAsync(bool pending)
        {
            var settings = new ReviewSettings();
            settings.Settings.SubtitleTranslation.Enabled = true;
            settings.Settings.SubtitleTranslation.ProtectedGoogleApiKey = "anonymous-placeholder";
            var session = new ReviewSession();
            var transport = new ReviewTransport { Hold = pending };
            using var http = new HttpClient(transport);
            using var client = new SubtitleTranslationClient(http);
            using var controller = new SubtitleTranslationController(new(session), settings, client,
                new SubtitleTranslationCredentials(new IdentityProtector()), new InlineDispatcher());
            controller.Attach();
            try
            {
                await Until(() => transport.Calls == 1 && (pending || session.Visible == "translated"));
                var clears = session.Clears;
                settings.Settings.SubtitleTranslation.FontSize = 40;
                Momoka.Shell.ShellPrefs.ApplySubtitleTranslation();
                settings.Settings.SubtitleTranslation.Position = 75;
                Momoka.Shell.ShellPrefs.ApplySubtitleTranslation();
                await Task.Delay(450);
                Assert.Equal(1, transport.Calls);
                Assert.False(transport.Token.IsCancellationRequested);
                Assert.Equal(clears, session.Clears);
                transport.Reply.TrySetResult(ReviewTransport.Success());
                await Until(() => session.Font == 40 && session.Position == 75 && session.Visible == "translated");
                Assert.Equal(1, transport.Calls);
                // A translation identity edit must still invalidate the old request/cache.
                settings.Settings.SubtitleTranslation.TargetLanguage = "en";
                Momoka.Shell.ShellPrefs.ApplySubtitleTranslation();
                await Until(() => transport.Calls == 2);
                Assert.True(session.Clears > clears);
            }
            finally { await controller.StopAsync(); }
        }
        private static async Task Until(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            while (!condition()) await Task.Delay(10, timeout.Token);
        }
        private sealed class ReviewTransport : HttpMessageHandler
        {
            internal int Calls;
            internal bool Hold;
            internal CancellationToken Token;
            internal TaskCompletionSource<HttpResponseMessage> Reply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Token = cancellationToken;
                var count = Interlocked.Increment(ref Calls);
                return Hold && count == 1 ? Reply.Task.WaitAsync(cancellationToken)
                    : Task.FromResult(count == 1 ? Success() : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }
            internal static HttpResponseMessage Success() => new(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":{\"translations\":[{\"translatedText\":\"translated\"}]}}", Encoding.UTF8, "application/json")
            };
        }
        private sealed class ReviewSession : ISubtitleTranslationPlaybackSession
        {
            public long Generation => 1;
            public bool IsCurrent => true;
            public CancellationToken Lifetime => CancellationToken.None;
            internal volatile string? Visible;
            internal int Font, Position, Clears;
            public Task<bool> EnsureOverlayAsync(string scriptPath, CancellationToken cancellationToken) => Task.FromResult(true);
            public Task<SubtitleTranslationCue?> ReadCueAsync(CancellationToken cancellationToken) => Task.FromResult<SubtitleTranslationCue?>(new("anonymous", "1", 1, 20));
            public Task<bool> ShowAsync(SubtitleTranslationCue cue, string translated, int fontSize, int position, CancellationToken cancellationToken)
            {
                Font = fontSize; Position = position; Visible = translated; return Task.FromResult(true);
            }
            public Task ClearAsync(CancellationToken cancellationToken) { Visible = null; Interlocked.Increment(ref Clears); return Task.CompletedTask; }
        }
        private sealed class ReviewSettings : Momoka.Shell.Services.ISettingsService { public AppSettings Settings { get; } = new(); }
        private sealed class IdentityProtector : ISecretProtector
        {
            public string Protect(string value) => value;
            public string Unprotect(string value) => value;
        }
        private sealed class InlineDispatcher : Momoka.Shell.Platform.IUiDispatcher { public void Run(Action action) => action(); }
    }
}
namespace Momoka.Shell.Services
{
    public interface ISettingsService { AppSettings Settings { get; } }
    public sealed class PlaybackService(ISubtitleTranslationPlaybackSession session)
    {
        public long Generation => session.Generation;
        public event Action<PlaybackUpdate<Momoka.Emby.EmbyItem?>>? NowPlayingUpdated;
        public ISubtitleTranslationPlaybackSession? CaptureSubtitleTranslationSession() => session;
        public void Pulse() => NowPlayingUpdated?.Invoke(new(Generation, null));
    }
}
namespace Momoka.Shell.Platform { public interface IUiDispatcher { void Run(Action action); } }
namespace Momoka.Shell
{
    public static class ShellPrefs
    {
        public static event Action? SubtitleTranslationChanged;
        public static void ApplySubtitleTranslation() => SubtitleTranslationChanged?.Invoke();
    }
}
