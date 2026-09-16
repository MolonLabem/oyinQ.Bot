# OyinQ

OyinQ — Telegram-бот и Mini App для клубов настольных игр и кэмпов. Приложение ведёт каталоги и личные коллекции, помогает создавать сборы и записываться на них, хранит историю подтверждённых партий и предоставляет администраторам управление сообществами.

Проект состоит из одного приложения ASP.NET Core на .NET 10, React Mini App и PostgreSQL. Telegram остаётся транспортом и точкой входа; основные пользовательские и административные сценарии находятся в Mini App.

## Основные понятия

- `OyinQCommunity` — привязка приложения к Telegram-группе. Поддерживаются режимы `Club` и `Camp`.
- `Participant` и личная коллекция глобальны и не зависят от выбранного сообщества.
- Клуб хранит авторитетную коллекцию; связанные клубы и кэмпы читают её через `SharedCollectionReader`. Личное владение и обещания привезти коробку на кэмп хранятся отдельно. Старые кэмпы без связи продолжают читать свой сохранённый каталог.
- `GameGathering` — единая модель сбора. Будущие сборы показываются в расписании, завершённые и отменённые сохраняются в истории.
- Завершение по времени не доказывает факт партии. Организатор или администратор сообщества отдельно подтверждает результат и фактический состав.
- BGG — единственный внешний источник данных об играх. Недоступность BGG не должна ломать работу с уже сохранёнными данными.

Подробные архитектурные инварианты и обязательные правила разработки находятся в [AGENTS.md](AGENTS.md).

## Структура приложения

- `Program.cs` регистрирует сервисы, применяет миграции и подключает HTTP/Telegram и workers.
- `Features/MiniApp` — аутентифицированные маршруты; `MiniAppEndpointSupport` — общие проверки и ответы об ошибках. Доменные правила находятся в `Features/Collections`, `Catalog`, `Communities`, `Gatherings`, `Notifications` и `Admin`.
- `Integrations` — BGG-клиент и Telegram: команды, ссылки, публикации, доставка и выбор чатов/администраторов. Старые ссылки и callback подтверждения Camp-импорта читаются для совместимости; отдельного Telegram wizard нет.
- `MiniApp/src/app/App.tsx` выбирает сообщество и раздел. Административные коллекции и участники кэмпа находятся в `ClubCollection` и `CampParticipants`; мобильные панели используют `useMobileDialog`.
- PostgreSQL хранит очереди, аренды и результаты фоновой работы. Прогресс BGG-refresh сохраняется отдельно; коллекция и её версия публикуются один раз после завершения.

## Локальный запуск

Понадобятся:

- .NET SDK 10;
- Node.js 24 и npm;
- PostgreSQL;
- Telegram-бот для проверки реальной интеграции.

Минимальная конфигурация:

| Переменная | Хранение и назначение |
|---|---|
| `Database__ConnectionString` | Секрет: подключение PostgreSQL; обязательно |
| `Telegram__Token` | Секрет: токен бота; обязательно |
| `Telegram__WebhookSecret` | Секрет: обязателен в webhook-режиме |
| `Telegram__PublicBaseUrl` | Обычная конфигурация окружения: публичный HTTPS origin; обязателен и для polling, поскольку нужен Mini App |
| `Administration__SuperAdminTelegramUserIds` | Обычная серверная конфигурация: доверенные Telegram ID через запятую; в production укажите владельца |
| `BoardGameGeek__ApiToken` | Необязательный секрет; без него сохранённые данные доступны, новые запросы BGG отключены |

Секреты не должны попадать в Git. В Development бот использует long polling; production должен использовать webhook.

`Telegram:UseLongPolling` уже задан в appsettings для Development/Production;
production-переменная для polling не нужна. `CommunityBootstrap:CommunitiesJson`
необязателен и только добавляет отсутствующие сообщества; обычная работа использует БД.
`Gatherings:ScheduleConflictWarningWindowMinutes` имеет стандартное значение 120.
Предпочтения уведомлений и интервал повторного запроса набора игроков хранятся в БД;
периоды опроса workers и лимиты повторов заданы в коде, дополнительных env vars нет.
Единственный старый `Administration:BootstrapTelegramUserIds` сохраняется как
совместимый fallback владельца; новые установки используют `SuperAdminTelegramUserIds`.

Запуск backend:

```powershell
dotnet restore oyinQ.Bot.slnx
dotnet run --project oyinQ.Bot.csproj
```

Для отладки в Visual Studio выберите профиль `http`/`https` или `Container (Dockerfile)`. Для контейнерного профиля нужен запущенный Docker Desktop в режиме Linux containers; проект подключает Microsoft Visual Studio Container Tools как зависимость сборки. При ошибке запуска сначала проверьте `docker version`: должны отображаться Client и Server. PostgreSQL и секреты приложения настраиваются отдельно.

Запуск Mini App в отдельном терминале:

```powershell
Set-Location MiniApp
npm ci
npm run dev
```

При старте backend применяет EF Core migrations до запуска HTTP и фоновых обработчиков. Ошибка миграции останавливает приложение.

## Проверка изменений

Перед коммитом выполняются команды, соответствующие затронутым областям. Перед выпуском обязателен полный набор:

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

Дополнительно перед выпуском:

```powershell
./scripts/verify-release-input.ps1
```

Скрипт проверяет, что встроенные release resources существуют, отслеживаются Git и попадут в сборку из подготовленного индекса. Подробные ручные сценарии собраны в [docs/manual-verification.md](docs/manual-verification.md).

Для PostgreSQL-тестов задайте `OYINQ_TEST_POSTGRES` на отдельный локальный сервер
с правом создания БД. Тесты создают и удаляют только базы `oyinq_test_<guid>`.
Без этой переменной интеграционные тесты пропускаются. CI поднимает PostgreSQL 17
и запускает их; frontend typecheck также запрещает неиспользуемые импорты и параметры.
Артефакты `output/`, `.playwright-cli/` и индекс `.codegraph/` не входят в сборку.

## Документация

Документы разделены по назначению, чтобы правила не приходилось синхронизировать в нескольких местах:

| Документ | Назначение |
|---|---|
| [AGENTS.md](AGENTS.md) | Канонические архитектурные и процессные правила |
| [CHANGELOG.md](CHANGELOG.md) | Единая история пользовательских изменений; эти данные показывает раздел «Что нового?» |
| [docs/manual-verification.md](docs/manual-verification.md) | Общий чек-лист ручной и интеграционной проверки |
| [docs/database-rollout.md](docs/database-rollout.md) | Безопасная репетиция миграций и выпуск схемы PostgreSQL |
| [docs/botfather-setup.md](docs/botfather-setup.md) | Ручные настройки профиля бота и Main Mini App |
| [docs/planning-notifications-plays.md](docs/planning-notifications-plays.md) | Жизненный цикл сборов, планирование, доставка уведомлений, подтверждение партий и BG Stats |
| [docs/wishlist-recruitment.md](docs/wishlist-recruitment.md) | Хотелки и групповые напоминания о наборе игроков |
| [docs/club-collection-revisions.md](docs/club-collection-revisions.md) | Общие коллекции, версии, атомарное обновление BGG |
| [docs/cleanup-audit.md](docs/cleanup-audit.md) | Аудит точек входа, сохранённая совместимость и результаты cleanup |
| [Data/Imports/RollMove/README.md](Data/Imports/RollMove/README.md) | Одноразовое восстановление и сверка каталога RollMove |

`docs/releases/` содержит встроенные объявления конкретных выпусков. Эти файлы являются ресурсами приложения, а не временными артефактами, поэтому должны оставаться в Git.

## Выпуски

Любая пользовательская функция, заметное UX-изменение, исправление ошибки или изменение поведения до завершения работы получает запись в [CHANGELOG.md](CHANGELOG.md). Это единый источник данных для страницы «Что нового?». Если выпуск отправляется в Telegram, рядом добавляется отдельный краткий текст в `docs/releases/` и регистрируется как встроенный ресурс проекта.

Чистый рефакторинг и обслуживание репозитория без пользовательского эффекта отдельной записи не требуют.

## Развёртывание

Контейнер слушает `PORT`, который предоставляет платформа; локальный fallback — `8080`. Production использует PostgreSQL и webhook. Порядок обновления базы, включая обязательную остановку старых экземпляров при несовместимой миграции, описан только в [docs/database-rollout.md](docs/database-rollout.md).

Реальные BGG, Telegram, BG Stats и production-копия базы не проверяются модульными тестами. Их необходимо проверять отдельными staging-сценариями из общего чек-листа.
