# OyinQ

OyinQ is a .NET 10 ASP.NET Core backend, React Telegram Mini App, and one Telegram bot serving multiple board-game communities. PostgreSQL is the source of truth. A community is either a `Club` or a `Camp`.

The Mini App owns registration, collections, contributions, game discovery, gathering lifecycle, and all administration. Telegram is deliberately thin: `/start`, `/admin` entry, contextual deep links, native prepared user/chat selection, group announcements, and notifications.

## Current model

- Clubs have no registration gate and store their revisioned, versioned OyinQ JSON collection in PostgreSQL `Club.CollectionJson`.
- Camps have inclusive local dates, lifecycle status, scoped `CampRegistration`, an immutable source-Club snapshot in `Camp.BaseCollectionJson`, and typed participant availability in `CampGameContributions`.
- Both modes use `GameGathering`; presentation is immutable `GameSnapshotJson`, and signup concurrency is enforced in PostgreSQL.
- Personal BGG imports are Camp-only persisted jobs. A hosted worker owns the authoritative selection draft and survives request cancellation/restarts.
- BGG is the only external board-game provider. Missing BGG credentials visibly disable search/add/import without disabling stored collections, contributions, or gatherings.
- Telegram user/chat assignment uses prepared native peer selectors. Raw Telegram IDs are not accepted by normal administration APIs.
- Runtime community resolution uses `OyinQCommunities` through `ICommunityStore`. Optional bootstrap JSON only inserts missing rows.

## Configuration

.NET hierarchical configuration is used. Environment variables use double underscores.

| Configuration key | Environment variable | Required | Purpose |
|---|---|---:|---|
| `Database:ConnectionString` | `Database__ConnectionString` | yes | PostgreSQL connection string. |
| `Telegram:Token` | `Telegram__Token` | yes | Telegram bot token. |
| `Telegram:WebhookSecret` | `Telegram__WebhookSecret` | production | Telegram webhook secret. Not required in Development long-polling mode. |
| `Telegram:PublicBaseUrl` | `Telegram__PublicBaseUrl` | yes | Public HTTPS origin used for webhook and Mini App URLs. |
| `Telegram:UseLongPolling` | `Telegram__UseLongPolling` | no | Defaults to `false`; Development configuration sets `true`. Do not set it in production. |
| `BoardGameGeek:ApiToken` | `BoardGameGeek__ApiToken` | no | Server-only BGG API token. |
| `Administration:BootstrapTelegramUserIds` | `Administration__BootstrapTelegramUserIds` | initial bootstrap only | Optional comma-separated administrator IDs. Missing IDs are inserted into PostgreSQL at startup. Remove after initial setup. |
| `CommunityBootstrap:CommunitiesJson` | `CommunityBootstrap__CommunitiesJson` | no | Optional one-time JSON bootstrap for a fresh database. Remove after rows exist. |

`appsettings.json` contains stable non-secret defaults. `appsettings.Development.json` enables long polling and contains the non-secret local Docker PostgreSQL connection. Secrets are never committed.

Administrator authorization is stored in `OyinQAdministrators`. Use the bootstrap value only to establish initial or recovery access, then add/remove administrators in Mini App administration and remove the bootstrap variable. A configured bootstrap ID is reinserted when missing, so leaving it configured intentionally acts as recovery access.

Bootstrap JSON example:

```json
[{"key":"club","name":"Board game club","telegramChatId":-1001111111111,"mode":"Club","timeZone":"Asia/Qyzylorda"}]
```

Only `Club` and `Camp` are valid modes. Bootstrap is optional when communities already exist in PostgreSQL. Clubs and Camps are created in the admin Mini App with Telegram's native group picker. `/admin` opens that global area even when the administrator belongs to no managed community.

## Collection recovery

Normal deployments must reuse the persistent PostgreSQL database. EF migrations define schema; they are not a source-control backup for mutable Club collections.

For deliberate fresh-database recovery:

1. Bootstrap the first administrator.
2. Create the Club with the native Telegram group picker.
3. Open **Manage collection → Import JSON**.
4. Prefer the audited `Data/Imports/RollMove/club-collection.v2.json` when present; otherwise select the reviewed v1 membership artifact or a newer exported Club JSON file. See `Data/Imports/RollMove/README.md` for deterministic v2 generation.
5. Verify the displayed revision and game count before enabling normal use.

Import requires the current collection revision and returns HTTP 409 if another administrator changed the document. It never silently overwrites a newer revision.

## Local development

