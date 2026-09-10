# Architecture and integrity review — 2026-09-10

## Remediation status

The five findings below describe the original review. All five now have local code/configuration fixes:

- Restricted Telegram users require `IsMember=true` in the shared membership verifier. Regression tests exercise both direct checks and community resolution.
- Super Admin discovery includes unavailable configured communities and reports `IsBotUnavailable` in the admin overview. Club/Camp UI shows a warning while retaining management and deletion. Normal administrators still require live Telegram authorization. Discovery-to-deletion tests preserve the stored binding and known chat through soft deletion.
- Mini App community discovery loads independently of capabilities and personal profile functionality. Pending or failed discovery never supplies a scoped community. The UI distinguishes unavailable discovery from a successful empty list, provides an isolated retry, and continues to gate administration. DOM tests exercise the real personal collection, slow/error responses, recovery, and admin denial.
- CI provisions disposable PostgreSQL 17. `OYINQ_REQUIRE_POSTGRES_TESTS=true` disallows silently missing configuration; `scripts/verify-postgres-tests.py` also rejects missing, skipped, or failed PostgreSQL results in the TRX report.
- `GatheringRecruitment.CandidatePredicate` now supplies the same expression to database candidate selection and in-memory eligibility/ranking. Both request and dispatch use `LoadCandidatesAsync`. Tests cover exact start/36-hour boundaries, the Camp override, and closed gathering exclusion.

Validation after the fixes: the backend suite passed 550 tests, with 21 PostgreSQL tests skipped locally; the subsequently expanded admin-overview test passed all three cases (one additional case). All 144 frontend tests passed, and TypeScript checking passed. The CI report guard was run against the local TRX report and correctly rejected all 21 skipped PostgreSQL tests. PostgreSQL and Docker are not installed locally, so database execution and container validation remain CI requirements. Nothing was deployed, and the obsolete production chat has not been removed.

The original probes were also rerun after the fixes: a restricted nonmember is now rejected, and Super Admin discovery now returns the unavailable community. The frontend production build passed. The original source line references below are retained as review evidence and may shift after remediation.

## Assessment and scope

The system has useful shared domain services, but changing a rule in one place does not yet reliably update every consumer. Five actionable findings remain below. This is a review of the local source tree, including the Camp recruitment and owned-game changes made in this session. It is not certification of the deployed service or its database, backups, networking, or recovery procedures.

Reviewed the gathering lifecycle and capacity rules, registration and game contribution rules, catalogue mapping, Mini App and Telegram authorization, recruitment request/dispatch paths, identity provisioning, startup, database migrations, and CI. A supplemental exact-block scan covered 197 production C#/TypeScript/JavaScript files. That scan detects literal duplication, not semantic equivalence or every possible defect.

No production code or deployed settings were changed as part of this review. Earlier requested fixes remain in the local tree.

## Findings

### 1. P1 — Restricted Telegram nonmembers pass the community membership check

**Source:** `Integrations/Telegram/TelegramCommunityMembershipVerifier.cs:47–50`; consumed by `Features/Communities/CommunityContextResolver.cs:37–42` and `:49–60`.

The verifier accepts every `ChatMemberStatus.Restricted` result. Telegram's restricted-member object also has `IsMember`; restricted status alone does not establish current membership. The installed Telegram.Bot package documents this field as whether the user is currently a member.

A focused executable probe using the real verifier and a fake Telegram HTTP response returned `True` for `status=restricted, is_member=false`. Therefore, a validly authenticated user with this Telegram response can pass the community membership gate. Other action-specific permissions may still apply, but this gate itself is incorrect.

**Recommendation:** Handle restricted users explicitly through `ChatMemberRestricted.IsMember` in the shared verifier. Cover true and false values and a community-scoped endpoint in regression tests. Fix this first because it affects access control.

### 2. P2 — Super Admin permission and community discovery disagree

**Source:** `Features/Admin/AdminAuthorizationService.cs:83–85` and `:137–145`; `Features/MiniApp/AdminEndpoints.cs:156` onward.

`CanAdministerCommunityAsync` permits a Super Admin to administer an existing, undeleted community. Discovery first removes communities whose known chat record says the bot is absent, then evaluates Super Admin access. The admin endpoint filters displayed communities using those discovery results.

A focused in-memory probe confirmed both results for the same unavailable community: administration allowed, discovery returned zero entries. A Super Admin consequently cannot reach that community through the admin list to clean it up. This is relevant to the requested removal of chat `-1003214883850`; the probe used synthetic data and did not inspect or modify that production record.

**Recommendation:** Keep configured unavailable communities discoverable to Super Admins, with explicit availability information. Treat administrative management permission separately from the bot's ability to post in a chat. Test discovery, detail access, and deletion together.

### 3. P2 — One temporary membership failure blocks the whole Mini App

**Source:** `Features/Communities/CommunityContextResolver.cs:49–60`; `Integrations/Telegram/TelegramCommunityMembershipVerifier.cs:77–84`; `Features/MiniApp/MiniAppIdentityFilter.cs:18–22`; `MiniApp/src/app/App.tsx:28–38`.

Community bootstrap checks the active communities sequentially. A temporary Telegram failure for one check throws out of the entire list operation and becomes HTTP 503. The app loads `/communities` and `/capabilities` together and renders a global error on either failure, before it can render the global profile. Thus a transient community membership outage also blocks access to otherwise independent personal profile functionality.

This finding follows the source control flow; it was not reproduced against production. It describes an application consequence of the supplied transient Telegram error, not the underlying cause of the network reset.

