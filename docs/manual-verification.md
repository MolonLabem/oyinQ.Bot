# OyinQ manual verification

Run against an approved staging PostgreSQL database and real Telegram groups.

1. Open an existing Club as a member; confirm there is no registration gate.
2. Create Club and Camp communities with Telegram native chat selection; reject a reused chat.
3. As Super Admin, open one chat's `Администраторы`, add a current Telegram administrator with native user selection, and confirm there is no raw-ID or Super Admin grant field.
   - Before approval, verify that the Telegram administrator sees only the chat name and `🔒 Доступ не выдан`; after approval verify only that chat's data/actions are visible.
   - Remove the Telegram role and confirm old Mini App controls immediately return 403.
   - Forge a request for another community key/Club/Camp ID and verify it returns 403. Revoke the permission and repeat from an already-open Mini App.
4. Browse/search the Club collection and verify Russian type/category labels on cards, details, and pickers.
5. Add a BGG game with expansions, then import a BGG username collection and verify existing games/expansions remain while missing owned items are added. Close/reopen the Mini App during the import and confirm the persisted job continues.
6. Export JSON, re-import it, and verify stale revision conflict handling.
7. Search BGG by exact name, prefix, whole word, and substring; verify ranked lightweight results, years, and “Показать ещё”.
8. Create a Club gathering from the collection and another from a BGG link; confirm neither mutates the legacy global catalog.
9. Create a Camp from a Club; change the Club and confirm the Camp base snapshot stays unchanged.
10. Activate the Camp and register with city, accommodation choice, and several exact dates.
11. Reopen registration through “Редактировать регистрацию”; confirm saved exact dates are restored.
12. Try to create or join a Camp gathering on an unselected date; verify rejection, then select the date and retry.
13. As an organizer, try to remove a date containing an active future gathering; verify the edit is blocked.
14. As a participant, remove such a date; verify the affected-gathering summary, explicit confirmation, withdrawal, and one ordered promotion.
15. Cancel Camp registration; verify only that Camp’s contributions/legacy import work/future participation are removed. Persistent ownership, profile imports, history and other communities remain.
16. Start a large BGG import, close/reopen the Mini App, and verify the persisted job continues.
17. In Profile import selection, test expansion-without-base warning and additive ownership; for a pending legacy Camp import also test explicit base-library duplicate resolution.
18. Add a personal game already present in the Camp base library; ownership is saved without an implicit Camp contribution. Select Camp availability explicitly.
19. Verify a base-library-only game has no personal “Точно привезу” prompt; an explicit personal duplicate does.
20. Have two participants contribute one game; verify one catalog item, both providers, and clickable username profiles where public.
21. Verify users without a public username remain readable non-links in provider and gathering participant lists.
22. Create a gathering with an empty description; confirm taxonomy is shown separately and not copied into description.
23. Fill a gathering, create an ordered waitlist, leave a confirmed place, and verify exactly one promotion and a persisted notification intent; verify worker delivery separately.
24. Close/reopen/edit a gathering; reject a maximum below confirmed count and unsafe expansion selection. Increase capacity with a waitlist and verify ordered promotions happen before a newcomer or guest can take the seats.
25. Manually cancel a gathering and simulate Telegram publication failure; verify the row remains in History → Отменены with retryable publication state.
26. Let an underfilled gathering pass its start; verify it remains in cancelled history with reason «Не набралось достаточно участников», preserves its roster, updates its Telegram announcement, and notifies the organizer and confirmed participants once.
27. Switch rapidly between Upcoming, History, Completed, and Cancelled; verify each request returns only its canonical scope, including while an older cached Mini App or backend instance is still serving the compatible legacy parameters.
28. Inspect the group announcement: organizer/participants are escaped direct mentions and the same message is edited in place.
29. Test light/dark themes, narrow safe-area layout, responsive desktop layout, and fullscreen enter/exit labels on supported Telegram clients.
30. Restart once in webhook mode and once in Development long-polling mode; confirm structured startup logs report five private commands, one group command, and the private Mini App menu URL without exposing the token.
31. In a managed group type `/`; verify only `/oiynq — Открыть OyinQ` is suggested. Run it and verify the response names the correct Club/Camp and opens the contextual flow through the bot DM.
32. In an unmanaged group run `/oiynq` as a normal user and as a global administrator. Verify only the administrator sees an admin entry action and no community is created.
33. In a private chat verify `/start`, `/menu`, `/help`, `/privacy`, and authorization-checked `/admin`; verify `/privacy` opens the production `/privacy` URL in a normal browser without Telegram `initData`.
34. Create one new Club and one new Camp through peer selection. Verify each group gets exactly one onboarding notice. Simulate send failure and verify creation remains committed and the administrator sees the warning. Restart and verify no notice repeats.
35. Confirm managed groups have no persistent reply keyboard or permanent Open button. Verify native peer-selector fallback keyboards remain private and are removed after use.
36. Complete the manual Main Mini App, previews, privacy URL, description picture, and splash checklist in `docs/botfather-setup.md`; test the bot profile Open App action in current Telegram mobile and desktop clients. From a group gathering announcement, press **Открыть сбор** and verify the Mini App opens that gathering directly without opening the bot private chat or sending `/start`.

## Первый вход, профиль и постоянная коллекция

