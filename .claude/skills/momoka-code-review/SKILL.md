---
name: "momoka-code-review"
description: "Use when reviewing Momoka changes, a specified module, or fixes after earlier review, especially when the user wants to avoid overengineering, unnecessary abstractions, repeated findings, or endless review rounds. Also use for Momoka review requests phrased as 代码审查, 复审, 避免过度设计, or 不必要的抽象. Not for ordinary feature implementation or reviews of other repositories."
---

# Momoka Code Review

Find actionable defects and keep fixes proportionate. Prefer an honest zero-finding result to speculative criticism. This skill controls review judgment; [CLAUDE.md](../../../CLAUDE.md) and domain implementations remain authoritative. Current commands live in [docs/开发与验证.md](../../../docs/开发与验证.md).

## Establish scope and authority

Resolve the active checkout with `git rev-parse --show-toplevel` and inspect its status. Read that checkout's `CLAUDE.md`, including after context compaction. Load relevant repository domain skills; for C# or XAML read [momoka-winui-shell](../momoka-winui-shell/SKILL.md). General WinUI checklists do not override project choices.

Honor the requested diff, revision or module. For a diff, include relevant staged, unstaged and untracked changes. Resolve the comparison base from the request and actual Git state; do not guess `origin/main` or silently select a different tree. If scope is materially ambiguous, inspect useful context and ask one focused question.

Read the changed behavior through callers, state ownership, dependencies and tests. Expand only where evidence connects another path to this review. Report unrelated old defects separately. Treat earlier review conclusions as context, not proof about current code.

Default to review only. A review request does not authorize edits, commits, pushes or live server mutations. If the user also requests fixes, follow that authorization and current project verification rules; do not silently turn review suggestions into implementation.

## Decide what deserves a finding

A confirmed finding needs a verified location, a reachable trigger, expected versus actual behavior, a concrete consequence, and supporting evidence. Source flow, protocol implementation or an isolated fixture can establish a defect without causing real data loss. State limits; source inspection does not establish live server support or rendered UI behavior.

Use no finding quota or minimum number of comments. File length, unfamiliar code and a desire to be useful are not evidence. If a candidate lacks a supported trigger or consequence, inspect the relevant evidence or omit it; do not promote a possibility into a defect. Report an unverified concern only when concrete evidence points to a consequential unknown, with the missing condition and smallest useful check. Zero findings is a complete review result.

Classify before reporting:

| Classification | Required treatment |
| --- | --- |
| Confirmed defect | Prioritize by impact and propose the smallest complete correction |
| Unverified concern | Identify the missing condition and a bounded way to verify it |
| Optional improvement | Explain a current benefit and its cost; keep it separate from defects |
| Declined suggestion | Retain the reason; reopen only when new evidence changes it |

Check correctness, data and credential safety, async/session ownership, state updates and regression risks first. A concrete project constraint is not a subjective style preference. Investigate credible high-impact concerns instead of downgrading them to finish. The precision threshold must not suppress a defect that the evidence actually establishes.

## Keep abstraction proportional

For a proposed interface, service, base class or shared component, identify the current problem it solves and compare it with an existing boundary, local guard or small function. Describe the benefit and added indirection, state, configuration and testing cost. Choose a broader refactor only when evidence shows the local approach cannot solve the complete problem or meaningfully reduce an existing maintenance burden.

Useful reasons include shared business semantics that already drift, a present replacement need, an actual permission or lifecycle boundary, a test seam for important behavior, or conflicting responsibilities. Hypothetical future backends, file length, naming taste, duplicate counts and implementation counts alone are insufficient.

One implementation can justify an interface that narrows access. Preserve such boundaries, including existing settings and capability contracts, unless their purpose has genuinely disappeared. Similar backend requests can have different authentication, routes and failure semantics; verify semantics before sharing them.

Keep local presentation rules near the UI when project policy permits. Keep business rules testable without forcing code into Core solely because an existing test project references it. Reuse existing production paths. A minimal fix must preserve cancellation, identity checks, notifications, permissions and resource ownership; fewer lines are not proof of simplicity.

## Re-review and stop

On re-review, inspect the prior findings, their fixes and affected paths. Recheck dismissed suggestions only if new source, requirements, tests or runtime evidence changes the reasoning; name that evidence. Group repeated symptoms by root cause. Do not lower the review threshold so far that confirmed ordinary functional defects disappear.

End the review when the agreed scope is checked, every necessary defect has a clear disposition, and consequential unresolved concerns have been clarified. Optional preferences do not prolong the review. Never obtain closure by relabeling defects, removing required checks or weakening baselines.

Separate the review verdict from delivery readiness. Verification evidence must match final code, and post-merge checks follow `CLAUDE.md`. If required execution is missing, report the exact gap. Repeat passed checks only for new relevant changes, failures or unresolved concerns. Review count alone does not establish readiness.

## Report

Reply in Simplified Chinese. Lead with the decision and reviewed scope/version, then report necessary findings by impact. Each finding gives location, trigger, consequence, evidence and a minimal correction. Keep supported unverified concerns separate. Omit optional refactoring suggestions by default; include them when the user requests them or evidence establishes a significant current benefit, never to fill the report. Summarize actual verification and material gaps; omit empty sections and long code dumps.

With no necessary findings, say “在本轮范围内未发现需要处理的缺陷” and state any verification limits. Do not claim the code is defect-free or that unrun checks passed.

Example: a 700-line page with two size-based display predicates and a read-only settings interface has no demonstrated reason for new services or interface removal. An old-account response replacing the current list is a concrete defect; check the captured session/request at result application, test that race, and inspect any affected UI behavior required by project policy.

For skill maintenance, use the bounded cases in [references/review-scenarios.md](references/review-scenarios.md) to check these decisions without touching live data.
