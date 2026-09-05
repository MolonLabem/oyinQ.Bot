# OyinQ

OyinQ is a .NET 10 ASP.NET Core backend, React Telegram Mini App, and one Telegram bot serving multiple board-game communities. PostgreSQL is the source of truth. A community is either a `Club` or a `Camp`.

The Mini App owns registration, collections, contributions, game discovery, gathering lifecycle, and all administration. Telegram is deliberately thin: private `/start`, `/menu`, `/help`, `/privacy`, `/admin`, group `/oiynq`, contextual deep links, native prepared user/chat selection, group announcements, and notifications.

## Current model

- Clubs have no registration gate and store their revisioned, versioned OyinQ JSON collection in PostgreSQL `Club.CollectionJson`. Administrators can add a whole BGG Owned collection by username through a persisted additive job; it never removes existing games or selected expansions.
- Кэмпы хранят точные UTC-границы открытия и закрытия (окончание исключено), статус, регистрации `CampRegistration` с выбранными `CampRegistrationDays`, неизменяемый снимок базы `Camp.BaseCollectionJson` и явную доступность игр в `CampGameContributions`. `DaysStaying` — производное поле совместимости, а не основание допуска.
- Both modes use `GameGathering`; presentation is immutable `GameSnapshotJson`, and signup concurrency is enforced in PostgreSQL. `GatheringCapacity` derives occupied seats from the organizer, confirmed registered participants, and manual guests; the API transports that result as `occupiedSeats`.
- `GatheringLifecycle` owns active/upcoming/due status semantics, `GatheringListQuery` owns list/history filtering, and the profile schedule composes that query instead of maintaining a second schedule model.
- Gathering instants are stored and transported as UTC. The Mini App interprets and validates `datetime-local` values in the selected community's validated IANA time zone, never in the browser or server machine time zone.
- Личная коллекция хранится глобально в `ParticipantCollectionItems`: импорт BGG и ручное добавление доступны в Профиль → Моя коллекция. Она сохраняется после отмены регистрации и переключения сообществ. Кэмп получает только явно выбранные отметки «Могу привезти» / «Точно привезу».
- Поиск клуба объединяет клубную коллекцию и личные игры только текущего пользователя; `Club.CollectionJson` остаётся клубным. Поиск кэмпа использует прежнюю проекцию базовой коллекции и вкладов участников.
- Профиль содержит внутренние вкладки «Моя коллекция», «Календарь», «Настройки». Календарь по-прежнему показывает сборы из доступных сообществ. `tab=mine` совместим, старые ключи localStorage сохранены.
- Все Mini App API создают/обновляют Participant через `ParticipantIdentityService` после проверки `initData`. Личный `/start` не требуется для записи на сбор. Для уведомлений показывается отдельное приглашение запустить бота; ссылка сохраняет контекст сбора и использует `getMe()`.
- Личные импорты переиспользуют существующий worker и `CampBggImports` с `CampId = null`; старые задания продолжают работать. Повторное подтверждение идемпотентно, импорт не удаляет ручные игры.
- BGG is the only external board-game provider. Missing BGG credentials visibly disable search/add/import without disabling stored collections, contributions, or gatherings.
- BGG XML API calls are server-side, authenticated with the application token, throttled, and limited to 20 IDs per `/thing` request. Public Mini App screens include the required linked “Powered by BGG” attribution.
- Telegram user/chat assignment uses prepared native peer selectors. Raw Telegram IDs are not accepted by normal administration APIs.
- Runtime community resolution uses `OyinQCommunities` through `ICommunityStore`. Optional bootstrap JSON only inserts missing rows.

## Планирование и история партий

У кэмпа задаются начало и окончание с временем в часовом поясе сообщества. Регистрация остаётся по пересекающимся календарным дням; первое и последнее утро подписаны точным временем. Исторические полные дни мигрируют без сокращения интервала.

Профиль показывает межсообщественный календарь, личную коллекцию, настройки и отдельно подтверждённые партии. Ситуации со сборами, требующие внимания, доступны администраторам в разделе «Контроль сборов». Каталог фильтруется по владельцу, обеспечению коробки и будущим сборам; популярность считается по подтверждённым партиям.

Личные уведомления сохраняются в PostgreSQL и доставляются отдельным worker. Напоминания и сообщения о полном сборе по умолчанию выключены. Перенос времени, отмена, повышение из очереди и несостоявшийся сбор обязательны. Потерянный ответ Telegram не повторяется автоматически.