1. На staging примените новую миграцию к копии базы со вкладами одного участника в нескольких кэмпах. Сравните число регистраций, вкладов, Bringing, участий и сохранённые JSON до/после; они не меняются. В личной коллекции — одна запись на участника/BGG ID/тип. Повторный запуск приложения не создаёт дубли. Неизвестные ID не восстанавливаются по названию.
2. Пользователь, никогда не писавший боту, открывает Club-сбор из группы: карточка открывается и запись работает без «Участник не найден». Повтор запроса не создаёт второго Participant/участия. Недействительный initData не создаёт Participant. Смените имя/username в Telegram: они обновятся, PreferredDisplayName сохранится.
3. До личного запуска видны текст «Чтобы получать уведомления о сборах, один раз запустите бота.» и кнопка «Запустить OyinQ». Пройдите /start → Mini App: открывается тот же сбор. После обновления CTA исчезает. Mini App сам не проставляет PrivateChatStartedAt. Ошибка getMe не ломает карточку: есть инструкция отправить /start.
4. Свежий пользователь кэмпа всё ещё обязан зарегистрироваться и выбрать день сбора. Отменённые/завершённые/закрытые сборы не принимают запись; чужие сообщества и административные запросы не обходят проверки доступа.
5. В Профиль → Моя коллекция добавьте игру вручную и импортируйте BGG Owned. Выберите часть базы/дополнений, подтвердите дважды: только выбранные ID добавлены однократно, ручные и невыбранные ранее сохранённые игры остались. Закройте и откройте приложение во время загрузки и выбора: задание возобновляется. Старый `tab=mine&import=...` продолжает прежний черновик.
6. Зарегистрируйтесь на два кэмпа: коллекция одинакова, но оба каталога не получают её автоматически. Отметьте игру Bringing только в первом: во втором она не обещана. Отмените регистрацию: личная коллекция осталась, вклад первого кэмпа удалён. Удаление владения при активном вкладе просит сначала убрать отметку.
7. В Club-поиске проверьте «Есть в клубе», «Есть у вас», «Есть в клубе · Есть у вас» и отсутствие дубликатов ID. Второй пользователь не видит ваши личные игры. Экспорт Club.CollectionJson не меняется от личных операций. Camp-каталог по-прежнему использует только базу и event-вклады.
8. На узком экране проверьте три внутренние вкладки профиля; нижняя навигация — Сборы/Игры/Профиль. Календарь сохраняет межсообщественные переходы и значок организатора. Даты/город/проживание/имя регистрации находятся в явно обозначенных настройках конкретного кэмпа.
9. Проверьте `/oiynq`, `/OIYNQ@CurrentBot` и совместимый `/oyinq`, включая `topic`. В новой помощи и onboarding рекламируется только `/oiynq`; прежние ключи localStorage сохраняются.
10. Сверка BGG уже сохранена в `Data/Imports/RollMove/reconciliation-report.json`. Добавление в аккаунт владелец выполняет вручную; после него можно повторить read-only сверку аккаунта. Production-база и бот этой проверкой не обновлялись.

## Время кэмпа, планирование и записи партий

Выполните отдельный сценарий: [проверки новых функций](planning-notifications-plays.md#ручная-проверка). Старое название фильтра «Сыграны» заменено на «Завершены»; факт партии проверяется отдельно.

## Финальная проверка выпуска 2026-09-04

Проверить пункты 9–14 в [планировании и партиях](planning-notifications-plays.md): отрицательный исход без партии, фактический состав и несколько ссылок BG Stats, внешнюю игру с дополнениями и явным владением, атомарность локальных изменений, предпросмотр рассылки и повтор только ошибочных получателей. Общий результат и ограничения: [отчёт выпуска](releases/2026-09-04-review.md). Текст для Telegram: [объявление](releases/2026-09-04.md).
# Стабилизация после аудита

1. Откройте приложение пользователем без доступных сообществ: перейдите в «Профиль», проверьте коллекцию, настройки имени/уведомлений и пустой календарь. Действий кэмпа быть не должно.
2. Выключите ещё не доставленное напоминание и включите обратно до начала сбора: оно должно снова стать допустимым, но уже доставленное не повторяется.
3. Отмените сбор до первого личного запуска бота получателем повышения: после `/start` сообщение об освободившемся месте не приходит.
4. Исправьте фактический состав партии после добавления ссылки игроком: автор видит и может удалить собственную ссылку, не получает доступа к чужим ссылкам.
5. В рассылке отличайте «Подготовка», «Отправляется», «Ошибка» и «Проверьте чат вручную». Повтор подготовки доступен, неизвестный результат отправки защищён от повтора. Не публикуйте реальную рассылку ради проверки без отдельного разрешения.
6. Перед выпуском выполните [интеграционные и clean-checkout проверки](stabilization-verification.md).
# Длинные объявления сборов

Создать сбор с фото, затем добавить восемь участников, гостя, длинное описание и несколько дополнений. Изменить время/описание: Telegram должен обновить прежнее сообщение, сохранив его ID. Подпись длиннее 1024 видимых символов заменяется краткой сводкой с числом игроков, гостей и дополнений; полный состав доступен по кнопке «Открыть сбор». Повторить для объявления, ранее опубликованного текстом, и для сообщения старше двух суток. Имена с `<`, `&` и emoji не должны повреждать HTML. При реальном отказе Telegram Mini App показывает, что сбор сохранён, а объявление может содержать прежние данные; повтор обновления не создаёт другое сообщение.


# Коллекции и компактный профиль

1. В профиле нет блока «Что дальше»; календарь и его переходы в сборы работают.
2. Базовая игра с принадлежащими участнику дополнениями занимает одну карточку. «Дополнения» раскрывает отдельные кнопки удаления и Camp-доступность каждого дополнения. Удаление базы не удаляет владение дополнениями: они остаются самостоятельными строками.
3. Поиск дополнения раскрывает его под базой. Проверить несколько официальных родителей, старый ParentBggId и дополнение без имеющейся базы. Каталог и админская коллекция показывают только реально сохранённые дополнения; BGG при раскрытии не запрашивается.
4. В клубе наличие коробки — вторичная строка в каталоге и деталях сбора. В кэмпе сохраняются явные обещания «Я привезу».
