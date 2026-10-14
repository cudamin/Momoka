# Endpoint map: Momoka's Emby calls → Jellyfin 12.2.0

The left column is what `EmbyClient*.cs`, `EmbyServerGateway` and `EmbyUrl` call today, taken from [momoka-emby-api's endpoint list](../../momoka-emby-api/references/endpoints.md). The right column is the Jellyfin 12.2.0 route with the same purpose, from `work/jellyfin/openapi.json`. Paths are written with the specification's parameter names; the two products spell them differently (`{Id}` vs `{itemId}`, `{MediaSourceId}` vs `{routeMediaSourceId}`), which is a naming difference, not a routing one.

"Absent" means the shape does not exist in the 12.2.0 document — not that the server refuses it. See [Version drift](#version-drift-and-what-it-costs) for why that distinction matters.

## Browsing and items

| Momoka today | Jellyfin 12.2.0 | Note |
| --- | --- | --- |
| `GET /Users/{UserId}/Views` | `GET /UserViews` | `userId` is a query parameter |
| `GET /Users/{UserId}/Items` | `GET /Items` | `userId` query; see the `GetItems` behaviour change below |
| `GET /Users/{UserId}/Items/{Id}` | `GET /Items/{itemId}` | `userId` query |
| `GET /Users/{UserId}/Items/Resume` | `GET /UserItems/Resume` | `userId` query; the route name is `UserItems`, not `Items` |
| `GET /Users/{UserId}/Items/Latest` | `GET /Items/Latest` | `userId` query; returns a bare array of `BaseItemDto`, not a query-result envelope |
| `GET /Items` | `GET /Items` | unchanged; `OrderVersionsByAddedAsync` by `Ids` still works |
| `GET /Items/{Id}/Similar` | `GET /Items/{itemId}/Similar` | |
| `GET /Genres`, `GET /Years` | same | |
| `GET /Tags` | **absent** | Tags come back from `GET /Items/Filters` alongside genres and years |
| `GET /Shows/NextUp` | same | |
| `GET /Shows/{Id}/Seasons`, `/Episodes` | `GET /Shows/{seriesId}/Seasons`, `/Episodes` | |
| `POST /Items/{Id}` | `POST /Items/{itemId}` | |
| `POST /Items/{Id}/Refresh` | `POST /Items/{itemId}/Refresh` | |
| `DELETE /Items/{Id}` | `DELETE /Items/{itemId}` | Jellyfin also documents `DELETE /Items` with a body |
| `GET /Items/{Id}/Download` | `GET /Items/{itemId}/Download` | |
| `POST /Collections` | same | |
| `POST /Collections/{Id}/Items` | `POST /Collections/{collectionId}/Items` | |

Jellyfin also documents `GET /Items/Filters2` next to the legacy `GET /Items/Filters`; the latter is what Momoka's `GetFilterValuesAsync` matches.

## Images

| Momoka today | Jellyfin 12.2.0 | Note |
| --- | --- | --- |
| `GET` / `POST` / `DELETE` `/Items/{Id}/Images/{Type}` | same | |
| `GET` / `DELETE` `/Items/{Id}/Images/{Type}/{Index}` | same | |
| `GET /Items/{Id}/RemoteImages` | `GET /Items/{itemId}/RemoteImages` | |
| `GET /Images/Remote` | **absent** | The server-side proxy for a remote candidate has no documented Jellyfin equivalent; check `RemoteImages/Providers` and `RemoteImages/Download` before assuming one |
| `POST /Items/{Id}/RemoteImages/Download` | `POST /Items/{itemId}/RemoteImages/Download` | |
| `GET` / `POST` `/Users/{Id}/Images/Primary` | `GET` / `POST` `/UserImage` | `userId` query; the current user's image lives at a top-level route |
| `POST /Users/{Id}/Images/Primary/Delete` | `DELETE /UserImage` | `userId` query |

Jellyfin additionally documents a path-segment form, `GET /Items/{itemId}/Images/{imageType}/{imageIndex}/{tag}/{format}/{maxWidth}/{maxHeight}/{percentPlayed}/{unplayedCount}`, for clients that prefer not to use query parameters. Momoka's `EmbyUrl.Image` uses query parameters (`maxWidth`, `quality`, `tag`); the query form still exists in the document.

The **upload body** question from `momoka-emby-api` — raw bytes for item images, Base64 text for user avatars — is unchanged by the product: the same two code paths (`PostBytesAsync`, `EmbyClient.Users.cs`) are what a Jellyfin branch would reuse, and the same "the specification and the implementation disagree" caveat applies.

## Subtitles

| Momoka today | Jellyfin 12.2.0 | Note |
| --- | --- | --- |
| `GET /Items/{Id}/RemoteSearch/Subtitles/{Language}` | `GET /Items/{itemId}/RemoteSearch/Subtitles/{language}` | |
| `POST /Items/{Id}/RemoteSearch/Subtitles/{SubtitleId}` | `POST /Items/{itemId}/RemoteSearch/Subtitles/{subtitleId}` | |
| `DELETE /Items/{Id}/Subtitles/{Index}` | `DELETE /Videos/{itemId}/Subtitles/{index}` | Moves from `Items` to `Videos` |
| `GET /Videos/{Id}/{MediaSourceId}/Subtitles/{Index}/Stream.{Format}` | `GET /Videos/{routeItemId}/{routeMediaSourceId}/Subtitles/{routeIndex}/Stream.{routeFormat}` | Same shape, different parameter names |

Jellyfin also documents `POST /Videos/{itemId}/Subtitles` (upload an external subtitle), a `.../{routeStartPositionTicks}/Stream.{routeFormat}` variant, and `GET /Videos/{itemId}/{mediaSourceId}/Subtitles/{index}/subtitles.m3u8`.

The escaping rule from the Emby skill still holds: `{subtitleId}` is a plugin-made marker and must be `Uri.EscapeDataString`-escaped.

## Playback

| Momoka today | Jellyfin 12.2.0 | Note |
| --- | --- | --- |
| `GET /Videos/{Id}/stream` (+ `Static=true`, `MediaSourceId`) | same | The document lists `static` and `mediaSourceId` |
| `POST /Sessions/Playing` | same | |
| `POST /Sessions/Playing/Progress` | same | |
| `POST /Sessions/Playing/Stopped` | same | |

Jellyfin additionally documents `POST /Sessions/Playing/Ping` (the progress heartbeat needs a `playSessionId`), `POST /Sessions/Capabilities` and `/Sessions/Capabilities/Full`. A third-party compatibility note in the scratch tree reports that official clients call the capabilities route immediately after login and treat a 404 as fatal — worth knowing if progress reporting is added, and worth verifying rather than copying.

`PlaybackInfo` has the same status in both products: documented at `GET`/`POST /Items/{itemId}/PlaybackInfo`, and not used by this client.

## User state

| Momoka today | Jellyfin 12.2.0 | Note |
| --- | --- | --- |
| `POST` / `DELETE` `/Users/{UserId}/PlayedItems/{Id}` | `POST` / `DELETE` `/UserPlayedItems/{itemId}` | No user segment |
| `POST` / `DELETE` `/Users/{UserId}/FavoriteItems/{Id}` | `POST` / `DELETE` `/UserFavoriteItems/{itemId}` | No user segment |
| `POST /Users/{UserId}/Items/{Id}/HideFromResume` | **absent** | The nearest documented lever is `POST /UserItems/{itemId}/UserData` with `UpdateUserItemDataDto`; verify against the server before relying on it |

`UserItemDataDto` keeps its name and its fields (`Played`, `PlaybackPositionTicks`, `UnplayedItemCount`, `IsFavorite`, `Likes`, …) in Jellyfin, so the existing DTO and the "no body is not a failure" rule in `PostForJsonAsync` / `DeleteForJsonAsync` carry over.

Jellyfin also documents `GET`/`POST /UserItems/{itemId}/UserData` and `POST`/`DELETE /UserItems/{itemId}/Rating` (the like/dislike pair Momoka deliberately does not expose).

## Library management

| Momoka today | Jellyfin 12.2.0 | Note |
| --- | --- | --- |
| `GET /Library/VirtualFolders/Query` | **absent** | `GET /Library/VirtualFolders` returns the list directly, without the `StartIndex` / `Limit` / `TotalRecordCount` envelope |
| `POST /Library/VirtualFolders` | same | |
| `POST /Library/VirtualFolders/LibraryOptions` | same | |
| `POST /Library/VirtualFolders/Name` | same | |
| `POST /Library/VirtualFolders/Delete` | `DELETE /Library/VirtualFolders` | |
| `POST /Library/VirtualFolders/Paths` | same | |
| `POST /Library/VirtualFolders/Paths/Update` | same | |
| `POST /Library/VirtualFolders/Paths/Delete` | `DELETE /Library/VirtualFolders/Paths` | |
| `GET /Libraries/AvailableOptions` | same | |
| `GET /Library/MediaFolders` | same | |
| `GET /Library/SelectableMediaFolders` | **absent** | Used by the third-party client in `work/`, so it answers on at least some servers — undocumented here |
| `POST /Library/Refresh` | same | |
| `POST /Items/{libraryId}/Refresh` | `POST /Items/{itemId}/Refresh` | |
| `GET /ScheduledTasks` | same | |
| `POST /ScheduledTasks/Running/{Id}` | `POST /ScheduledTasks/Running/{taskId}` | |
| `POST /ScheduledTasks/Running/{Id}/Delete` | `DELETE /ScheduledTasks/Running/{taskId}` | The `POST …/Delete` alias is Emby's; Jellyfin documents the `DELETE` |
| `GET` / `POST` `/System/Configuration/{key}` | same | |

Jellyfin also documents `GET /Library/PhysicalPaths`, which has no Emby counterpart in this list.

## Environment browsing

| Momoka today | Jellyfin 12.2.0 | Note |
| --- | --- | --- |
| `GET /Environment/DefaultDirectoryBrowser` | same | |
| `GET /Environment/Drives` | same | |
| `GET /Environment/NetworkDevices` | **absent** | Network browsing is not exposed |
| `POST /Environment/DirectoryContents` | `GET /Environment/DirectoryContents` | The document lists `GET` only, with `path`, `includeFiles`, `includeDirectories`. Momoka sends credentials in the body; that form is not documented here |
| `POST /Environment/ValidatePath` | same | |
| `GET /Environment/ParentPath` | same | Still plain-text bytes rather than JSON |

## User management

| Momoka today | Jellyfin 12.2.0 | Note |
| --- | --- | --- |
| `POST /Users/AuthenticateByName` | same | |
| `GET /Users/Query` | `GET /Users` | No pagination parameters; returns the whole list |
| `GET /Users/Public` | same | |
| `POST /Users/New` | same | |
| `GET /Users/{Id}` | `GET /Users/{userId}` | |
| `POST /Users/{Id}` | **absent** | Editing is split: `POST /Users/{userId}/Policy`, `POST /Users/Configuration` (with `userId`) |
| `POST /Users/{Id}/Policy` | `POST /Users/{userId}/Policy` | |
| `POST /Users/{Id}/Password` | `POST /Users/Password` | `userId` query; body is `UpdateUserPassword` (`CurrentPw`, `NewPw`, `ResetPassword`) |
| `POST /Users/{Id}/Delete` | `DELETE /Users/{userId}` | |
| `POST /Users/{Id}/Configuration/Partial` | `POST /Users/Configuration` | `userId` query |
| `POST /Users/{sourceId}/CopyData` | **absent** | User-copy is an Emby feature |
| `GET /Users/CopyDataOptions` | **absent** | Same |
| `POST` / `DELETE` `/Users/{Id}/Connect/Link` | **absent** | Emby Connect has no Jellyfin equivalent |

Jellyfin also documents `GET /Users/Me` (the user behind the token), `POST /Users/AuthenticateWithQuickConnect`, `POST /Users/ForgotPassword` and `/ForgotPassword/Pin`.

`AuthenticationResult` keeps its name and shape (`User`, `SessionInfo`, `AccessToken`, `ServerId`), so the login response DTO carries over.

## System and notifications

| Momoka today | Jellyfin 12.2.0 | Note |
| --- | --- | --- |
| `GET /System/Info`, `/System/Info/Public` | same | `/System/Info` declares a stricter policy (`FirstTimeSetupOrIgnoreParentalControl`) than the default authorization; `/System/Info/Public` declares no `security` at all |
| `GET` / `POST` `/System/Configuration` | same | |
| `POST /System/Restart`, `/System/Shutdown` | same | |
| `GET /System/ActivityLog/Entries` | same | |
| `GET /Sessions` | same | |
| `GET /Localization/Cultures`, `/Countries` | same | |
| `GET /Notifications/Types`, `/Notifications/Services/Defaults`, `POST /Notifications/Services/Test` | **absent** | Jellyfin has no notification-services API |
| `GET /Notifications/Services`, `GET`/`POST`/`DELETE` `/Notifications/Services/Configured` | **absent** | These were already Emby specification gaps; on Jellyfin the whole area is gone |
| `GET /Users` (notification user list) | `GET /Users` | Present |
| `GET /Library/VirtualFolders` (notification library list) | same | Present |

Jellyfin also documents `/System/Info/Storage`, `/System/Endpoint`, `/System/Logs` and `/System/Ping`.

## No documented equivalent

Collecting the "absent" rows, these features have no route in the Jellyfin 12.2.0 document:

- Tag enumeration as its own endpoint (`/Tags`) — the values are still reachable through `/Items/Filters`.
- The remote-image proxy (`/Images/Remote`).
- Hiding an item from resume (`…/HideFromResume`).
- Paged virtual-folder listing (`/Library/VirtualFolders/Query`) and `/Library/SelectableMediaFolders`.
- Network-device browsing (`/Environment/NetworkDevices`), and the credentialed `POST` form of `/Environment/DirectoryContents`.
- Editing a user as a whole (`POST /Users/{userId}`), user copy (`CopyData`, `CopyDataOptions`) and Emby Connect.
- The entire notifications surface.
- `GET /Shows/Missing` — Jellyfin expresses missing episodes as a filter instead: `GET /Items` carries `isMissing` and `is4K` parameters. This is the exact inverse of the Emby situation recorded in `EmbyFilterBy`, where those two parameters are the ones Emby silently drops.

That last row is the reason this skill exists as a sibling rather than a paragraph: the same two words mean "the server ignores it" on one product and "this is the documented way" on the other.

## Version drift and what it costs

Jellyfin 12.0 (2026-09-07) is the release that moved the API surface this skill describes. What its notes say, verbatim where it matters:

- "Support for the legacy `/emby/` and `/mediabrowser/` addresses has been removed, and the deprecated way of signing in is now disabled, including on existing servers."
- "`GetItems` is now asynchronous and applies `recursive` when filters are requested, limited to requests that include `includeItemTypes`. The same query can return a different result set than it did on 10.11." — this lands directly on Momoka's main query in `ItemQuery`.
- "`ItemByName` responses are restricted and people are deduplicated."
- "Swashbuckle has been updated to v10, which changes the generated OpenAPI document. SDKs need to be regenerated." — so the *document* changed shape, not only the routes.
- "As a general reminder of our API support policy: if an endpoint is not listed in the OpenAPI specification it should not be used, and if an endpoint or parameter is marked obsolete it should not be used. Deprecations will normally be marked for an entire major release cycle before removal."

Removed routes named in the same notes: `POST /Users/{userId}/EasyPassword`, `GET /Items/{itemId}/CriticReviews`, `GET /Environment/NetworkShares`, `POST /System/MediaEncoder/Path`, `GET /LiveTv/Recordings/Groups/{groupId}`, and the `GET` alias of `/QuickConnect/Initiate`. Newly obsolete but still functional: `GetTrailers`, `GetArtists`, `GetAlbumArtists`, `GetArtistByName`, `GetMusicGenre`, the music-genre instant-mix routes, `GetRecordingsSeries` and the startup routes. The HLS controllers are hidden from the document.

### Why the legacy shapes are not a safe shortcut

A third-party Jellyfin client in `work/moviepilot-protocol-20260924-M8nFhS/backend-v2/app/modules/jellyfin/jellyfin.py` builds `Users/{user}/Items`, `Users/{user}/Items/Latest` and `Users/{user}/Items/Resume`, and sends `Authorization: MediaBrowser Token="…"`. It works against the servers it targets. So the Emby-shaped routes do still answer somewhere, and a "just try it" test on one server will not tell you which contract you are on.

The two readings are both true and both needed:

- **Observed**: a real client uses those paths, and Jellyfin's parser has kept the aliases.
- **Documented**: Jellyfin's stated policy is that unlisted endpoints must not be used, and 12.0 removed a batch of exactly this kind of leftover.

Choose the documented route, and when a feature has no documented route, record the server version you measured it against instead of quietly depending on the alias.

### Re-verifying the map

```bash
curl --fail --show-error --silent "http://host:8096/openapi.json" -o work/jellyfin-openapi.json
```

Then compare by structure, not by literal grep — parameter names differ between products and between Jellyfin versions, so a text search reports false gaps. Normalising every `{…}` segment to a placeholder before matching is enough to line the two documents up; that is how the tables above were produced.

The snapshot these tables come from is `work/jellyfin/openapi.json`, sha256 `bd31bb194443d1c57616fa0a8521e099b17d096b92794e471c0e0725c50949c8`, `info.version` `12.2.0`, 294 paths. Nothing in these tables has been exercised against a running Jellyfin server.
