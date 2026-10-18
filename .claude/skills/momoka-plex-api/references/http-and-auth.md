# Transport, credentials and errors

`PlexHttp` is the only place this client performs HTTP for Plex. Nothing else in `Momoka.Core` may open a
socket for it, and no caller may build a request URI by hand.

## Headers

| Header | Sent when | Why |
| --- | --- | --- |
| `X-Plex-Token` | A token is available | The credential. Never a query string, never a log line, never a launch argument. |
| `X-Plex-Client-Identifier` | Always, when known | Stable across restarts, so the server attributes a stream to this device instead of inventing a player per launch. Comes from `DeviceIdentity.DeviceId`. |
| `X-Plex-Product`, `X-Plex-Platform`, `X-Plex-Version` | Always | Plex's own product identification. |
| `Accept: application/json` | Always | Plex defaults to XML; JSON is what this client parses. |

`MediaServerHeaders.Create(device, token, MediaServerKind.Plex)` returns the token and the client
identifier for **playback** — that list becomes mpv's `--http-header-fields`, so the file is fetched with
the same credentials and no query string.

The transport sends a token only when one is given. `GET /identity` is called with `PlexCall.Anonymous`,
and the test suite pins that no token reaches it.

## Where a request may go

- Server requests stay on the server's own origin, and `PlexEndpoint.Api` / `Special` refuse a relative
  path that would leave it or climb out of the base path.
- Redirects are followed at most three times, and **only** within the same scheme, host and port. A
  redirect that changes origin ends the request instead of replaying the token at the new host. A
  non-`GET` redirect that is not 307/308 is refused rather than replayed with a changed method.
- The one request that leaves the server's origin is the account sign-in. Its URI is built inside
  `PlexHttp` from the `AccountAuthority` constant, so it cannot be redirected by a caller, and its
  password travels in the body.

## Credentials at rest

The Plex account password and the resulting token go into the existing DPAPI vault
(`CredentialVault` / `ISecretProtector`) under the same `AccountProfile` as every other backend. There is
no Plex-specific store. The token is written to `AccountProfile.ProtectedAccessToken`; the password to
`ProtectedPassword` when 「记住密码」 is on.

A Plex server does not describe its users, so the account id and name are carried on
`PlexConnection` (`EmbyConnection.UserId` / `UserName`) rather than fetched. Restore validates them by
calling the server root with the saved token: a 401 there means the token is no longer good for that
server, which is the same contract `EmbyTokenExpiredException` has for Emby.

## Errors

| Condition | Raised | Message policy |
| --- | --- | --- |
| 401 | `PlexApiException` with `IsTokenExpired` | Locally generated; the session re-authenticates once, then `PlexMediaAdapter.Translate` turns it into `EmbyTokenExpiredException`. |
| 403 | `PlexApiException` with `IsAuthenticationFailure` | Locally generated. |
| 404 | `PlexApiException` | Locally generated; the path is not echoed. |
| Other non-2xx | `PlexApiException` with the status only | The response body and `ReasonPhrase` may echo the request credentials, so neither is read into the message. |
| Timeout / connection failure | `PlexApiException` | 「连接 Plex 服务器超时」 / 「无法连接 Plex 服务器」. |
| Caller cancelled | `OperationCanceledException` | Preserved as cancellation, not translated into a transport failure. |

Server error bodies are never surfaced, logged or used to build a message. `PlexMediaTests` and
`PlexProtocolTests` pin this with a fixture that puts the token in both the body and the reason phrase.

## Response shape

- Server answers are wrapped in `MediaContainer`; `PlexClient.RootAsync` unwraps it. The account service
  answers with a bare object.
- Numeric and boolean attributes arrive as **strings** as often as numbers (`"viewCount": "0"`,
  `"default": "1"`). Read them through `Text` / `Number` / `Integer` / `Flag`, never with a typed
  deserialiser.
- The item array is named after its contents: `Metadata`, `Directory` or `Video`. Read all three.
- Paging is `X-Plex-Container-Start` / `X-Plex-Container-Size` **headers**, and the totals come back as
  `totalSize` / `size` on the container.
