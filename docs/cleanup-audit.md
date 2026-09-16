# Cleanup / consolidation audit — 2026-09-16

Baseline: working tree on `master`, HEAD `27721cd`, including the already prepared
shared-collection, copy/filter and atomic-refresh changes. Those changes were kept.
This pass adds no product feature and does not rewrite applied migrations.

## Entry points and ownership

| Area traced | Runtime owner / audit decision |
| --- | --- |
| Startup / DI | `Program.cs`: three explicit maintenance CLI branches; options, typed BGG client, scoped application services, migrations/bootstrap before HTTP/workers. EF's design-time factory remains required even though application code never calls it. |
| Telegram | Webhook parser and optional development polling both reach `TelegramUpdateHandler`. Commands, membership/chat migration tracking, peer-selection fallback and old Camp import callbacks are reachable. No abandoned conversation-state/wizard stack found. |
| HTTP | `MapMiniAppEndpoints` maps the endpoint groups under `MiniAppIdentityFilter`. Domain mutations retain server-owned identity, scoped community/admin checks and service validation. No HTTP route was deleted: `/games` supplies pickers, `/catalog` supplies filtered cards, and Camp import routes still serve saved legacy jobs. |
| Frontend | `main.tsx` → `App.tsx` → community/global profile/admin context → existing pages. `ClubCollection` and `CampParticipants` are substantial independent admin screens. No dynamic component loader depends on the removed helpers. |
| Provider data | `IBoardGameGeekClient` / `BoardGameGeekClient` own HTTP, `HttpRetryHelper` bounded status retries, `BggSelectionService` typed selection, `BggGameMapper` projection, `PlayerCountRange` limits. `ExternalSelectionAsync` returns both gathering snapshot and ownership projection from one provider selection. |
| Collections / wishes | `ClubCollectionService`, `ParticipantCollectionService`, `GameWishService` retain distinct ownership. `SharedCollectionReader` resolves Club/Camp source links; `GameCatalogService` / `EffectiveCampCatalogService` compose catalogs and contributions. These are not duplicate mutation models. |
| Gatherings / plays | Management, rules, capacity, access, lifecycle, list query, presentation and play confirmation retain their separate responsibilities. Collection revisions publish snapshots; play/outcome revisions guard explicit play edits. Worker progress does not update either counter. |
| Authorization / identity | `IAdminAuthorizationService`, `CommunityContextResolver`, membership verifiers and `ParticipantIdentityService` remain canonical. Telegram API interfaces are useful test/integration seams, not wrappers removed for appearance. |
| Dates | `CampOperatingWindow` owns UTC bounds, `CommunityTime` conversion, frontend `format` display and local inputs. Only a formatter with no production caller was removed. |
| EF model | Current foreign keys, indexes and navigation mappings were checked against consumers. Camp compatibility snapshots, exact registration dates, global ownership, historical play audit and persisted jobs remain. No evidence justified dropping columns/indexes or changing the migration chain. |

## Changes and blast radius

- Removed the unused `GatheringGameSelectionService.FromArbitraryBggAsync` wrapper.
  Its three tests now exercise `ExternalSelectionAsync`, the route used by production.
- Removed the short `HttpRetryHelper.SendAsync` overload used only in tests; those
  tests retain the same retry assertions against the production overload with
  separate accepted/transient budgets.
- Removed unused writers for `campimp:` callback data and `i-` / `c-` start parameters.
  Parsers remain: previously sent Telegram messages can still arrive. Tests use
  literal historical link fixtures instead of constructing them with dead writers.
- Centralized the three Club endpoint conflict responses in
  `MiniAppEndpointSupport.FromException`, preserving HTTP 409, `stale_revision`,
  message and `currentRevision`. Added a response-contract regression.
- Extracted collection management and Camp participant viewing into their own
  components. `AdminPage` is 917 lines instead of 1,211. Existing scoped requests,
  back navigation, state and authorization-dependent controls are retained.
- Both filter and add-game panels use `useMobileDialog` for viewport sizing,
  native dialog lifecycle, focus/scroll restoration and modal Telegram Back priority.
  Draft/filter/mutation logic stays with its screen.
- Removed unused date-input formatting and attendance badge helpers, unused imports
  and the unused `club` prop passed through catalog cards. Removed only assertions
  of deleted helper behavior; test cases for actual product behavior remain.
