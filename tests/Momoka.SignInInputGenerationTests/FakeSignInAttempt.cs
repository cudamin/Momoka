using Momoka.Configuration;
using Momoka.Emby;

namespace Momoka.Tests;

// An immutable receipt of the arguments the actual VM submitted. No identity comparison, generation,
// authentication, credential save or source selection is implemented by this endpoint.
internal sealed record FakeSignInSnapshot(
    string ServerId,
    string Address,
    MediaServerKind Kind,
    string AccountId,
    string AccountUsername,
    string Username,
    string Password,
    bool Remember);

internal sealed class FakeSignInAttempt
{
    private readonly TaskCompletionSource _confirmation = new();
    private Exception? _failure;
    private bool _returnWithoutCommit;

    internal FakeSignInAttempt(ServerProfile server, AccountProfile account, string password,
        string username, bool remember, CancellationToken token)
    {
        Snapshot = new(server.Id, server.Url, server.ServerKind, account.Id, account.Username,
            username, password, remember);
        Token = token;
    }

    internal FakeSignInSnapshot Snapshot { get; }
    internal CancellationToken Token { get; }
    internal bool CancellationObserved { get; private set; }
    internal bool? CancelledAtConfirmation { get; private set; }
    internal bool Completed { get; private set; }

    internal void Confirm() => _confirmation.TrySetResult();

    internal void Reject(string message)
    {
        _failure = new InvalidOperationException(message);
        Confirm();
    }

    // Deliberately faulty success-only return for the VM's post-await guard, not a session commit.
    // The endpoint still observes the supplied cancellation; this mode can never record an Adopt.
    internal void ReturnWithoutCommit()
    {
        _returnWithoutCommit = true;
        Confirm();
    }

    internal async Task<bool> AwaitConfirmationAsync()
    {
        using var registration = Token.Register(() => CancellationObserved = true);
        try
        {
            // A remote acknowledgement can arrive after local cancellation. Hold the receipt until
            // the test explicitly releases it, rather than completing immediately on Cancel.
            await _confirmation.Task.ConfigureAwait(true);
            CancelledAtConfirmation = Token.IsCancellationRequested;
            if (_failure is not null) throw _failure;
            if (_returnWithoutCommit) return false;

            // This is only the token-aware confirmation boundary. It must NOT look at VM input,
            // selected account, another request's ticket or production session state.
            Token.ThrowIfCancellationRequested();
            return true;
        }
        finally
        {
            Completed = true;
        }
    }
}
