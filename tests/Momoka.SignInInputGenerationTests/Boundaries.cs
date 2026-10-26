using Momoka.Tests;

// Tiny presentation/value/service substitutes in the production namespaces. Multiple namespaces are
// intentional for these boundaries. No production session, gateway, vault, configuration store,
// protector, authentication/restore/re-auth, HTTP, native library or application is referenced.
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
    internal static class Failure
    {
        // Classification is outside R-008; tests assert the actual VM's notice ownership, not R-004.
        internal static string Describe(Exception error) => error.Message;
    }

    internal static class Log
    {
        internal static void Warn(string category, string message, Exception? error = null) { }
        internal static void Info(string category, string message) { }
    }
}

namespace Momoka.Configuration
{
    public sealed class AccountProfile
    {
        public string Id { get; set; } = "";
        public string Username { get; set; } = "";
        public bool RememberPassword { get; set; } = true;
        public bool HasSavedPassword { get; set; }
    }

    public sealed class ServerProfile
    {
        public string Id { get; set; } = "";
        public string Url { get; set; } = "";
        public string Name { get; set; } = "";
        public Momoka.Emby.MediaServerKind ServerKind { get; set; }
        public string? ServerVersion { get; set; }
        public string? ServerId { get; set; }
        public List<AccountProfile> Accounts { get; } = [];
    }

    public sealed class AppSettings
    {
        public List<ServerProfile> Servers { get; } = [];
        public bool AggregateMode { get; set; }
        public ServerProfile? ResolveLastServer() => Servers.FirstOrDefault();
    }

    public sealed class CredentialVault
    {
        internal Dictionary<string, string> Values { get; } = [];
        internal int Reads { get; private set; }

        public string GetPassword(AccountProfile account)
        {
            Reads++;
            return Values[account.Id];
        }
    }
}

namespace Momoka.Services
{
    public interface ISettingsService
    {
        Momoka.Configuration.AppSettings Settings { get; }
        Momoka.Configuration.ServerProfile ResolveServer(string address);
        Momoka.Configuration.AccountProfile ResolveAccount(Momoka.Configuration.ServerProfile server, string username);
        void Save();
    }

    internal sealed class MemorySettings : ISettingsService
    {
        public Momoka.Configuration.AppSettings Settings { get; } = new();
        internal int Saves { get; private set; }

        // Resolution uses the actual pure input helpers, never a second identity implementation.
        public Momoka.Configuration.ServerProfile ResolveServer(string address) => Settings.Servers.Single(server =>
            Momoka.Emby.EmbySignInIdentity.SameAddress(server.Url, address, server.ServerKind));

        public Momoka.Configuration.AccountProfile ResolveAccount(Momoka.Configuration.ServerProfile server, string username) =>
            server.Accounts.Single(account => Momoka.Emby.EmbySignInIdentity.SameUsername(account.Username, username));

        public void Save() => Saves++;
    }
}

namespace Momoka.Emby
{
    public enum MediaServerKind { Emby, Jellyfin, FnNas, Plex }

    public sealed class EmbyUser
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public bool HasPassword { get; set; }
    }

    public sealed class PublicSystemInfo
    {
        public MediaServerKind ServerKind { get; set; }
        public Uri? ApiBase { get; set; }
        public string? ServerName { get; set; }
        public string? Version { get; set; }
        public string? Id { get; set; }
    }

    public sealed record EmbyDiscoveredServer
    {
        public string Url { get; init; } = "";
        public string Name { get; init; } = "";
        public string VersionLabel { get; init; } = "";
    }

    public sealed class EmbyServerDiscovery
    {
        public Task<IReadOnlyList<EmbyDiscoveredServer>> DiscoverAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("R-008禁止服务器发现/网络入口");
    }

    // Rebase is not exercised by the VM. Fail closed instead of loading real redirect/HTTP code.
    internal static class EmbyHttpRedirect
    {
        internal static void Validate(Uri address) => throw new InvalidOperationException("R-008禁止重定向入口");
        internal static bool SameOrigin(Uri first, Uri second) => throw new InvalidOperationException("R-008禁止重定向入口");
    }

    public sealed class EmbyServerGateway
    {
        internal PublicSystemInfo Info { get; set; } = new();
        internal List<EmbyUser> Users { get; } = [];
        internal List<CancellationToken> DetectTokens { get; } = [];
        internal TaskCompletionSource<PublicSystemInfo>? HeldDetection { get; set; }

        public Task<PublicSystemInfo> DetectAsync(string address, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            DetectTokens.Add(token);
            return HeldDetection?.Task ?? Task.FromResult(Info);
        }

        public Task<List<EmbyUser>> GetPublicUsersAsync(Uri apiBase, CancellationToken token, MediaServerKind kind)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Users.ToList());
        }
    }

    public sealed class EmbySession
    {
        public EmbyServerGateway Gateway { get; } = new();
        public bool IsSignedIn { get; private set; }
        internal List<FakeSignInAttempt> Attempts { get; } = [];
        internal List<FakeSignInSnapshot> Adopted { get; } = [];

        public Momoka.Configuration.AccountProfile? PreferredAccount(Momoka.Configuration.ServerProfile server) =>
            server.Accounts.FirstOrDefault();

        public Task SignInAsync(Momoka.Configuration.ServerProfile server, Momoka.Configuration.AccountProfile account,
            string password, string username, bool remember, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var attempt = new FakeSignInAttempt(server, account, password, username, remember, token);
            Attempts.Add(attempt);
            return RecordConfirmationAsync(attempt);
        }

        private async Task RecordConfirmationAsync(FakeSignInAttempt attempt)
        {
            if (!await attempt.AwaitConfirmationAsync().ConfigureAwait(true)) return;
            Adopted.Add(attempt.Snapshot);
            IsSignedIn = true;
        }
    }
}