- Connected registration date toggling and submit validation to the already tested
  registration helpers instead of maintaining equivalent inline logic.
- Removed 31 selectors for 16 obsolete classes from old filter/picker layouts.
  Active selectors, responsive rules, CSS variables and current modal styles remain.
- Enabled TypeScript unused-local/parameter errors. Docker uses Node 24 like CI,
  publishes the backend once, and excludes local generated artifacts. The .NET
  project excludes the same artifact folders from default items.
- Added PostgreSQL 17 to CI so integration tests do not silently skip, and added
  the existing embedded-release-input validation step. CI credentials are disposable
  test values, not deployment secrets.

## Dependencies, configuration and logging

No package was removed. The application directly uses EF Core, its design-time
tools, Npgsql and Telegram.Bot. Tests require EF relational/InMemory and xUnit/test
SDK tooling. Frontend dependencies are React/React DOM plus TypeScript, Vite,
React plugin/types, Vitest and jsdom; all have direct build/test consumers.
Explicit EF version references were kept rather than relying accidentally on a
transitive version. No framework or infrastructure was added.

No runtime environment variable was removed or relocated. Every current options
property has a consumer. README now distinguishes credentials from ordinary
environment configuration and fixed defaults. Missing DB/token/HTTPS origin or
webhook secret already fail during startup; BGG remains optional. Bootstrap JSON
and the single legacy owner ID fallback are deliberate compatibility, not unused
settings. Camp dates, timezones, recruitment cooldowns and notification preferences
belong to the database, not new deployment variables.

Removed a duplicate warning and unused logger dependency from the message cleanup
processor: the deletion handler already logs the same failed attempt with chat and
message identity. Startup/migration summaries, integration errors, state transitions
and job failures remain structured. No live secrets or deployment values were read. The
common error mapper still rethrows unexpected exceptions; broad worker catches
persist failure or log it rather than silently discarding work.

## Workers retained separately

| Worker | Purpose / existing safeguards retained |
| --- | --- |
| Camp/profile BGG import | Persisted draft, ownership validation, lease, cancellation and saved confirmation result. |
| Club BGG import | Leased import, current-document merge and atomic completion. |
| Club metadata refresh | Persistent staging, lease fencing, final locked publication; +0 for identical content. |
| Camp lifecycle | Exact end boundary and locked close; does not close over future gatherings. |
| Gathering lifecycle | Locked scheduled-status transition, persisted notifications and post-commit publication. |
| Notification | Bounded drain, current eligibility checks, preferences, retry limit and DeliveryUnknown semantics. |
| Recruitment digest | Explicit request only, persistent cooldown, attempt token and send boundary. |
| Release announcements | Explicit confirmed recipients, durable preparation/delivery state and no automatic ambiguous resend. |
| Telegram message cleanup | Persisted targets, locked processing, one-minute retry spacing; already-deleted messages succeed. Retry lifetime has no total-attempt cap (see below). |
| Telegram setup / polling | Profile setup plus webhook or development receiver; startup retries and cancellation retained. |

Timers are wake-ups for persisted work. Workers were not merged into a generic
runner because their transaction and delivery semantics differ. Existing tests
cover lease recovery, concurrent operations, migration replay, waitlists and
notification eligibility; this audit is not a production multi-instance soak test.

## Deliberately retained / deferred

- Historical migration files, Camp `BaseCollectionJson`, nullable import `CampId`,
  legacy JSON upgrades, `DaysStaying` display compatibility and
  `LegacyPlayOutcomeJson` audit. Deletion needs a production data-retention plan.
- Old start parameters, `tab=mine`, import storage keys, `/oyinq` routing alias,
  callback parser and Camp-specific import endpoints: already issued links/jobs
  remain supported. No safe retirement date was established.
- Existing DTO compatibility fields and older read endpoints without a proven
  retirement contract. Absence from one current screen does not prove external
  cached clients no longer use them.
- Remaining community forms in `AdminPage`, compact gathering/profile page code,
  and service-specific polling/error loops. Further splitting would add files
  without a demonstrated benefit for this pass.
- API/service interfaces and independent workers are retained where they isolate
  external IO, EF scope or meaningful transaction boundaries.
