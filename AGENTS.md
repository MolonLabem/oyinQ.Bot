# OyinQ implementation guide

## Architecture

OyinQ is one ASP.NET Core .NET 10 application, one React Telegram Mini App, and one Telegram bot serving multiple PostgreSQL-backed communities. `BotMode` has exactly `Club` and `Camp`. Never branch on hard-coded chat IDs or duplicate identity, integrations, collections, or gathering stacks per mode.

The Mini App is the primary application UI. It owns Camp registration, Club collections, Camp contributions, catalog/search, gathering creation and participation, and all administration. Telegram is a thin adapter limited to `/start`, contextual deep links, `/admin` entry, prepared native peer selection and its DM fallback, group gathering announcements, and notifications. Do not add reply-keyboard application menus or `game:`, `interest:`, `copy:`, `collection:`, `session:`, or `reg:` callback applications.
The bot registers a default Mini App menu button plus `/start`, `/menu`, and `/admin` private-chat commands at webhook startup. `/menu` uses the same community-aware Mini App entry point as `/start`; `/admin` remains authorization-checked.

Runtime community resolution uses PostgreSQL through `ICommunityStore` and `CommunityContextResolver`. `CommunityBootstrap:CommunitiesJson` is optional one-time bootstrap input that only inserts missing rows. A fresh installation may use it; an existing installation must start without it. Never restore a single-chat fallback. Community modes in new configuration are only `Club` and `Camp`.

Telegram identity is global. `Participant.ActiveCommunityKey` is the last selected context, not authorization. Group links use `community-{key}`. Plain `/start` discovers authorized communities with `getChatMember`; contextual starts and Mini App APIs recheck membership. Mini App identity comes only from validated `Telegram.WebApp.initData`.

## Club, Camp, and gatherings

`OyinQCommunity` is the managed Telegram binding. `Club` and `Camp` are mode-qualified one-to-one rows with composite foreign keys and mode checks. Create subtype and community together.

Clubs have no registration gate. Camps use relational `(CampId, ParticipantId)` registration and inclusive local `StartDate`/`EndDate`. Club ownership is versioned OyinQ JSON in `Club.CollectionJson`; every mutation locks the current document, checks `CollectionRevision`, and increments it. Camp base collections are immutable-at-creation snapshots in `Camp.BaseCollectionJson`; participant availability is relational `CampGameContributions`. Use the typed, versioned collection/contribution serializers. Never store raw provider JSON or accept client-authored provider snapshots.

New Camps start as `Draft`; only `Active` Camps accept registrations, imports, contributions, gatherings, joins, or leaves. `Closed` and `Cancelled` Camps remain readable to administrators. Existing migrated Camps without dates remain readable but must receive dates before new mutations.

Personal BGG imports are Camp-only persisted jobs in `CampBggImports`. The hosted worker leases queued or stale-running jobs, and the server owns the versioned selection draft. Confirmation accepts only draft IDs plus selected base/expansion IDs. `CampGameContribution.Source` is `Legacy`, `BggImport`, or `Manual`; re-import replaces only BGG-import rows.

Both modes use `GameGathering`. New gatherings render from immutable `GameSnapshotJson`; `GameId` is a nullable compatibility bridge only. `GatheringGameSelectionService` may use a selected collection item or transient BGG details but must not insert a global `Game`. Base games and expansions are independent selections.

Player limits include the organizer and satisfy `minimum <= desired <= maximum`. PostgreSQL transactions and row locks are the concurrency authority. Signups are unique; waitlists are ordered; leaving/giving up promotes exactly one eligible person in the same transaction. Capacity changes cannot silently displace confirmed participants.

Attendance is explicit: `Attended`, `NoShow`, `CancelledInAdvance`, or `Unknown`. Never infer no-shows, and never create no-shows for a cancelled gathering. Do not add karma, ratings, voting, or opaque reliability scores.

## Integrations and background work

BGG is the only external board-game provider. `BoardGameGeek:ApiToken` is server-only and optional; missing credentials must degrade gracefully. Tests use fake HTTP and never call live BGG. Expansion relationships come only from inbound BGG `boardgameexpansion` links.
BGG name search is explicitly user-triggered, returns at most five lightweight results, and fetches full game details only after selection. Do not add background search polling or recurring catalog synchronization.

Business code requests typed gathering notifications; Telegram is the delivery adapter. Persist time-based work in PostgreSQL using hosted services. Never schedule important work only with `Task.Delay`. Prefer editing one group announcement plus relevant DMs.

Gathering publication state is persisted on `GameGathering`. Domain mutations commit before Telegram delivery; failures are visible to the organizer and retryable. Waitlist promotion is returned from the transaction and notified after commit.

Mini App peer selection uses `PendingTelegramPeerSelections`, `savePreparedKeyboardButton`, and `WebApp.requestChat`. Requests are random, expiring, single-use, purpose-bound, and scoped to the initiating global administrator. Final APIs consume the server-side selection token and never accept raw Telegram user/chat IDs. Telegram user ID alone remains authorization authority; shared names/usernames are presentation data.

## Configuration

Use hierarchical .NET Options/configuration. Environment variables use `__`: `Database__ConnectionString`, `Telegram__Token`, `Telegram__WebhookSecret`, `Telegram__PublicBaseUrl`, and `BoardGameGeek__ApiToken`. `Administration__BootstrapTelegramUserIds` is an optional comma-separated, one-time bootstrap value; startup inserts missing IDs, while `OyinQAdministrators` is the runtime authorization source and the Mini App manages membership. Remove bootstrap configuration after setup so deleted administrators are not restored on restart. Stable defaults live in appsettings files. Development enables `Telegram:UseLongPolling`; production must use webhooks and must not set a long-polling deployment variable. Never commit secrets.
Community timezones use validated IANA identifiers. Mini App admin forms use a native selectable timezone list; do not return to unrestricted timezone text inputs or assume Telegram provides a native timezone picker.

Camp rows created from `CommunityBootstrap:CommunitiesJson` have no event dates, so they start as inactive drafts. Set their name, dates, and time zone in Mini App administration before activation.

Northflank provides `PORT`; do not require it as application configuration. Keep Docker's local `8080` fallback.

## Retained legacy schema

Runtime code must not use legacy `GameSession`, `GameSessionParticipant`, `CollectionImport`, `GameInterest`, global `GameCopy`, legacy participant registration fields, or `GameGathering.GameId`. Their entities and EF mappings intentionally remain because production data was retained by migration `20260828183821_ClubCampContextsAndGatheringSnapshots`. Do not delete those tables/columns or enum values without a separately reviewed, deterministic production-data migration and backup. Historical migration/designer matches are expected.

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
npm run build
Set-Location ..
docker build -t oyinq-bot .
```

Static and mocked checks do not prove live BGG, Telegram, Northflank, or database behavior.
