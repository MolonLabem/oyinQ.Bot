# Стабилизация после аудита A1–A10

Исправления выполнены в текущем цикле без изменения выбранной архитектуры. Прикладные проверки, постоянные regression tests и реальный PostgreSQL-прогон успешны. Рассылка, развёртывание, коммит и миграция рабочей базы не выполнялись. Docker image build пока не подтверждён: локальный Docker Desktop daemon недоступен.

## 1–10. Причины и исправления

| Находка | Причина | Исправление и доказательство |
|---|---|---|
| A1 | `[Rr]eleases/` скрывал обязательный EmbeddedResource | Добавлены исключения `!docs/releases/` и `!docs/releases/**`; документы находятся в Git-индексе. `scripts/verify-release-input.ps1` проверяет каждый EmbeddedResource на наличие, отслеживание и ignore. Сборка экспорта индекса успешна. |
| A2 | После BGG не перечитывался binding, удаление могло закончить перечисление сборов раньше нового INSERT | `CommunityMutationLock` блокирует строку binding в обоих сценариях. Создание перечитывает активность/удаление/mode; удаление перечисляет сборы после получения той же блокировки. Два PostgreSQL-теста проверяют оба порядка гонки. |
| A3 | В создание передавался `now`, захваченный перед BGG | Окончательное время читается после сетевой фазы и получения локальных блокировок, перед `GatheringRules.Create`. Постоянный тест двигает часы во время BGG lookup и подтверждает отказ без INSERT. |
| A4 | Отложенное повышение проверяло private capability, но не актуальное место | Dispatcher перечитывает gathering, активность binding и подтверждённый signup; ключ должен соответствовать текущему JoinedAt. Отмена, выход и повторная запись делают прежнее повышение Expired. Тесты покрывают все эти случаи и допустимую доставку. |
| A5 | Любое существование dedup key навсегда запрещало повторную оценку | `NotificationPolicy.CanReconsider` различает идентичность события и его пригодность к доставке. Planner может атомарно вернуть SuppressedByPreference/Expired reminder/provider notice в Pending, затем Dispatcher проверяет текущую семантику и настройки. Delivered/DeliveryUnknown сохраняют защиту. Unit- и PostgreSQL-тесты зелёные. |
| A6 | Expiry проверялся раньше Confirmed | После проверки владельца возвращается сохранённое подтверждение; срок проверяется только для неприменённого черновика. Повтор не применяет новую выборку. Проверено unit-тестом и новым PostgreSQL scope после expiry. |
| A7 | Удаление сначала требовало участия в исправленном фактическом составе | Remove находит точную связь reference → play → gathering → community и использует центральный CanRemove: автор либо организатор. GET показывает исключённому из фактического состава автору только его ссылки; UI использует CanRemove отдельно от CanShare. Постоянное воспроизведение зелёное. |
| A8 | Проверялась длина исходного URL вместо канонического | После trim, parse и проверки HTTPS/официального host проверяется длина AbsoluteUri. Общая MaxUrlLength используется сервисом и EF. Кириллический путь, разрастающийся до 2427 символов, отклоняется русской ошибкой до БД. |
| A9 | Подготовка и отправка имели общий ambiguous catch | Введена сохраняемая фаза Preparing. Ошибка/прерывание подготовки → Failed; перед отправкой подготовленный payload и destination получают границу Delivering. Явный отказ → Failed, успех → Delivered, неоднозначная отправка → DeliveryUnknown. Истёкшая Preparing не может продолжить отправку без conditional claim своего исходного attempt. UI получает CanQueue/CanRetry от сервера. Проверено fake HTTP и PostgreSQL recovery/concurrent-worker тестами. |
| A10 | App и Profile требовали выбранный Community даже для глобального ownership | Без сообщества доступны «Сообщества» и «Профиль» с коллекцией, календарём и настройками. Глобальные endpoints не требуют community ради аутентификации. Расписание/history по-прежнему фильтруют реально разрешённые сообщества; Camp controls доступны только с выбранным Camp. Проверено HTTP-тестом подписанного initData и тремя Mini App-тестами. |

## 11. Постоянные воспроизведения

Семь исходных сценариев перенесены из диагностического `.cs.txt` в `oyinQ.Bot.Tests/AuditRegressionTests.cs`: оба варианта CreationRechecksStateAfterProviderLookup, LatePrivateStartDoesNotDeliverPromotionForCancelledGathering, ReminderCanResumeAfterTemporarilyDisablingPreference, ConfirmedImportReplaysAfterDraftExpiry, ReferenceAuthorCanRemoveAfterRosterCorrection, NormalizedReferenceFitsDatabaseColumn. Намеренно красного исходника в docs больше нет. Длинный URL теперь проверяется как ожидаемая ошибка валидации, а не как успешно сохраняемое значение.

Дополнительно: девять проверок состояния уведомлений в NotificationDeliveryTests; подготовка выпуска в ReleaseAnnouncementTests; GlobalProfileApiTests с настоящим HTTP routing и initData; GlobalProfile.test.tsx с пустым контекстом; восемь PostgreSQL-тестов.

## 12. PostgreSQL

Использован PostgreSQL 18.4, отдельный временный кластер на loopback, без подключения к рабочей базе. Каждый тест создаёт уникальную базу и удаляет её в Dispose; фейки изолируют BGG/Telegram. Testcontainers не добавлен: доступные PostgreSQL binaries и существующий Npgsql оказались достаточны.

Восемь тестов в PostgreSqlStabilizationTests:

1. Удаление коммитится во время BGG lookup — создание отклоняется.
2. Создание удерживает lock — удаление ждёт и отменяет новый закоммиченный сбор.
3. Ошибка локального сохранения после ownership/contribution откатывает все три изменения; успешная попытка сохраняет их вместе.
4. Восемь конкурентных provisioning/upsert дают одного Participant и один item; отдельный дубль отвергается unique constraint БД.
5. Повтор Confirmed из нового scope после expiry возвращает исходный результат и не применяет изменённую выборку.
6. Suppressed reminder восстанавливается, конкурентные dispatchers отправляют один раз; Failed/CannotMessageUser возобновляются, Delivered/DeliveryUnknown защищены.
7. Реальная предыдущая схема с двумя Camp contributions и регистрацией мигрируется вперёд: ownership дедуплицирован, оба commitment и attendance сохранены, UTC bounds рассчитаны; повтор Migrate ничего не меняет.
8. Рассылка восстанавливается в новых scopes: ошибка подготовки допускает retry; конкурентные workers отправляют один раз; Delivered не повторяется; прерванный Delivering становится Unknown; прерванный Preparing становится Failed.

## 13. Уведомления

Pending — кандидат на delivery validation. Failed — повтор с существующим backoff/лимитом. CannotMessageUser — ожидание настоящего более позднего private update. SuppressedByPreference/Expired у планируемых reminder/provider событий допускают повторную оценку planner. Перед фактической отправкой проверяются текущие настройки и применимая предметная семантика. Delivering имеет lease, неопределённый результат/прерывание → DeliveryUnknown. Delivered и DeliveryUnknown автоматически не восстанавливаются. Историческая отмена не подчинена проверке Upcoming, применяемой к повышению/напоминанию.

## 14. Создание и конкуренция

Один use case остаётся в GatheringManagementService. Идентичность разрешается каноническим сервисом; ранняя Camp-проверка отклоняет заведомо недопустимые запросы без BGG. Метаданные загружаются без DB transaction. Затем начинается локальная транзакция, блокируется community binding, для Camp — Camp, для ownership — Participant. После перечитывания mutable state применяются существующие Camp/attendance policies и текущие часы/GatheringRules. Ownership, commitment, gathering и notification intents коммитятся вместе. Групповая публикация остаётся после коммита.

## 15. Глобальный профиль

При пустом списке или отсутствии выбранного сообщества доступна нижняя навигация «Сообщества / Профиль». Коллекция допускает просмотр, поиск, импорт, manual add/remove; настройки — имя и уведомления. Календарь показывает только разрешённое расписание, в том числе пустое. Фальшивое глобальное сообщество не создаётся. Существующие legacy route/storage ключи сохранены. Возврат из календаря в глобальный профиль не требует подстановки community.

## 16–17. Удаление старого и согласованность

Убрана зависимость глобальных Profile/Calendar/History endpoints от произвольного community. Для глобального manual add больше не используется Camp request contract. Удалён диагностический красный `.cs.txt`. Новый `CommunityMutationLock` общий для двух конфликтующих сценариев; URL limit и delete permission централизованы; retry eligibility принадлежит backend; подготовленный групповой send использует тот же destination/fallback механизм, что обычные публикации.

Повторный поиск сохранил единственный прикладной `Participants.Add` в ParticipantIdentityService. `/oyinq` остался только совместимым routing alias. Новых BGG loaders/search stacks, сериализаторов или параллельного catalog/gathering use case не создано. Прямые Telegram сообщения остаются у интерактивного адаптера; автоматические DM-уведомления проходят через notification subsystem. Переписывания старых snapshots не было.

## 18. Отложенная производительность

Подсчёт популярности читает широкую историю PlayRecord; подготовка рассылки последовательно обращается к Telegram. Эти пункты оставлены техническим долгом до измерений, без новых cache/read-model/concurrency подсистем в стабилизации.

## 19–20. Результаты проверки

- Рабочее дерево: .NET Release build/test успешны; 459 тестов, 0 failures, 0 skips, включая 8 PostgreSQL tests.
- Mini App: 73 теста в 17 файлах; `npm run check` и `npm run build` успешны.
- EF: pending model changes отсутствуют. Дополнительная миграция для этих исправлений не нужна; Preparing — добавленное целое enum-значение, существующие значения не сдвинуты.
- `git diff --check` и `git diff --cached --check` успешны после нормализации концов файлов.
- EmbeddedResource проверен скриптом; docs/releases виден в `git ls-files`, не игнорируется.
- Чистая копия создана через `git checkout-index` из подготовленного индекса в отдельную пустую папку. Локальные ignored/untracked исходники в неё не копировались. `dotnet restore`, Release build (0 warnings/errors), 459 тестов без пропусков, EF model check, `npm ci --offline --no-audit`, 73 Mini App теста/check/build успешны.
- Подготовлен индекс всего текущего цикла; пользовательский Task.md не изменялся и не включался в подготовку. Коммит не создан.
- `docker build -t oyinq-bot .` попытка выполнена, но daemon `dockerDesktopLinuxEngine` недоступен; контейнерная сборка не доказана.

## 21. Остаточные риски

Docker build необходимо повторить при работающем daemon. Не проверялись production migration, реальные Telegram permissions/topic errors и открытие BG Stats на устройствах. PostgreSQL-тесты подтверждают перечисленные сценарии, но не являются доказательством всех возможных гонок регистрации/редактирования/внешних API. DeliveryUnknown сознательно требует ручной проверки — автоматический повтор мог бы создать дубль. Готовность контейнерного развёртывания не объявляется до соответствующей проверки. Пользовательская рассылка не запускалась.
