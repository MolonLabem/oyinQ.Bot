# OyinQ implementation guide

## Product and architecture

OyinQ is one ASP.NET Core application and one Telegram bot serving multiple configured chats. `BotMode` is either `Club` or `Camp`; do not branch on hard-coded chat IDs and do not duplicate identity, gathering, integration, or Telegram stacks per mode. `Gatherer` and `BoardCamp` remain enum aliases only so old bootstrap JSON can be deployed during the transition; new code and documentation must use `Club` and `Camp`.

`OYINQ_COMMUNITIES` is required bootstrap configuration only. Startup inserts missing rows into `OyinQCommunities`; runtime resolution and Mini App administration use PostgreSQL through `ICommunityStore`. `OyinQCommunity` is the managed Telegram-chat binding. `Club` and `Camp` are mode-qualified one-to-one rows: each has a composite foreign key to `(OyinQCommunity.Key, OyinQCommunity.Mode)` plus a database check constraint for its own mode. This is what prevents one Telegram chat from being both modes. Always create the subtype together with the chat row. `CommunityOptions` owns bootstrap validation and `CommunityContextResolver` is the only place that resolves a Telegram chat or context key. Never restore a single-chat context fallback.

Telegram user identity is global. `Participant.ActiveCommunityKey` is only the user's last selected context, not proof of authorization or a separate identity. A group launch uses `community-{key}` as the Telegram start parameter. Plain `/start` discovers all configured communities authorized through `getChatMember`; contextual starts and Mini App API calls recheck membership server-side. Never trust a frontend-provided user ID, chat ID, mode, or community key by itself. Mini App identity must come from validated `Telegram.WebApp.initData`.

Camp retains event registration, accommodation, collections/contributions, interests where applicable, administration, statistics, and exports. Club has no registration gate. `CampRegistration` is relational and scoped by `(CampId, ParticipantId)`; the legacy registration columns on `Participant` remain only for transition compatibility. Both modes use `GameGathering`; do not create another mode-specific recruitment aggregate.

## Current Club/Camp collection and migration architecture

`Club.CollectionJson` and `Camp.BaseCollectionJson` are PostgreSQL `jsonb` documents owned by OyinQ. Use `ClubCollectionDocument` and `ClubCollectionSerializer`; version 1 is the only supported version. Never store raw BGG or Tesera JSON. Camp creation copies the serialized Club document and optionally stores `SourceClubId` for provenance. It is snapshot semantics, not a live relation.

Participant Camp contributions live in `CampGameContributions`, unique by `(CampId, ParticipantId, BggId, ItemType)`. `CampContributionSelectionService` persists only selected rows and merges equal BGG items into one logical catalog result while retaining contributor IDs. Base games and expansions are independent selections. A selected expansion without its base is valid and the Mini App renders the informational `🟨` marker.

Every new `GameGathering` is rendered from `GameSnapshotJson`; `GameId` is nullable and exists only as a non-destructive bridge for legacy rows. `GatheringGameSelectionService` builds snapshots from the selected Club/Camp catalog or transient BGG details without inserting `Game` rows. Do not make presentation depend on `Game`, a mutable collection, or BGG availability. `GatheringService` serializes join/leave/promotion with a PostgreSQL transaction and `FOR UPDATE`, scopes the row by community key, and applies the Camp registration gate.

Migration `20260828183821_ClubCampContextsAndGatheringSnapshots` is deliberately additive. It backfills gathering snapshots, converts existing managed chats into Club/Camp subtype rows, copies legacy global Club copies into versioned JSON, and migrates legacy registration/personal-copy data into Camp rows. Existing Camp creators are recorded as `0` because the old schema did not retain that audit fact. The down migration refuses to run when snapshot-only gatherings exist because inventing a legacy `GameId` would lose data. Review this SQL and take a database backup before production deployment.

Camp creation is initiated through `/admin` in a private Telegram chat. The flow stores short-lived PostgreSQL conversation state, uses `KeyboardButtonRequestChat`/`chat_shared`, and then calls `TelegramCampChatValidator`; a shared chat ID alone is never sufficient. Mini App administration may create Clubs but must direct Camp creation to this native flow.

`BOARD_CAMP_CHAT_ID`, legacy `GameSession`, global `GameCopySource.Club`, `GameInterest`, and old `CollectionImport` paths remain only for production-data compatibility. New Club/Camp behavior must not depend on them. Remove the variable and legacy entities only in a separately reviewed migration after old sessions/imports are retired or converted; never silently drop their data.

## Gathering domain invariants

