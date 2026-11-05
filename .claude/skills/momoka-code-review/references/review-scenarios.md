# Review skill scenario checks

Use these cases when maintaining [the review skill](../SKILL.md). They are isolated fixtures, not reports about production code or proof of automatic invocation rates. Give the evaluator the skill and fixture; compare its decisions with the expectations below. Keep the exercise read-only and use no real accounts, desktop interaction or server mutations.

## A correct fix with no reason to refactor

Fixture: a list callback now checks the active page, captured account scope and request generation before updating the stable collection. Leaving advances the generation; entering a different account changes scope. Current tests cover late account responses, leaving the page and reverse request completion, and the applicable build passes. No runtime UI evidence is supplied. A read-only interface has one implementation and intentionally exposes no writes. Two display predicates use only page width. Backend request blocks look similar but have different authentication, routes and failures. The module has 700 lines, with no evidence of conflicting responsibilities or a performance problem. Earlier suggestions to remove the interface or add presentation services were declined for these reasons.

Expected: zero confirmed findings. Do not reopen declined suggestions, infer a defect from missing UI evidence or invent a refactor from file length. State the evidence boundary and separate review completion from applicable delivery checks.

## Cancellation with an already queued response

Fixture: account A's completed read queues an unconditional UI list replacement. Switching to B cancels the token but does not withdraw queued work. An isolated test executes that callback after the switch and observes B's list replaced by A's results. The user asks for review only and suggests removing identity checks to simplify the code. No final fix or fix verification exists.

Expected: report the established stale-response defect. Propose checking captured session/request identity when applying the result while preserving cancellation. Do not edit or claim that pre-fix tests establish a fix.

## A shared rule that already drifted

Fixture: two download entry points implement the same retry limit, but one uses 3 and the other 5. The explicit requirement is 3 for both; an isolated boundary test observes a fourth request from the second path. Protocols and business semantics match. The user asks whether to extract a general class. An unrelated one-implementation read-only interface remains necessary.

Expected: report the wrong limit. A shared constant or small named rule can have a current benefit; the fixture does not justify a general download framework or removal of the read-only interface. Verify the corrected boundary after a fix is authorized.

## Old evidence after conflict resolution

Fixture: merging a fix changes its result-application condition. The only successful tests and build report belong to the former branch; no current condition inspection, compilation or affected tests are available. The user asks whether it is complete.

Expected: identify a verification gap, not an invented functional defect. Inspect the final condition and obtain applicable final-code evidence before claiming delivery readiness.

## Discovery boundaries

Should trigger: "审查 Momoka 本次改动，宁可零发现也不要硬找问题"; "复审 Momoka 的修复，避免不必要的抽象"; "检查 Momoka 这个模块的实际缺陷".

Should not take over: implementing an ordinary Momoka feature; reviewing another repository; checking only whether a single API exists; evaluating skill installation. Follow the owning workflow for those requests.

Review discovery wording manually when the description changes. Measure actual automatic selection only through a supported host; these query examples alone do not establish a trigger rate.
