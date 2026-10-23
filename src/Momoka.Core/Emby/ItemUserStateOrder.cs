namespace Momoka.Emby;

/// <summary>Orders one item's confirmed user-state edits without serialising independent requests.</summary>
/// <remarks>
/// Watched owns Played and all progress/count/date fields; Favorite owns IsFavorite. A full response
/// may refresh the other group only when no edit of that group overlapped this request. There is no
/// server revision in the DTO, so an overlapping snapshot cannot overrule that group's own answer.
/// The owner calls Begin/Apply/Complete on its UI thread; this type does no IO or dispatching.
/// </remarks>
public sealed class ItemUserStateOrder
{
    private readonly FieldState _watched = new();
    private readonly FieldState _favorite = new();
    private long _version;

    public Change BeginWatched(bool played) => Begin(watched: true, played);
    public Change BeginFavorite(bool favorite) => Begin(watched: false, favorite);

    private Change Begin(bool watched, bool value)
    {
        var field = watched ? _watched : _favorite;
        var other = watched ? _favorite : _watched;
        var version = ++_version;
        field.Issued = version;
        field.Pending++;
        return new Change(version, watched, value, other.Pending > 0);
    }

    /// <summary>Returns a new merged state, or null if a later edit of the same field already succeeded.</summary>
    public EmbyUserData? Apply(Change change, EmbyUserData? current, EmbyUserData? response)
    {
        var field = change.Watched ? _watched : _favorite;
        var other = change.Watched ? _favorite : _watched;
        if (change.Version <= field.Confirmed) return null;

        // Only successful own-field responses advance this watermark. A later failed request must
        // not suppress an earlier success, and a newer other-field snapshot is not its confirmation.
        field.Confirmed = change.Version;
        var state = Copy(current);
        if (response is null)
        {
            if (change.Watched)
            {
                state.Played = change.Value;
                if (!change.Value)
                {
                    state.PlaybackPositionTicks = 0;
                    state.PlayedPercentage = 0;
                }
            }
            else state.IsFavorite = change.Value;
            return state;
        }

        if (change.Watched) CopyWatched(response, state);
        else state.IsFavorite = response.IsFavorite;

        // Capture overlap both ways: an edit already pending at Begin, or one issued afterwards.
        // Keep this full-snapshot benefit for isolated/sequential edits, but never resurrect progress
        // from a favorite response that was in flight alongside a watched/unwatched edit.
        if (!change.OtherPending && other.Issued < change.Version)
        {
            if (change.Watched) state.IsFavorite = response.IsFavorite;
            else CopyWatched(response, state);
        }
        return state;
    }

    /// <summary>Always finish the edit, including failure/cancellation; failure does not confirm a value.</summary>
    public void Complete(Change change) => (change.Watched ? _watched : _favorite).Pending--;

    private static EmbyUserData Copy(EmbyUserData? source)
    {
        var state = new EmbyUserData();
        if (source is null) return state;
        CopyWatched(source, state);
        state.IsFavorite = source.IsFavorite;
        return state;
    }

    private static void CopyWatched(EmbyUserData source, EmbyUserData target)
    {
        target.Played = source.Played;
        target.PlaybackPositionTicks = source.PlaybackPositionTicks;
        target.PlayedPercentage = source.PlayedPercentage;
        target.PlayCount = source.PlayCount;
        target.UnplayedItemCount = source.UnplayedItemCount;
        target.LastPlayedDate = source.LastPlayedDate;
    }

    public readonly struct Change
    {
        internal Change(long version, bool watched, bool value, bool otherPending)
        {
            Version = version;
            Watched = watched;
            Value = value;
            OtherPending = otherPending;
        }
        internal long Version { get; }
        internal bool Watched { get; }
        internal bool Value { get; }
        internal bool OtherPending { get; }
    }

    private sealed class FieldState
    {
        public long Issued;
        public long Confirmed;
        public int Pending;
    }
}
