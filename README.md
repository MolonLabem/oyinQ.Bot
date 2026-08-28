# OyinQ Bot

One Telegram bot for multiple BoardGame Club and Camp chats. A chat has exactly one mode, while both modes share the same snapshot-based gathering engine. Telegram provides contextual entry points and notifications; the Mini App is the primary UI.

## Requirements

- .NET 10 SDK
- PostgreSQL 17+ (or another currently supported PostgreSQL version compatible with Npgsql)
- Telegram bot token from BotFather
- BGG API token only when BoardGameGeek features are enabled
- Docker / Docker Compose (optional)

## Configuration

The application reads configuration from environment variables or .NET user secrets.

| Variable | Required | Description |
| --- | --- | --- |
| `TELEGRAM_BOT_TOKEN` | yes | Telegram bot token from BotFather. |
| `TELEGRAM_WEBHOOK_SECRET` | webhook mode | Secret path component used by `/telegram/webhook/{secret}`. Must contain only letters, digits, `_`, or `-`. |
| `PUBLIC_BASE_URL` | yes | Public HTTPS origin serving the Mini App, for example `https://bot.example.com`. It is also the webhook origin in webhook mode. |
| `CONNECTION_STRING` | yes | PostgreSQL connection string. |
| `BOARD_CAMP_CHAT_ID` | legacy only | Existing `GameSession` announcement target. New Club/Camp gatherings never read it. Keep it only until legacy sessions are retired. |
| `OYINQ_COMMUNITIES` | yes | Bootstrap JSON array for initial managed chats. Each item has a stable `key`, `name`, negative `telegramChatId`, mode (`Club` or `Camp`), and an explicit IANA `timeZone`. Runtime configuration is stored in PostgreSQL. Old `Gatherer`/`BoardCamp` values are accepted temporarily as aliases. |
| `ADMIN_TELEGRAM_IDS` | admin | Non-secret comma-separated Telegram user IDs allowed to use Mini App administration. |
| `BGG_API_TOKEN` | production | Bearer token for BoardGameGeek API access. When missing or blank, BGG features are disabled while the rest of the bot continues to run. |
| `USE_LONG_POLLING` | no | `true` for local development. Default: `false` (webhook mode). |
| `PORT` | hosting | HTTP port supplied by the hosting platform. Docker defaults to `8080`. |

Do not commit real tokens, passwords, connection strings, or Telegram IDs that should remain private. `appsettings.json` intentionally contains logging configuration only.

## Local setup

The Mini App lives in `MiniApp` and is built into `wwwroot/app`:

```bash
cd MiniApp
npm ci
npm run check
npm run build
cd ..
```

The Dockerfile performs the same locked frontend build automatically. Launch OyinQ through a configured group entry point; the bot validates membership and returns a Web App button carrying the stable community key. A user belonging to multiple configured communities can switch context in the Mini App.

### 1. Start PostgreSQL

With Docker Compose:

```bash
docker compose up -d postgres
```

The included development database uses:

```text
Host=localhost;Port=5432;Database=oyinq;Username=oyinq;Password=oyinq
```

Use different credentials outside local development.

### 2. Configure user secrets

From the repository root:

```bash
dotnet user-secrets set "TELEGRAM_BOT_TOKEN" "<telegram-token>" --project oyinQ.Bot.csproj
dotnet user-secrets set "CONNECTION_STRING" "Host=localhost;Port=5432;Database=oyinq;Username=oyinq;Password=oyinq" --project oyinQ.Bot.csproj
dotnet user-secrets set "PUBLIC_BASE_URL" "https://<public-https-origin>" --project oyinQ.Bot.csproj
dotnet user-secrets set "OYINQ_COMMUNITIES" '[{"key":"club","name":"Board game club","telegramChatId":-1001111111111,"mode":"Club","timeZone":"Asia/Qyzylorda"}]' --project oyinQ.Bot.csproj
dotnet user-secrets set "ADMIN_TELEGRAM_IDS" "<telegram-user-id>[,<another-id>]" --project oyinQ.Bot.csproj
dotnet user-secrets set "USE_LONG_POLLING" "true" --project oyinQ.Bot.csproj
```

Enable the approved BGG integration with:

```bash
dotnet user-secrets set "BGG_API_TOKEN" "<bgg-api-token>" --project oyinQ.Bot.csproj
```

Then restart the application. No code or database change is required.

Bootstrap existing chats when required (PowerShell example; use platform environment variables/secrets in production):

```powershell
dotnet user-secrets set "OYINQ_COMMUNITIES" '[{"key":"club","name":"Board game club","telegramChatId":-1001111111111,"mode":"Club","timeZone":"Asia/Qyzylorda"},{"key":"legacy-camp","name":"Existing Camp","telegramChatId":-1002222222222,"mode":"Camp","timeZone":"Asia/Qyzylorda"}]' --project oyinQ.Bot.csproj
```

