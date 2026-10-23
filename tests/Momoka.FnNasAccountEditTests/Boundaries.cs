using System.Text.Json.Nodes;

// Only presentation enum values and the external session/service endpoint are substitutes.
// No production authentication/session/client, protector, config, file, HTTP or native API is loaded.
namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
}

namespace Microsoft.UI.Xaml.Controls
{
    public enum InfoBarSeverity { Informational, Success, Warning, Error }
}

namespace Momoka.Diagnostics
{
    internal static class Log
    {
        internal static void Warn(string category, string message, Exception? error = null)
        {
            // Failure feedback still executes the actual PageViewModel.Report/Failure.Describe.
            // Never create a file sink or expose even the anonymous password values in logs.
        }
    }
}

namespace Momoka.Emby
{
    public sealed class FixtureConnection(bool isFnNas)
    {
        public bool IsFnNas { get; } = isFnNas;
    }

    public sealed class FixtureFnNasConnection(string key)
    {
        public Uri ApiBase { get; } = new("https://" + key + ".invalid/v/");
    }

    public sealed class EmbySession
    {
        internal EmbySession()
        {
            Sources = [new(this, "source-a"), new(this, "source-b")];
            DefaultSource = Sources[0];
        }

        public IReadOnlyList<EmbySessionScope> Sources { get; }
        internal EmbySessionScope? DefaultSource { get; set; }
        public bool IsSignedIn => DefaultSource is { IsAvailable: true };
        public EmbySessionScope Capture() => DefaultSource ?? throw new InvalidOperationException("匿名来源未连接");

        // This is a narrow confirmation endpoint, NOT production ChangeFnNasPasswordAsync.
        // Dispatch checks cancellation once; a controlled acknowledged write can finish after Cancel.
        public Task ChangeFnNasPasswordAsync(EmbySessionScope scope, string password, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            scope.ThrowIfNotAvailable();
            return scope.Endpoint.WritePasswordAsync(password);
        }
    }

    public sealed class EmbySessionScope
    {
        private readonly EmbySession _owner;
        private readonly EmbyClient _client;

        internal EmbySessionScope(EmbySession owner, string key)
        {
            _owner = owner;
            Key = key;
            Endpoint = new(key);
            _client = new(Endpoint, key);
        }

        public string Key { get; }
        public bool IsAvailable { get; internal set; } = true;
        public bool IsCurrent => IsAvailable && ReferenceEquals(_owner.DefaultSource, this);
        public FixtureConnection Connection { get; internal set; } = new(true);
        internal FixtureEndpoint Endpoint { get; }
        internal TaskCompletionSource? BeforeExecute { get; set; }

        public void ThrowIfNotAvailable()
        {
            if (!IsAvailable) throw new OperationCanceledException("匿名来源已退出");
        }

        public void ThrowIfNotCurrent()
        {
            if (!IsCurrent) throw new OperationCanceledException("匿名默认来源已改变");
        }

        public async Task ExecuteAsync(Func<EmbyClient, CancellationToken, Task> action, CancellationToken token)
        {
            await EnterAsync(token);
            await action(_client, token);
        }

        public async Task<T> ExecuteAsync<T>(Func<EmbyClient, CancellationToken, Task<T>> action, CancellationToken token)
        {
            await EnterAsync(token);
            return await action(_client, token);
        }

        private async Task EnterAsync(CancellationToken token)
        {
            if (BeforeExecute is { } gate) await gate.Task;
            token.ThrowIfCancellationRequested();
            ThrowIfNotAvailable();
        }
    }

    public sealed class EmbyClient(FixtureEndpoint endpoint, string key)
    {
        public FixtureEndpoint FnNas { get; } = endpoint;
        public FixtureFnNasConnection FnNasConnection { get; } = new(key);
    }

    public sealed class FixtureEndpoint(string key)
    {
        internal List<FixtureWrite> Writes { get; } = [];
        internal int ConfirmedWrites { get; private set; }
        internal int UserReads { get; private set; }
        internal int ConfigurationReads { get; private set; }
        internal string Subtitle { get; set; } = key == "source-a" ? "zh-CN" : "de-DE";
        internal string Audio { get; set; } = key == "source-a" ? "en-US" : "fr-FR";

        public Task<JsonObject> GetUserAsync(FixtureFnNasConnection connection, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            UserReads++;
            return Task.FromResult(new JsonObject { ["lan"] = Subtitle, ["audio_lan"] = Audio });
        }

        public Task<JsonObject> GetConfigurationAsync(Uri apiBase, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ConfigurationReads++;
            return Task.FromResult(new JsonObject
            {
                ["supported_languages"] = new JsonArray("zh-CN", "en-US", "ja-JP", "ko-KR", "de-DE", "fr-FR")
            });
        }

        public Task UpdateUserInfoAsync(FixtureFnNasConnection connection, JsonObject body, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return SubmitAsync(new("preferences", null, (JsonObject)body.DeepClone()));
        }

        internal Task WritePasswordAsync(string password) => SubmitAsync(new("password", password, null));

        private async Task SubmitAsync(FixtureWrite request)
        {
            Writes.Add(request);
            // Deliberately ignore post-dispatch page cancellation. The actual VM/base must reject
            // late UI completion without reclassifying this acknowledged fake write as unsent.
            await request.Reply.Task;
            ConfirmedWrites++;
        }
    }

    internal sealed class FixtureWrite(string kind, string? password, JsonObject? body)
    {
        internal string Kind { get; } = kind;
        internal string? Password { get; } = password;
        internal JsonObject? Body { get; } = body;
        internal TaskCompletionSource Reply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Succeed() => Reply.SetResult();
        internal void Fail() => Reply.SetException(new InvalidOperationException("匿名确认失败"));
    }
}