- Message cleanup retries failed deletion indefinitely with one-minute spacing.
  Dropping an undelivered deletion after a fixed number of attempts would change
  recovery behavior; terminal-error/retention policy needs a separate decision.
- No major domain rename, new schema migration or configuration migration.

## Counts

Counts compare the working tree before and after this pass, not the much older
committed tree. Source counts exclude migrations and tests; frontend includes
declaration files, excludes CSS. A smaller central page and fewer duplicate rules
are the goal, not fewer files at any cost.

| Metric | Before | After |
| --- | ---: | ---: |
| Backend source files | 172 | 172 |
| Backend source lines | 15,288 | 15,243 |
| Frontend source files | 62 | 65 |
| Frontend source lines | 4,025 | 4,018 |
| AdminPage lines | 1,211 | 917 |
| EF migration/snapshot files | 29 | 29 |
| Application / test NuGet references | 4 / 6 | 4 / 6 |
| npm runtime / dev dependencies | 2 / 7 | 2 / 7 |
| Production CSS, uncompressed build | 53.41 kB | 50.68 kB |
| Backend build warnings | 0 | 0 |
| Frontend unused-code diagnostics | 4 | 0 |
| Backend tests | 571 | 572 |
| Frontend tests | 155 | 155 |

## Verification

All commands completed successfully against the final code:

```powershell
dotnet restore oyinQ.Bot.slnx
dotnet build oyinQ.Bot.slnx --configuration Release --no-restore
# OYINQ_TEST_POSTGRES set to the disposable local PostgreSQL 17 instance
dotnet test oyinQ.Bot.slnx --configuration Release --no-build --no-restore

Set-Location MiniApp
npm ci
npm run check
npm test -- --maxWorkers=2
npm run build
Set-Location ..

./scripts/verify-release-input.ps1
git diff --check
docker build -t oyinq-bot .
```

Backend: 572 passed, zero failed/skipped; Release build: zero warnings/errors.
Frontend: 155 passed in 37 files, strict typecheck and production bundle passed.
Two test workers keep this Windows machine stable while builds run. Dependency
installation reported zero npm vulnerabilities. No dependencies were upgraded.
The GitHub workflow itself was not dispatched; its commands were exercised locally.

Playwright drove the real Mini App against local API fixtures, including:

- Club and Camp catalogs/filter panel, wishes, profile, notification settings,
  Camp registration form, personal collection and calendar.
- Gathering creation, signup and waitlist presentation.
- Admin context switching, adding a game, existing-game no-op and exact expansion
  editing, deletion, BGG refresh completion and collection import completion.
- Club/Camp settings, administrator lists, Camp participant screen and gathering
  control screen. Widths 320–430 px plus desktop and a short viewport were checked.

All final scripts returned no unexpected API requests or uncaught page exceptions.
The intentionally mocked 503 scenario verified the error/retry screen. Initial
fixture issues (URL parsing/hidden details selectors) were corrected and rerun;
these were harness errors. Screenshots and scripts remain locally in ignored
`output/cleanup/`. Representative filter, collection-edit and participant screenshots
were visually inspected. Browser, Vite and the disposable PostgreSQL container
were shut down after verification.

These are fixture-backed browser checks and database-backed local regressions,
not live Telegram/BGG or production verification. Telegram commands/help,
publication formatting, callbacks, notification delivery transitions, Camp sharing,
contributions and waitlist concurrency are covered by automated tests. No real
messages were sent, no production data changed, and no release was deployed.

## Integration before commit

Before pushing, fetched `origin/master` at `e831ab6` and integrated its gathering,
BGG enrichment, complexity and profile-import changes. The filter panel retains
complexity, mechanics, duration and Camp date controls; preview counts and applied
queries share those values. Existing publication and recruitment migrations are
retained in both new migration target models. The upstream PostgreSQL CI gate is
preserved alongside the embedded-resource check.

Built and tested an export of the prepared Git index: 667 backend tests passed
with no skips, including the 34 checks required by the PostgreSQL integrity gate;
189 frontend tests passed across 45 files. Release build, strict TypeScript,
production bundle, release-input check and Docker build passed. Local Node 26
required `NODE_OPTIONS=--no-experimental-webstorage` for jsdom; Docker and CI use
Node 24. The disposable database used `127.0.0.1` to avoid Windows localhost
connection delays. Browser smoke checks above preceded this integration.
