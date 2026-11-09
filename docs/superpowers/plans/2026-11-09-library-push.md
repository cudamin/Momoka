# Library push implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement task by task. Tasks below share one connection lifetime and are implemented together by the current writer; independent agents research and review.

**Goal:** Add Plex library push and keep Jellyfin's existing push connection alive.

**Architecture:** Preserve HomeViewModel and its 30-second reconciliation. Extend the existing realtime protocol/connection with Plex-specific parsing and one socket-owned Jellyfin heartbeat loop.

**Tech Stack:** C#, .NET 10, ClientWebSocket, current offline console test runner.

**Spec:** ../specs/2026-11-09-library-push-design.md

## Task 1: Protocol and actual connection

- [x] Extend EmbyRealtimeTests and transport/redirect fixtures: literal Plex paths and credential-free URL, NotificationContainer and legacy envelopes, 0/5/9 and LIBRARY_UPDATE, ignored progress/playing/invalid input, real source-key callback and reconnect with new token, Watch attach/release. Add Jellyfin immediate/periodic/update/reconnect heartbeat cases.
- [x] Restore/build current tests with Release, single-node, shared compilation and node reuse disabled; run `realtime-handshake-fixture` and record expected failures before product edits.
- [x] Add `PlexRealtimeProtocol.IsLibraryChanged(string)`; extend EmbyRealtimeProtocol Supports/Build and heartbeat interval parser; extend connection receive dispatch and socket-owned heartbeat lifecycle. Preserve Emby endpoint, fallback polling, identity checks and rejection of all redirects.
- [x] Rebuild, rerun realtime fixture and resource-budget websocket/compatibility coverage. Expected: exit 0, no failures.

## Task 2: Evidence and delivery checks

- [x] Record fixed official sources, hashes, local choices and FnNas limitations in docs/媒体库实时更新.md; update Plex support and test usage documentation.
- [x] Run current Release full main tests, relevant Shell compilation and offline lifecycle fixture; inspect diff and obtain one independent review for protocol, lifecycle and secret boundaries.
- [x] Resolve necessary findings, rerun affected checks, commit only this task's changes with project trailer.
- [x] Check main-tree ownership and integrate only when it can be done without interfering with current writers. Reverify final integrated code and publish by project policy; otherwise record concrete remaining work in PROGRESS.md and report the boundary.

## Review focus

Malformed/irrelevant frames must not refresh; Plex must never send a query token; redirects must not receive credentials; old heartbeat tasks must end before replacement/close; each callback must preserve original source identity. Offline assertions are not real server or rendered UI evidence.
