# OyinQ implementation guide

## Architecture

OyinQ is one ASP.NET Core .NET 10 application, one React Telegram Mini App, and one Telegram bot serving multiple PostgreSQL-backed communities. `BotMode` has exactly `Club` and `Camp`. Never branch on hard-coded chat IDs or duplicate identity, integrations, collections, or gathering stacks per mode.

The Mini App is the primary application UI. It owns Camp registration, Club collections, Camp contributions, catalog/search, gathering creation and participation, and all administration. Telegram is a thin adapter limited to `/start`, contextual deep links, `/admin` entry, prepared native peer selection and its DM fallback, group gathering announcements, and notifications. Do not add reply-keyboard application menus or `game:`, `interest:`, `copy:`, `collection:`, `session:`, or `reg:` callback applications.
Bot profile setup runs in both webhook and long-polling modes. Private command scope is `/start`, `/menu`, `/help`, `/privacy`, `/admin`; group scope contains only `/oyinq`. `/menu` uses the same community-aware Mini App entry point as `/start`; `/admin` remains authorization-checked. Group `/oyinq` resolves the managed chat and uses a runtime `getMe()` username for the safe contextual private-chat deep link. Never hard-code a bot username.

Administration has two authorization roles. `Administration:SuperAdminTelegramUserIds` is the configuration-only global Super Admin allowlist; Super Admins can administer every known community and create new Club/Camp bindings without a Telegram group-admin requirement. Normal OyinQ Group Admins are persisted in `ChatAdminPermissions` for exactly one `OyinQCommunity`. Their effective access requires both an active permission row and a live Telegram `creator`/`administrator` result for that community chat on every protected request. Telegram group-admin status alone only permits discovery of the locked admin entry and never exposes statistics, participants, collections, gatherings, exports, configuration, or mutations. An effective Group Admin may grant or revoke normal permissions only for the same community and only to a current Telegram administrator; Super Admin assignment remains configuration-only. Never authorize from `OyinQAdministrators`, `Participant.ActiveCommunityKey`, hidden controls, or callback/route payloads without rechecking the target community server-side.

`KnownTelegramChats` is the finite discovery registry. Update it from real group messages and `my_chat_member`/`chat_member` updates, and backfill it from configured `OyinQCommunities`; never attempt Telegram-wide chat enumeration. Unconfigured known chats may appear only as locked identity/state entries. Registered Club/Camp identity remains `OyinQCommunity`, and permissions never inherit between a source Club and a Camp.

Runtime community resolution uses PostgreSQL through `ICommunityStore` and `CommunityContextResolver`. `CommunityBootstrap:CommunitiesJson` is optional one-time bootstrap input that only inserts missing rows. A fresh installation may use it; an existing installation must start without it. Never restore a single-chat fallback. Community modes in new configuration are only `Club` and `Camp`.

Telegram identity is global. `Participant.ActiveCommunityKey` is the last selected context, not authorization. Group links use `community-{key}`. Plain `/start` discovers authorized communities with `getChatMember`; contextual starts and Mini App APIs recheck membership. Mini App identity comes only from validated `Telegram.WebApp.initData`. Participant names, Camp cities, and Telegram contact targets use shared presentation rules; prefer a public username link and fall back to the Telegram user-ID mention target, while keeping Telegram user ID as authorization authority.

## Club, Camp, and gatherings

`OyinQCommunity` is the managed Telegram binding. `Club` and `Camp` are mode-qualified one-to-one rows with composite foreign keys and mode checks. Create subtype and community together.

Clubs have no registration gate. Camps use relational `(CampId, ParticipantId)` registration plus exact relational `CampRegistrationDays` within inclusive local `StartDate`/`EndDate`; `DaysStaying` is compatibility/derived display data, never attendance authority. A complete registration requires at least one exact date, an accommodation choice, and normalized non-empty `City`. Creating or joining a Camp gathering requires its local date to be selected. Removing a selected date withdraws the participant from affected future gatherings only after confirmation and promotes one eligible waitlisted participant transactionally; an organizer cannot remove a date while organizing an active future gathering. Club ownership is a versioned v2 OyinQ JSON document in `Club.CollectionJson`; every mutation locks the current document, checks `CollectionRevision`, and increments it. Camp base collections are Draft-only snapshots in `Camp.BaseCollectionJson` and become immutable with their `SourceClubId` after activation. Participant availability is relational `CampGameContributions`, with `Available` or `Bringing` commitment. Use the typed, versioned collection/contribution serializers. Expansion snapshots preserve every provider-reported parent in `ParentBggIds`, while `ParentBggId` remains a compatibility bridge. Never store raw provider JSON or accept client-authored provider snapshots.