```powershell
docker compose up -d
dotnet user-secrets set "Telegram:Token" "<telegram-bot-token>" --project oyinQ.Bot.csproj
dotnet user-secrets set "Telegram:PublicBaseUrl" "https://<your-tunnel-host>" --project oyinQ.Bot.csproj
dotnet user-secrets set "BoardGameGeek:ApiToken" "<bgg-api-token>" --project oyinQ.Bot.csproj
dotnet user-secrets set "Administration:BootstrapTelegramUserIds" "<telegram-user-id>,<another-telegram-user-id>" --project oyinQ.Bot.csproj
dotnet user-secrets set "CommunityBootstrap:CommunitiesJson" '[{"key":"club","name":"Local club","telegramChatId":-1001111111111,"mode":"Club","timeZone":"Asia/Qyzylorda"}]' --project oyinQ.Bot.csproj
dotnet run --project oyinQ.Bot.csproj
```

To override the Development database:

```powershell
dotnet user-secrets set "Database:ConnectionString" "Host=localhost;Port=5432;Database=oyinq;Username=oyinq;Password=<password>" --project oyinQ.Bot.csproj
```

For local webhook testing instead of the Development default, set `Telegram:UseLongPolling` to `false` and also set:

```powershell
dotnet user-secrets set "Telegram:WebhookSecret" "<random-webhook-secret>" --project oyinQ.Bot.csproj
```

## Build and verification

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

The Dockerfile builds the Mini App and backend in separate stages. Northflank supplies `PORT`; the image uses it when present and falls back to `8080` locally.

`GET /health` is process liveness and intentionally performs no external calls. Configure `GET /ready` as readiness; it runs a cheap PostgreSQL connectivity check and does not depend on optional BGG or Telegram.

## Northflank

### Secrets

| Exact key | Value |
|---|---|
| `Database__ConnectionString` | PostgreSQL/Neon connection string |
| `Telegram__Token` | Telegram bot token |
| `Telegram__WebhookSecret` | Random webhook secret |
| `BoardGameGeek__ApiToken` | BGG API token; omit only when BGG features should be disabled |

### Variables

| Exact key | Example |
|---|---|
| `Telegram__PublicBaseUrl` | `https://oyinq.example.com` |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Administration__BootstrapTelegramUserIds` | `123456789,987654321` for initial bootstrap only; delete after administrators appear in the Mini App |
| `CommunityBootstrap__CommunitiesJson` | optional one-time bootstrap JSON; delete after successful bootstrap |

### Remove from Northflank

Remove the old flat keys `TELEGRAM_BOT_TOKEN`, `TELEGRAM_WEBHOOK_SECRET`, `PUBLIC_BASE_URL`, `CONNECTION_STRING`, `BGG_API_TOKEN`, `ADMIN_TELEGRAM_IDS`, `OYINQ_COMMUNITIES`, `USE_LONG_POLLING`, and `BOARD_CAMP_CHAT_ID` after adding their replacements where applicable. Remove any indexed `Administration__TelegramUserIds__0`, `__1`, and later keys as well. `Administration__BootstrapTelegramUserIds` is removable after initial setup because runtime administrators are stored in PostgreSQL and managed in the Mini App. Also remove every Tesera, Cloudflare/Tesera proxy, and legacy import-worker setting.

Do not set `Telegram__UseLongPolling` in production. Do not manually set `PORT` or `ASPNETCORE_URLS`; the platform and image handle them.

## Database compatibility

Migration `20260828183821_ClubCampContextsAndGatheringSnapshots` is additive and must be reviewed with a production backup. The EF model intentionally retains legacy `Games`, `GameCopies`, `GameInterests`, `CollectionImports`, `GameSessions`, `GameSessionParticipants`, the old participant registration columns, and nullable `GameGathering.GameId`. Runtime code no longer reads or writes those paths. They remain mapped so production data is not silently dropped; retirement needs a separately reviewed data audit/migration.

Migration `20260828230514_PersistAdministrators` is also additive. It creates only `OyinQAdministrators`; startup then inserts missing comma-separated bootstrap IDs with `ON CONFLICT DO NOTHING`. It does not delete or rewrite application data.

Migration `20260829000852_StabilizeClubCampMiniApp` is additive. It adds Club revisions, Camp dates, typed contribution source, Camp import jobs, pending Telegram peer selections, administrator presentation fields, and gathering publication state. Existing contribution JSON receives only the required version marker; existing published gatherings are marked published. Existing legacy tables and `Club.BggUsername` remain mapped and deprecated.

The additive migrations after it are `20260830193242_GatheringHistoryAndCleanup`, `20260830200452_CatalogMetadataCampAvailability`, `20260830201423_CampImportSkipResolution`, `20260830201708_ClubMetadataRefreshJobs`, and `20260830210951_FinalConsistencyAndReliability`. The final migration adds persisted import confirmations, Club-refresh leases, worker indexes, and partial unique active-job invariants. Its deterministic pre-index reconciliation ends duplicate active jobs without deleting application data. Validate this full batch and its generated SQL against a production backup before deployment; do not apply it as part of this repository review.

## Manual verification

Use [docs/manual-verification.md](docs/manual-verification.md) after deployment. Live Telegram and BGG behavior must not be inferred from mocked tests.
