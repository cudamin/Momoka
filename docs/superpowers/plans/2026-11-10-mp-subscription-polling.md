# MP subscription polling implementation plan

> **For agentic workers:** Execute the scoped Core task in its isolated worktree; the root implements Shell integration and performs final review.

**Goal:** Match official subscription-list conditional polling without moving existing cards during background reads.

**Architecture:** Core owns the batch endpoint, normalized signature and active-execution predicate. Shell VM serializes list reads; a page-owned dispatcher timer runs only for visible subscription list pages. Home/detail retain their separate behavior.

**Tech Stack:** Existing .NET 10, WinUI 3 and MoviePilot HTTP/session boundaries; no new dependencies.

**Spec:** [MP订阅列表按官方刷新](../specs/2026-11-10-mp-subscription-polling.md).

## Tasks

- [ ] Core: test then implement MoviePilotSubscriptionPolling signature/baseline/active-state policy and MoviePilotService.SubscriptionExecutionBatchesAsync(connectionStamp, token); anonymous endpoint and identity tests, commit separately.
- [ ] Shell VM: add failing actual-VM tests for silent list reads, failure retention, coalescing and explicit priority; implement serialized list reads and conditional batch refresh with separate in-flight guards.
- [ ] Page: attach 2500ms completion-based scheduling only to MoviePilotSubscriptionsPage; visibility/Release cancel or suspend correctly. Preserve home no-poll tests and add list lifecycle checks using actual owner code with inert framework clock boundaries.
- [ ] Verification: compile fresh Release Tests/Core/Shell/PageContract; run full Core, page contract, actual subscription VM and focused native refresh/visibility/layout probe. Inspect screenshots; no real service writes/playback.
- [ ] Documentation/delivery: update skills to current behavior, validate docs, integrate master without overwriting other work, verify final code and publish actual shortcut directory once available; no unsolicited remote push.

## Review focus

- Slow batches must not overlap or queue unbounded requests; failures preserve successful baselines.
- Slow full list reads coalesce and cannot be replaced by a late old identity.
- Background refresh leaves Busy/IsReady and existing card identity stable; explicit requests retain their visible feedback.
- Hidden windows/pages send no new requests, restored visibility checks promptly, released pages never revive.
- Search execution status is not import completion; unsupported/failed reads never invent an empty success.

## Execution status

Not implemented. The Core agent was stopped by a content filter while drafting an anonymous signature test; the following instruction explicitly prohibited retrying or using altered formatting, tools, or subagents to reproduce the blocked content. No Core or VM implementation was written. The independent page scheduler experiment passed 178/178 source-linked page tests (175/178 before the change), but the VM was a narrow substitute and the complete application could not compile against the missing integration method. Its three-file patch and logs are preserved under `work/mp-polling/`; those experimental changes were removed from tracked product/test files after inspecting the exact diff. This is not an application validation or delivery. Only the source findings, skills, this plan and status record are delivered.