The checked-in v2 Club recovery snapshot is refreshed only from positive BGG IDs found recursively in the four external source files `club.json`, `guests.json`, `john.json`, and `sergei.json`. The refresh must use typed BGG base/expansion requests for metadata and classification, deduplicate canonical IDs, attach expansions only through official included parents, report invalid/unresolved/orphan IDs, and serialize through `ClubCollectionSerializer`; never copy Tesera metadata into the OyinQ document. Runtime startup does not perform this refresh.

`ClubCollectionGame.HasExpansions` means that at least one actual, relevant expansion is present in that Club's collection document. Missing, null, and empty expansion arrays all mean no expansion availability. Mini App controls must use this semantic and must never infer availability from a field's existence or fetch arbitrary BGG expansions merely because a user opened a stored game. Club imports deduplicate official base-to-expansion relationships and must not create fake expansion availability.

New Camps start as `Draft`; only `Active` Camps before or on their local `EndDate` accept registrations, imports, contributions, gatherings, joins, or leaves. A worker closes expired Active Camps, but every mutation enforces the date boundary independently. Closing rejects future active gatherings; cancelling a Camp cancels its future gatherings after commit-aware notification/publication handling. `Closed` and `Cancelled` Camps remain readable to administrators. Existing migrated Camps without dates remain readable but must receive dates before new mutations. A community timezone is immutable after its first gathering.

Personal BGG imports are Camp-only persisted jobs in `CampBggImports`. The hosted worker leases queued or stale-running jobs, and a partial unique index permits one active job per Camp/participant. The server owns the versioned selection draft and persisted confirmation result. Confirmation accepts only draft IDs plus selected base/expansion IDs; a retry returns the original result. Base-library soft duplicates can be kept or added as personal copies, but only from the persisted selected subset and only by the import owner. `CampGameContribution.Source` is `Legacy`, `BggImport`, or `Manual`; re-import replaces only BGG-import rows.

Both modes use `GameGathering`. New gatherings render from immutable `GameSnapshotJson`; `GameId` is a nullable compatibility bridge only. Camp browsing, details, and gathering creation share one effective projection of base snapshot plus contributions. `GatheringGameSelectionService` may use a selected collection item or transient BGG details but must not insert a global `Game`. Base games and expansions are independent selections.

Player limits include the organizer and satisfy `minimum <= desired <= maximum`. PostgreSQL transactions and row locks are the concurrency authority. Signups are unique; waitlists are ordered; leaving/giving up promotes exactly one eligible person in the same transaction. Capacity changes cannot silently displace confirmed participants.

An incomplete or invalid provider player range (null, zero, negative, or minimum greater than maximum) has one shared gathering fallback: minimum 1 and maximum 12. Apply it to the Mini App controls and the immutable gathering snapshot so warning copy, submitted values, validation, persistence, and later presentation cannot diverge.

Attendance is explicit: `Attended`, `NoShow`, `CancelledInAdvance`, or `Unknown`. Never infer no-shows, and never create no-shows for a cancelled gathering. Do not add karma, ratings, voting, or opaque reliability scores.

Gathering lists expose Upcoming and History. Due gatherings that meet minimum attendance become `Completed`; underfilled due gatherings are hard-deleted after their Telegram announcement is queued for cleanup, with organizer/confirmed-participant DMs sent after commit. This is failed-gathering cleanup, not cancellation history.

Gathering list semantics belong to `GatheringListQuery`, not Telegram callbacks or React filtering. Upcoming means `StartsAtUtc > now` plus `Recruiting`, `Ready`, `Full`, or `Closed`. Unfiltered History contains persisted `Completed` or `Cancelled` rows regardless of scheduled time, plus due active-status rows (`StartsAtUtc <= now`) during the short interval before the lifecycle worker completes or deletes them; the `completed` and `cancelled` history filters remain status-exact. History is ordered by scheduled time descending; Upcoming is ordered ascending, with `Id` as the stable pagination tie-breaker. Apply status/date filtering and ordering before pagination. Client view or history-filter changes reset to page 1 and must not reuse an in-flight response from the previous selection. Legacy `GameSession.SessionStatus` (`Recruiting`, `Full`, `Closed`) has no cancelled/history meaning and must not be used for current gathering views.

Persist and transport gathering instants as UTC. Interpret Mini App `datetime-local` values in the selected community's validated IANA timezone, and render or validate those values in the same timezone; never depend on the browser or server machine timezone.

## Integrations and background work