**Recommendation:** Allow independent personal functionality to load when community discovery is unavailable. Represent failed membership checks as unknown/unavailable, keep community access closed, and provide a targeted retry. Do not turn an unavailable authorization check into permission or silently represent it as a successfully empty list. Test `/communities` returning 503 while personal profile APIs remain available.

### 4. P2 — CI does not run the PostgreSQL integrity tests

**Source:** `.github/workflows/dotnet-desktop.yml:10–29`; `oyinQ.Bot.Tests/PostgreSqlStabilizationTests.cs:22–27`.

The workflow runs `dotnet test` without a PostgreSQL service or `OYINQ_TEST_POSTGRES`. Tests marked `PostgreSqlFact` skip when that variable is absent. The local suite reported 20 skipped PostgreSQL tests. These include database-specific migration, locking, atomicity, and worker coordination coverage; successful in-memory tests do not establish those properties on PostgreSQL.

**Recommendation:** Add a disposable PostgreSQL service to CI, configure the test connection with database-creation privileges, and require the integration suite to execute rather than skip. Keep it isolated from production. Make successful migration and concurrency checks a deployment prerequisite.

### 5. P2 — Recruitment eligibility still has three copies

**Source:** `Features/Gatherings/GatheringRecruitment.cs:21–24`; `Features/Gatherings/RecruitmentDigestService.cs:66–69`; `Features/Gatherings/RecruitmentDigestWorker.cs:112–115`.

The date window and eligible statuses appear in the shared in-memory policy and separately in the request and worker database queries. They agree today, including the newly added Camp admin scope, but this is exactly the change-propagation risk raised in the review request. Changing the shared policy from 36 to 48 hours would not expand either database query: those candidates would be removed before reaching the policy.

**Recommendation:** Give recruitment candidate selection one shared query/policy owner used by both request and dispatch, with a server-translatable predicate. Keep ranking and message formatting downstream. Preserve the intentional differences between organizer reminders, Camp admin scope, cooldown, and queued-request expiration. Add boundary tests through both request and dispatch paths, not only the helper.

## Shared ownership that is already useful

| Concern | Existing shared owner | Review assessment |
| --- | --- | --- |
| Occupied seats and remaining capacity | `GatheringCapacity` | Includes organizer, confirmed participants, and guests; waitlist is separate. Keep all capacity changes here. |
| Gathering status and mutation rules | `GatheringLifecycle`, `GatheringRules` | Shared foundation for lifecycle validation and transitions. |
| Gathering list filtering | `GatheringListQuery`, reused by `ProfileGatheringQuery` | Avoids maintaining independent list/profile status filters. |
| Camp eligibility | `CampParticipationPolicy`, `CampOperatingWindow`, `CampContributionSelectionService` | Registration completeness, selected dates, ownership, and contribution changes have domain owners. |
| BGG data normalization and expansions | `BggGameMapper`, `GatheringExpansionSelection` | Shared backend transformations reduce entry-point differences. |
| Identity and community authorization | `ParticipantIdentityService`, `CommunityContextResolver`, admin authorization service | Central owners exist; findings 1–3 concern their behavior or how consumers compose them. |
| Delivery bookkeeping | Persisted notification/recruitment requests and worker state | Explicit delivery state, including uncertain delivery, is preferable to untracked automatic retries. PostgreSQL verification is still required. |

Startup awaits migrations before the host runs its workers. Readiness checks database connectivity. Webhook configuration includes secret verification, and startup chooses webhook or polling mode. These are useful source-level controls, but do not verify deployed configuration, rollback safety, or database restoration.

## Duplication to manage deliberately

- **C# and TypeScript player ranges:** Both languages implement range behavior. The shared JSON fixtures provide consistency checks; they do not make changes propagate automatically. Extend fixtures whenever the rule changes, or expose a canonical backend result where the UI does not need independent computation.
- **Provider presentation:** `GameProviderNotice`, the games page, and gathering details contain overlapping presentation decisions. Prefer a shared presentation model/component for the same facts, with explicit options for each screen.
- **API contracts:** Handwritten frontend types and backend response shapes can drift. Generated contracts or focused response-contract tests would provide stronger protection than manually synchronized declarations.
- **Worker infrastructure:** The exact-block scan found repeated job reset/lease bookkeeping in Club import and metadata refresh services. Extract only mechanics whose semantics truly match; separate job workflows should not be merged merely to reduce line count.
- **Ownership, bringing, and snapshots:** These are intentionally different concepts. Owning a game must not silently promise to bring it. Historical gathering snapshots should not change whenever catalogue metadata changes. These differences should remain explicit.

## Evidence and limits

The last completed validation of the current production-code tree in this session was:

- Backend Release build: 0 warnings, 0 errors.
- Backend tests: 538 passed, 20 PostgreSQL tests skipped.
- Frontend tests: 138 passed across 33 files.
- Frontend typecheck and production build: passed.

Production code did not change during this review, so those suites were not repeated solely for documentation. Two additional focused probes were executed from `../../review-probes/ReviewProbes.csproj`:

```text
Restricted user with is_member=false accepted: True
Super Admin can administer unavailable community: True
Super Admin discovery entries for unavailable community: 0
```

These synthetic probes reproduce findings 1 and 2 without calling Telegram or modifying production. Findings 3 and 5 are source-path analyses. Finding 4 is established from the workflow, skip condition, and local test outcome.

## Recommended order

1. Correct restricted-user membership authorization and add regressions.
2. Enable required PostgreSQL integration coverage in CI.
3. Align Super Admin discovery with management permission, then remove the obsolete community through the normal administrative lifecycle.
4. Decouple global personal functionality from community discovery failures.
5. Consolidate recruitment selection, then shared presentation and API contracts incrementally.

The repository's foundations support these targeted changes. A broad rewrite would add risk without addressing the demonstrated problems more directly.