Community keys are stable, non-secret identifiers containing at most 32 ASCII letters, digits, `_`, or `-`. `OYINQ_COMMUNITIES` only bootstraps missing database rows. Admins add Clubs in the Mini App. Admins create Camps with `/admin` in a private Telegram chat; the bot uses Telegram's native group picker and verifies that it can access the selected group. A Telegram group entry link carries the key, but the backend verifies membership before accepting the context.

Long polling does not require `TELEGRAM_WEBHOOK_SECRET`, but the Mini App still requires `PUBLIC_BASE_URL`. For local development, expose the local ASP.NET port through a trusted HTTPS tunnel and use that tunnel origin.

### 3. Restore, build, migrate, and test

```bash
dotnet restore oyinQ.Bot.slnx
dotnet build oyinQ.Bot.slnx
dotnet ef database update --project oyinQ.Bot.csproj
dotnet test oyinQ.Bot.slnx
```

If `dotnet ef` is not installed:

```bash
dotnet tool install --global dotnet-ef --version 10.*
```

### 4. Run locally

```bash
dotnet run --project oyinQ.Bot.csproj
```

With `USE_LONG_POLLING=true`, the application removes the webhook and starts receiving updates through Telegram long polling.

Verify the local health endpoint shown by `dotnet run`, for example:

```bash
curl http://localhost:5000/health
```

Then send `/start` in a configured group and use its **Open OyinQ** entry point. A plain `/start` in DM discovers the communities in which the user is currently a member.

## Club and Camp behavior

- **Club:** no event registration. Users create gatherings from that Club's versioned PostgreSQL JSON collection or from an arbitrary BGG link. Arbitrary BGG games and expansions are stored only in the gathering snapshot; they are not inserted into the global `Games` table or the Club collection.
- **Camp:** registration is required before catalog, contributions, interests, or gatherings. The effective catalog is the immutable `Camp.BaseCollectionJson` snapshot plus relational participant contributions. Multiple contributors for one BGG item appear as one logical catalog entry while contributor IDs/copies remain available.
- **Both modes:** `GameGathering.GameSnapshotJson` contains the immutable game name, BGG ID, images, player metadata, and selected expansions. Join/leave and waitlist promotion are serialized by PostgreSQL and the group recruitment message is edited in place.

Club collection administration supports viewing/searching in the Mini App, adding a BGG game, editing/removing games and expansions through the validated version-1 JSON document, and exporting/replacing that JSON for deliberate recovery. An administrator may also store an optional BGG username on each Club, preview a mirror of that account's Owned collection, review added/removed/changed games and excluded orphan expansions, and explicitly apply the proposed version-1 snapshot. Previewing never mutates PostgreSQL. An empty BGG collection requires a separate destructive confirmation before the existing JSON replacement path is used. Camp personal import requests BGG base games and expansions separately, shows a select-all preview, keeps base/expansion toggles independent, and uses `🟨` when an expansion is selected without its base game.

## Database migrations

Migrations are stored in `Data/Migrations`.

Create a migration after intentional model changes:

```bash
dotnet ef migrations add <MigrationName> --project oyinQ.Bot.csproj --output-dir Data/Migrations
```

Apply migrations manually:

```bash
dotnet ef database update --project oyinQ.Bot.csproj
```

The application also calls `Database.MigrateAsync()` during startup. Production database credentials therefore need permission to apply the checked-in migrations.

### Club/Camp migration notes

`20260828183821_ClubCampContextsAndGatheringSnapshots` is additive and preserves the legacy tables. It:

1. backfills immutable JSON snapshots for existing gatherings before allowing nullable `GameId`;
2. creates mode-qualified `Clubs` and `Camps` rows so one chat cannot satisfy both modes;
3. copies existing global Club copies into OyinQ version-1 collection JSON;
4. migrates legacy Camp registration fields and personal copies into relational Camp rows;
5. retains legacy `Game`, `GameCopy`, `GameInterest`, `CollectionImport`, and `GameSession` data for a later, separately reviewed retirement.

Take a PostgreSQL backup before the production rollout and review the generated SQL against a production schema clone. Existing Camp rows use `CreatedByTelegramUserId = 0` because the old schema did not retain the creator. Downgrade is intentionally blocked when snapshot-only gatherings exist; inventing a `GameId` would be destructive.

## Docker

Build:

```bash
docker build -t oyinq-bot .
```

Run against an existing PostgreSQL instance without BGG enabled:

```bash
docker run --rm -p 8080:8080 \
  -e PORT=8080 \
  -e TELEGRAM_BOT_TOKEN="<telegram-token>" \
  -e TELEGRAM_WEBHOOK_SECRET="<webhook-secret>" \
  -e PUBLIC_BASE_URL="https://bot.example.com" \
  -e CONNECTION_STRING="<postgres-connection-string>" \
  -e OYINQ_COMMUNITIES='<communities-json>' \
  -e ADMIN_TELEGRAM_IDS="<telegram-user-id>" \
  oyinq-bot
```

To enable BGG later, add:

```bash
-e BGG_API_TOKEN="<bgg-api-token>"
```

and restart/redeploy the container.

## Production deployment

Deploy the Dockerfile to any container host that provides a stable public HTTPS URL and connect it to a managed PostgreSQL database.

1. Create the PostgreSQL database and save its connection string as `CONNECTION_STRING`.
2. Configure all required environment variables from the table above. `BGG_API_TOKEN` is required for BGG links, collection management, and Camp imports.
3. Leave `USE_LONG_POLLING=false` or unset it.
4. Set `PUBLIC_BASE_URL` to the final public HTTPS origin with no Telegram webhook path appended.
5. Set a strong `TELEGRAM_WEBHOOK_SECRET` consisting only of letters, digits, `_`, and `-`.
6. Back up PostgreSQL, review pending migrations, and deploy the Docker image. The process listens on `${PORT:-8080}`.
7. Verify `GET /health` returns HTTP 200.
8. Check application logs for successful startup/migration and webhook setup.
9. Send `/start` in each configured community and verify its contextual Mini App entry.
10. Verify the Mini App lists only communities for which the Telegram user has membership.

The webhook setup service configures Telegram to call:

```text
{PUBLIC_BASE_URL}/telegram/webhook/{TELEGRAM_WEBHOOK_SECRET}
```

Do not expose the webhook secret in source control or public logs.

## Operational behavior

- Telegram identity always comes from the incoming Telegram update. Community context from a deep link is checked against configured communities and Telegram group membership before it is persisted.
- Admin authorization is based on `ADMIN_TELEGRAM_IDS`.
- A nonblank `BGG_API_TOKEN` enables BGG search, transient direct lookup, Club management, and Camp import. Missing access degrades with a clear Russian error.
- BGG game details use only API-declared expansion relationships. Club synchronization and Camp collection import send separate base-game and expansion requests, then enrich relationship data. A Club expansion is attached to every owned officially linked base game; expansions without an owned linked base are excluded from the proposed document and reported in the preview.
- BGG synchronization is reviewed replacement, not a background write. API errors or pending responses leave `Club.CollectionJson` unchanged, while removals shown in a successful preview are applied only after administrator confirmation.
- Images remain hosted by BGG and are stored as URLs in collection/contribution/gathering snapshots; OyinQ does not download or duplicate them.
- Gathering descriptions are optional and limited to 300 characters. Rule-teaching preference uses positive or neutral Russian wording.
- Camp creation uses `KeyboardButtonRequestChat`/`chat_shared`, then separately verifies group type and bot membership.
- Legacy queued imports and `GameSession` remain restart-safe compatibility features. New Club/Camp behavior does not use global Club copies or `BOARD_CAMP_CHAT_ID`.

## CI

GitHub Actions restores, builds, and tests the solution on pushes and pull requests targeting `master` using .NET 10.

Run the same checks locally before deployment:

```bash
dotnet restore oyinQ.Bot.slnx
dotnet build oyinQ.Bot.slnx --configuration Release --no-restore
dotnet test oyinQ.Bot.slnx --configuration Release --no-build --no-restore
```

## Health check

`GET /health` is the lightweight process health endpoint.

## Manual Telegram smoke test

1. Open a Club context, confirm no registration screen appears, create a gathering from the Club collection, choose an expansion, and verify the group announcement and details survive a later Club JSON edit.
2. Create another Club gathering from an arbitrary BGG link, select an API-linked expansion, and verify neither item appears in the Club JSON or as a new global catalog game.
3. As an admin, run `/admin` → **Создать кэмп**, enter a name, choose a Club snapshot or no base, select a Telegram group with the native picker, and verify the bot rejects a group already bound to a Club.
4. Open the new Camp as a member, verify normal features are blocked until registration, complete registration, and reopen the Mini App.
5. Import a personal BGG collection. Confirm every base game and expansion starts selected; toggle each independently; select an expansion without its base and verify the `🟨` warning does not prevent confirmation.
6. Import the same game from two participants and verify the Camp catalog shows one logical game. Create a Camp gathering from it and exercise join, full/waitlist, leave, promotion, and in-place announcement update.