Use a dedicated gathering aggregate unless a future migration can demonstrate that legacy `GameSession` semantics can be generalized without weakening either workflow. A gathering belongs to exactly one configured community and stores an actual UTC instant; render and accept local time using the community's configured IANA timezone.

Player limits mean total people including the organizer and must satisfy `minimum <= desired <= maximum`. The organizer occupies a confirmed place. Signups are unique per gathering. Once full, new signups enter a stable ordered waitlist. A confirmed participant leaving or explicitly giving up a place promotes the first eligible waitlisted participant in the same database transaction. Capacity changes may not silently displace confirmed participants.

Gathering presentation is game-first. Store BGG thumbnail and large image URLs on the normalized `Game`; do not download or duplicate the image. Cards prefer `ThumbnailImageUrl` and details/Telegram media prefer `ImageUrl`, with fallback to the other size and a graceful no-image state. Selected expansions do not require images.

`GameGathering.Description` is optional organizer context, limited to 300 characters server-side and in PostgreSQL. It should read as roughly one or two sentences, appear in details, be truncated on cards, and appear in a Telegram announcement only while concise. `CanTeachRules` is organizer-controlled and visible in cards, details, and announcements. Russian presentation uses positive `Могу объяснить правила` when true and neutral `Опыт с игрой желателен` when false; never use hostile wording.

PostgreSQL is the concurrency authority. Join, leave, give-up, and promotion operations must lock/serialize the gathering and affected signup rows (or provide equivalent constraints) so simultaneous joins cannot exceed maximum capacity, waitlist order is stable, and one seat cannot be promoted twice. UI capacity checks are advisory only.

Attendance is explicit: `Attended`, `NoShow`, `CancelledInAdvance`, or `Unknown`. Never infer `NoShow` from missing activity, and a cancelled gathering must never generate no-shows. Reliability UI shows transparent counts with sample size, distinguishes advance cancellation from no-show, avoids punitive styling, and should remain private or understated until a reasonable sample exists. Do not add karma, ratings, likes/dislikes, public voting, or an opaque reliability score.

## Games and integrations

`Game` is the legacy normalized catalog used by compatibility features. New Club ownership is JSON and new Camp participant availability is relational; do not encode either as new global `Game`/`GameCopy` rows. Normal BGG collection imports request owned base games and owned expansions separately, then use BGG relationship links to nest expansions.

`BGG_API_TOKEN` is server-only. A nonblank token automatically enables existing search, direct lookup, and collection import paths; missing token must degrade gracefully. Tests must use fake HTTP and CI must not call live BGG. `GetGameDetailsAsync` exposes expansion relationships for gathering creation only when BGG returns an inbound `boardgameexpansion` link. Never infer relationships from names. Selected expansions belong explicitly to a gathering rather than becoming normal club catalog copies.

BGG is the only external game catalog and collection provider. Do not restore Tesera clients, proxy infrastructure, health probes, URL parsers, or fallback behavior.

## Notifications and background work

Business/application code should request typed gathering notifications such as ready, below-minimum, cancelled, changed, waitlist-promoted, and reminder. Telegram is the delivery adapter; do not scatter direct sends through domain services. Cancellation and promotion are transactional messages, while optional reminders follow user preferences.

Time-based work must be persisted and restart-safe. Use the existing hosted-service pattern and PostgreSQL state; never schedule a gathering solely with an in-memory `Task.Delay`. Prefer editing one group announcement as state changes, plus relevant DMs, over posting a new group message for every join/leave.

## UI and security

The Telegram Mini App is the primary UI. Do not route `/start` back into legacy registration/menu handlers. Club and Camp share gathering components and APIs; Camp adds registration and contribution screens. Telegram remains the entry point for `/start`, native Camp chat selection, group announcements, contextual buttons, notifications, and import results. User-facing bot and Mini App text is Russian; identifiers and technical documentation are English.

All authorization is enforced by the backend. Organizer-only changes require the authenticated Telegram identity to own (or be explicitly delegated for) the gathering. Sequential IDs are acceptable routing identifiers but never authorization. Material date/time/game/capacity/cancellation edits notify confirmed participants.

## Configuration and verification

Never commit secrets. Required and optional variables are documented in `README.md`; add every new variable there. Review generated EF migrations and the model snapshot before committing.

Baseline verification:

```bash
dotnet restore oyinQ.Bot.slnx
dotnet build oyinQ.Bot.slnx --configuration Release --no-restore
dotnet test oyinQ.Bot.slnx --configuration Release --no-build --no-restore
```

Run frontend checks when a Mini App exists and Docker build when deployment/frontend packaging changes. Static and mocked tests do not prove live BGG, Telegram, Northflank, or database behavior.
