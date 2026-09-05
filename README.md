# OyinQ

OyinQ — Telegram-бот и Mini App для клубов настольных игр и кэмпов. Приложение ведёт каталоги и личные коллекции, помогает создавать сборы и записываться на них, хранит историю подтверждённых партий и предоставляет администраторам управление сообществами.

Проект состоит из одного приложения ASP.NET Core на .NET 10, React Mini App и PostgreSQL. Telegram остаётся транспортом и точкой входа; основные пользовательские и административные сценарии находятся в Mini App.

## Основные понятия

- `OyinQCommunity` — привязка приложения к Telegram-группе. Поддерживаются режимы `Club` и `Camp`.
- `Participant` и личная коллекция глобальны и не зависят от выбранного сообщества.
- Клуб хранит общий каталог. В кэмпе доступность игр формируется из базового каталога и явных обещаний участников привезти коробку.
- `GameGathering` — единая модель сбора. Будущие сборы показываются в расписании, завершённые и отменённые сохраняются в истории.
- Завершение по времени не доказывает факт партии. Организатор или администратор сообщества отдельно подтверждает результат и фактический состав.
- BGG — единственный внешний источник данных об играх. Недоступность BGG не должна ломать работу с уже сохранёнными данными.

Подробные архитектурные инварианты и обязательные правила разработки находятся в [AGENTS.md](AGENTS.md).

## Локальный запуск

Понадобятся:

- .NET SDK 10;
- Node.js 24 и npm;
- PostgreSQL;
- Telegram-бот для проверки реальной интеграции.

Минимальная конфигурация:

| Переменная | Назначение |
|---|---|
| `Database__ConnectionString` | Строка подключения PostgreSQL |
| `Telegram__Token` | Токен Telegram-бота |
| `Telegram__WebhookSecret` | Секрет webhook в production |
| `Telegram__PublicBaseUrl` | Публичный HTTPS origin без завершающего `/` |
| `Administration__SuperAdminTelegramUserIds` | Telegram ID глобальных администраторов через запятую |
| `BoardGameGeek__ApiToken` | Необязательный серверный токен BGG |

Секреты не должны попадать в Git. В Development бот использует long polling; production должен использовать webhook.

Запуск backend:

```powershell
dotnet restore oyinQ.Bot.slnx
dotnet run --project oyinQ.Bot.csproj
```

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
| [docs/wishlist-recruitment.md](docs/wishlist-recruitment.md) | Вишлист и групповые напоминания о наборе игроков |
| [Data/Imports/RollMove/README.md](Data/Imports/RollMove/README.md) | Одноразовое восстановление и сверка каталога RollMove |

`docs/releases/` содержит встроенные объявления конкретных выпусков. Эти файлы являются ресурсами приложения, а не временными артефактами, поэтому должны оставаться в Git.

## Выпуски

Любая пользовательская функция, заметное UX-изменение, исправление ошибки или изменение поведения до завершения работы получает запись в [CHANGELOG.md](CHANGELOG.md). Это единый источник данных для страницы «Что нового?». Если выпуск отправляется в Telegram, рядом добавляется отдельный краткий текст в `docs/releases/` и регистрируется как встроенный ресурс проекта.

Чистый рефакторинг и обслуживание репозитория без пользовательского эффекта отдельной записи не требуют.

## Развёртывание

Контейнер слушает `PORT`, который предоставляет платформа; локальный fallback — `8080`. Production использует PostgreSQL и webhook. Порядок обновления базы, включая обязательную остановку старых экземпляров при несовместимой миграции, описан только в [docs/database-rollout.md](docs/database-rollout.md).

Реальные BGG, Telegram, BG Stats и production-копия базы не проверяются модульными тестами. Их необходимо проверять отдельными staging-сценариями из общего чек-листа.