`Completed` не означает «сыграно»: организатор явно подтверждает партию и уточняет состав. BG Stats открывается через документированную createPlay-ссылку. Фактические игроки могут делиться отдельными ссылками на свои записи; файлов экспорта нет. Telegram ID в ссылку не попадает.

[Архитектура, миграции, настройки, ограничения и ручная проверка](docs/planning-notifications-plays.md).

[Перенос существующих данных и порядок выпуска](docs/database-rollout.md): миграции выполняются до запуска обработчиков; этот выпуск требует остановки старых экземпляров и проверки на восстановленной копии базы.

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
| `Administration:SuperAdminTelegramUserIds` | `Administration__SuperAdminTelegramUserIds` | yes | Comma-separated configuration-only Super Admin IDs. Set the owner account here. |
| `Gatherings:ScheduleConflictWarningWindowMinutes` | `Gatherings__ScheduleConflictWarningWindowMinutes` | нет | Возможное пересечение начал ±120 минут; 0 выключает предупреждение. |
| `CommunityBootstrap:CommunitiesJson` | `CommunityBootstrap__CommunitiesJson` | no | Optional one-time JSON bootstrap for a fresh database. Remove after rows exist. |

`appsettings.json` contains stable non-secret defaults. `appsettings.Development.json` enables long polling and contains the non-secret local Docker PostgreSQL connection. Secrets are never committed.

Super Admins come only from `Administration:SuperAdminTelegramUserIds`. Normal administrators are stored per chat in `ChatAdminPermissions` and remain effective only while Telegram reports them as an administrator of that chat. Telegram administrators without an OyinQ permission see a locked entry with no sensitive data or controls. A single legacy bootstrap ID is treated as the original owner only when the explicit Super Admin setting is absent; multiple legacy IDs are not promoted.

Bootstrap JSON example:

```json
[{"key":"club","name":"Board game club","telegramChatId":-1001111111111,"mode":"Club","timeZone":"Asia/Qyzylorda"}]
```

Only `Club` and `Camp` are valid modes. Bootstrap is optional when communities already exist in PostgreSQL. Clubs and Camps are created by a Super Admin with Telegram's native group picker. `/admin` opens for Super Admins and current Telegram administrators of chats recorded in `KnownTelegramChats`; unapproved chats remain locked.

## Collection recovery

Normal deployments must reuse the persistent PostgreSQL database. EF migrations define schema; they are not a source-control backup for mutable Club collections.

For deliberate fresh-database recovery:

1. Configure the owner as Super Admin.
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
dotnet user-secrets set "Administration:SuperAdminTelegramUserIds" "<owner-telegram-user-id>" --project oyinQ.Bot.csproj
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
| `Telegram__PublicBaseUrl` | `https://oiynq.example.com` |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Administration__SuperAdminTelegramUserIds` | `123456789` for the owner/Super Admin account |
| `CommunityBootstrap__CommunitiesJson` | optional one-time bootstrap JSON; delete after successful bootstrap |

### Remove from Northflank

Remove the old flat keys `TELEGRAM_BOT_TOKEN`, `TELEGRAM_WEBHOOK_SECRET`, `PUBLIC_BASE_URL`, `CONNECTION_STRING`, `BGG_API_TOKEN`, `ADMIN_TELEGRAM_IDS`, `OYINQ_COMMUNITIES`, `USE_LONG_POLLING`, and `BOARD_CAMP_CHAT_ID` after adding their replacements where applicable. Replace `Administration__BootstrapTelegramUserIds` and any indexed `Administration__TelegramUserIds__0`, `__1`, and later keys with the explicit owner-only `Administration__SuperAdminTelegramUserIds`. Also remove every Tesera, Cloudflare/Tesera proxy, and legacy import-worker setting.

Do not set `Telegram__UseLongPolling` in production. Do not manually set `PORT` or `ASPNETCORE_URLS`; the platform and image handle them.

## Database baseline

The migration chain starts with the clean `20260901073247_CleanBaseline` for a fresh database. Forum topics, community soft deletion/photos, and gathering guests are forward-only additive migrations. The retired global game/session/import/conversation entities, persistent administrator table, participant compatibility fields, `Club.BggUsername`, and `GameGathering.GameId` are not part of the model or schema.

This baseline deliberately replaces the earlier additive migration chain. Do not apply it over a database containing that old chain: create a backup or Neon branch, reset the target schema, and then apply the baseline. Super Admins are restored from configuration, normal administrator grants are recreated per community, and optional communities may be reinserted from `CommunityBootstrap:CommunitiesJson`.

## Manual verification

Use [docs/manual-verification.md](docs/manual-verification.md) after deployment. Live Telegram and BGG behavior must not be inferred from mocked tests.

The public privacy policy is served at `{Telegram:PublicBaseUrl}/privacy`; the Main Mini App root is
`{Telegram:PublicBaseUrl}/app/`. Command scopes, Telegram profile copy, and the private Mini App menu
button are configured by application startup in webhook and long-polling modes. Main Mini App
enablement, previews, artwork, privacy URL, splash settings, and the immutable current username are
manual BotFather settings documented in [docs/botfather-setup.md](docs/botfather-setup.md).

## Миграция личных коллекций

`20260904072034_PersistentParticipantCollection` добавляет глобальную коллекцию и nullable `Participant.PrivateChatStartedAt`, разрешает профильные задания импорта без CampId. Backfill берёт только положительные BGG ID из существующих CampGameContributions, дедуплицирует по участнику/ID/типу и сохраняет снимки; при конфликте предпочитает ручную запись, затем последнюю обновлённую. Вклады, обязательства, регистрации, клубные/базовые коллекции и история сборов не переписываются. Миграция только вперёд; перед применением на production проверьте резервную копию и прогон на staging.

Неизвестный исторический факт личного запуска остаётся `null`: Mini App не доказывает возможность написать пользователю. После реального личного сообщения CTA исчезает. Доставка уведомлений остаётся best effort и может не состояться, если пользователь блокирует бота. Обязательные уведомления о месте, изменении, отмене и недоборе участников не отключаются. Необязательных глобальных рассылок сейчас нет; фиктивные переключатели не добавлены. Старое уведомление о результатах Camp-импорта остаётся частью совместимого потока выбора копий.

Сверка RollMoveClub от 04.09.2026: 51 явный ID подтверждён BGG; уже Owned — 0, отсутствуют — 51; из них 14 уже были в recovery JSON, 37 добавлены (итого 374 базы). Аккаунт не изменялся по просьбе владельца. Списки и детали: [отчёт](Data/Imports/RollMove/reconciliation-report.json), [список для ручного добавления](Data/Imports/RollMove/bgg-missing-owned.csv). XML API2 документирует чтение коллекции, но не официальный write endpoint; ручное добавление выполняется через интерфейс BGG.

### Подтверждённые партии и обновления OyinQ

Завершение сбора по расписанию не считается сыгранной партией. Организатор отдельно подтверждает исход и фактических игроков. «Не состоялась» остаётся в истории сбора, но не создаёт партию и не увеличивает игровую статистику. BG Stats открывается по поддерживаемой ссылке; файлы экспорта не создаются. Организатор и фактические игроки могут добавлять отдельные ссылки BG Stats, автор и организатор — удалять их.

При создании сбора по игре BGG можно явно добавить игру и выбранные дополнения в личную коллекцию, а в кэмпе отдельно подтвердить, что привезёте их. Эти изменения и сбор сохраняются вместе. Клубная коллекция не меняется.

В админ-панели суперадминистратора появился раздел «Обновление OyinQ»: получатели, предпросмотр, подтверждение, результат доставки и повтор ошибочных отправок. Автоматической рассылки при запуске нет. Текст текущего выпуска встроен из [объявления](docs/releases/2026-09-04.md); для нового текста нужен новый стабильный идентификатор выпуска. Состояние доставки хранится в PostgreSQL, темы выбирает существующий групповой отправитель.

Полный отчёт о текущем цикле: [релизная проверка](docs/releases/2026-09-04-review.md). Изменения для пользователей: [CHANGELOG.md](CHANGELOG.md).

Стабилизация после аудита: [порядок проверок и PostgreSQL-тесты](docs/stabilization-verification.md).
Результат исправления A1–A10: [итоговый отчёт](docs/audits/2026-09-04-stabilization-result.md).
Профиль и личная коллекция доступны через «Профиль» даже без выбранного сообщества. Календарь включает только сообщества, доступ к которым подтверждён; регистрация и отметки кэмпа появляются только в выбранном кэмпе.
