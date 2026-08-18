# OyinQ Bot

Telegram bot for BoardCamp: participant registration, game catalog and interests, BGG/Tesera collection imports, live game recruitment sessions, and Telegram-only administration.

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
| `PUBLIC_BASE_URL` | webhook mode | Public HTTPS base URL, for example `https://bot.example.com`. |
| `CONNECTION_STRING` | yes | PostgreSQL connection string. |
| `BOARD_CAMP_CHAT_ID` | sessions | Telegram group/supergroup ID used for recruitment messages. Usually a negative number. |
| `ADMIN_TELEGRAM_IDS` | admin | Comma-separated Telegram user IDs allowed to use `/admin`. |
| `ACCOMMODATION_PRICE_PER_DAY` | no | Accommodation price used by registration/configuration. Default: `3000`. Admin accommodation reporting currently uses the project-fixed value `3000 ₸`. |
| `BGG_API_TOKEN` | no | Bearer token for BoardGameGeek API access. When missing or blank, BGG features are disabled while the rest of the bot continues to run. |
| `USE_LONG_POLLING` | no | `true` for local development. Default: `false` (webhook mode). |
| `PORT` | hosting | HTTP port supplied by the hosting platform. Docker defaults to `8080`. |

Do not commit real tokens, passwords, connection strings, or Telegram IDs that should remain private. `appsettings.json` intentionally contains logging configuration only.

## Local setup

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
dotnet user-secrets set "BOARD_CAMP_CHAT_ID" "<telegram-group-id>" --project oyinQ.Bot.csproj
dotnet user-secrets set "ADMIN_TELEGRAM_IDS" "<telegram-user-id>[,<another-id>]" --project oyinQ.Bot.csproj
dotnet user-secrets set "USE_LONG_POLLING" "true" --project oyinQ.Bot.csproj
```

`BGG_API_TOKEN` is intentionally optional. Until BGG approves API access, leave it unset. BGG import and manual BGG lookup actions are hidden or return a friendly unavailable message; Tesera and all non-BGG features remain usable.

After BGG API access is approved, enable BGG with:

```bash
dotnet user-secrets set "BGG_API_TOKEN" "<bgg-api-token>" --project oyinQ.Bot.csproj
```

Then restart the application. No code or database change is required.

Long polling does not require `PUBLIC_BASE_URL` or `TELEGRAM_WEBHOOK_SECRET`.

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

Then send `/start` to the bot in Telegram.

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
  -e BOARD_CAMP_CHAT_ID="<telegram-group-id>" \
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
2. Configure all required environment variables from the table above. `BGG_API_TOKEN` may remain unset until API access is approved.
3. Leave `USE_LONG_POLLING=false` or unset it.
4. Set `PUBLIC_BASE_URL` to the final public HTTPS origin with no Telegram webhook path appended.
5. Set a strong `TELEGRAM_WEBHOOK_SECRET` consisting only of letters, digits, `_`, and `-`.
6. Deploy the Docker image. The process listens on `${PORT:-8080}`.
7. Verify `GET /health` returns HTTP 200.
8. Check application logs for successful startup/migration and webhook setup.
9. Send `/start` to the bot and complete registration.
10. Verify the bot can post and edit a recruitment message in `BOARD_CAMP_CHAT_ID`.

The webhook setup service configures Telegram to call:

```text
{PUBLIC_BASE_URL}/telegram/webhook/{TELEGRAM_WEBHOOK_SECRET}
```

Do not expose the webhook secret in source control or public logs.

## Operational behavior

- Telegram identity always comes from the incoming Telegram update.
- Admin authorization is based on `ADMIN_TELEGRAM_IDS`.
- Collection imports are persisted in PostgreSQL and processed by a background worker.
- When `BGG_API_TOKEN` is missing, BGG is treated as disabled: BGG actions are hidden/rejected, queued BGG imports are failed with a clear reason, and the worker continues processing Tesera imports.
- BGG imports are stepped in chunks so a restart can resume from saved progress when BGG is enabled.
- Imports left in `Running` state for more than 10 minutes are returned to `Pending` by the worker.
- Personal imported copies default to `Maybe`; club copies default to `Bringing`.
- Session recruitment is posted in the BoardCamp group and updated by editing the original message.

## CI

GitHub Actions restores, builds, and tests the solution on pushes and pull requests targeting `master` using .NET 10.

Run the same checks locally before deployment:

```bash
dotnet restore oyinQ.Bot.slnx
dotnet build oyinQ.Bot.slnx --configuration Release --no-restore
dotnet test oyinQ.Bot.slnx --configuration Release --no-build --no-restore
```

## Health check

`GET /health` returns HTTP 200 when the ASP.NET Core process is running. It is a process-level health endpoint; it does not currently perform a PostgreSQL, Telegram, BGG, or Tesera dependency probe. BGG being disabled therefore does not make the health endpoint unhealthy.