BGG is the only external board-game provider. `BoardGameGeek:ApiToken` is server-only and optional; missing credentials must degrade gracefully. BGG failures disable only operations that require new provider data, return concise local copy, and log technical details server-side; stored catalogs, contributions, and gatherings remain usable. Tests use fake HTTP and never call live BGG. Expansion relationships come only from inbound BGG `boardgameexpansion` links.
Rich collection metadata stores BGG taxonomy IDs and canonical labels. Russian presentation uses an ID-based translation catalog with canonical English fallback. Club metadata refresh is a persisted leased job; it preserves current membership and selected expansions and locks the current Club document before applying one item.
BGG name search is explicitly user-triggered, returns at most 25 lightweight ranked results, and fetches full game details only after selection. Do not add background search polling or recurring catalog synchronization.

Business code requests typed gathering notifications; Telegram is the delivery adapter. Persist time-based work in PostgreSQL using hosted services. Never schedule important work only with `Task.Delay`. Prefer editing one group announcement plus relevant DMs.

Gathering publication state is persisted on `GameGathering`. Domain mutations commit before Telegram delivery; failures are visible to the organizer and retryable. Waitlist promotion is returned from the transaction and notified after commit.

Mini App peer selection uses `PendingTelegramPeerSelections`, `savePreparedKeyboardButton`, and `WebApp.requestChat`. Requests are random, expiring, single-use, purpose-bound, and scoped to the initiating global administrator. Final APIs consume the server-side selection token and never accept raw Telegram user/chat IDs. Telegram user ID alone remains authorization authority; shared names/usernames are presentation data.

## Configuration

Use hierarchical .NET Options/configuration. Environment variables use `__`: `Database__ConnectionString`, `Telegram__Token`, `Telegram__WebhookSecret`, `Telegram__PublicBaseUrl`, and `BoardGameGeek__ApiToken`. `Administration__SuperAdminTelegramUserIds` is the explicit comma-separated Super Admin allowlist and must contain the owner account in production. A single legacy `Administration__BootstrapTelegramUserIds` value is accepted only as an owner-preserving compatibility fallback; multiple legacy IDs are never promoted implicitly. `OyinQAdministrators` is retained schema only and grants no runtime access. Stable defaults live in appsettings files. Development enables `Telegram:UseLongPolling`; production must use webhooks and must not set a long-polling deployment variable. Never commit secrets.
Community timezones use validated IANA identifiers. Mini App admin forms use a native selectable timezone list; do not return to unrestricted timezone text inputs or assume Telegram provides a native timezone picker.

Camp rows created from `CommunityBootstrap:CommunitiesJson` have no event dates, so they start as inactive drafts. Set their name, dates, and time zone in Mini App administration before activation.

`GET /privacy` is public and must never require Telegram `initData`. Main Mini App enablement, its `{Telegram:PublicBaseUrl}/app/` URL, previews, profile artwork, privacy URL, and native splash settings remain manual BotFather responsibilities; see `docs/botfather-setup.md`. Newly created Club/Camp bindings receive one best-effort group onboarding message after the database commit. Bootstrap/restart paths must not send it.

Northflank provides `PORT`; do not require it as application configuration. Keep Docker's local `8080` fallback.

## Retained legacy schema

Runtime code must not use legacy `GameSession`, `GameSessionParticipant`, `CollectionImport`, `GameInterest`, global `GameCopy`, `ParticipantConversationState`, legacy participant registration fields, or `GameGathering.GameId`. Their entities and EF mappings intentionally remain because production data was retained by migration `20260828183821_ClubCampContextsAndGatheringSnapshots`. Do not delete those tables/columns or enum values without a separately reviewed, deterministic production-data migration and backup. Historical migration/designer matches are expected.

`Club.BggUsername` is also retained only for migration compatibility. Runtime Club collection authority is PostgreSQL `CollectionJson`; never restore full-account Club BGG synchronization.

Admin statistics and CSV export use `CampRegistration`, Club/Camp collection documents, `CampGameContributions`, and `GameGathering`; never query the legacy tables for current reports.

## Verification

Review every EF migration and model snapshot. Run:

```powershell
dotnet restore oyinQ.Bot.slnx
dotnet build oyinQ.Bot.slnx --configuration Release --no-restore
dotnet test oyinQ.Bot.slnx --configuration Release --no-build --no-restore
Set-Location MiniApp
npm ci
npm run check
npm test
npm run build
Set-Location ..
docker build -t oyinq-bot .
```

Static and mocked checks do not prove live BGG, Telegram, Northflank, or database behavior.
