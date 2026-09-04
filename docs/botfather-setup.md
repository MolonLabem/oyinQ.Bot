# BotFather setup for OyinQ

`Telegram:PublicBaseUrl` is the production HTTPS origin. In the checklist below replace
`{production-base-url}` with that value, without a trailing slash.

## Public bot identity

In `@BotFather` open `/mybots` → OyinQ → **Edit Bot** and verify:

```text
Name:
OyinQ

About:
Настольные игры, сборы и коллекции клубов и кэмпов.

Description:
OyinQ помогает находить настольные игры, создавать сборы и присоединяться к ним. Клубы ведут общую коллекцию, а участники кэмпов отмечают игры, которые привезут. Все основные действия доступны в удобном Mini App.
```

Profile picture remains a manual asset. Use the established OyinQ logo and check it at small size.

**Description Picture: Manual asset required.** No suitable safe repository image is currently
committed. It should be simple, contain no dense text, and contain no real Club/Camp data.

## Main Mini App

Telegram documents Main Mini App configuration under `@BotFather` → `/mybots` → bot →
**Bot Settings** → **Configure Mini App**. Enable the Main Mini App and set:

```text
{production-base-url}/app/
```

The root must remain context-free: OyinQ authenticates Telegram `initData`, lists accessible
communities, and exposes administration only to authorized administrators. Enabling the Main Mini
App provides the prominent profile **Open App** entry and permits profile previews. It makes the app
eligible for Telegram discovery surfaces; it does not guarantee featuring in the Mini App Store.

Upload previews made from safe local/demo data, preferably in this order:

1. Game catalog with search and filters.
2. Gathering list and a gathering card.
3. Profile `Моя коллекция` and explicit Camp availability.
4. Club/Camp administration overview.

Use real Mini App screenshots, consistent branding, and both representative light/dark views. Do
not commit or upload screenshots containing real participant names or other private production data.
No safe demo screenshot set is currently committed.

Configure Telegram's native loading screen at `@BotFather` → `/mybots` → OyinQ → **Bot Settings**
→ **Configure Mini App** → **Configure Splash Screen**. Use the current OyinQ logo and established
colors in light and dark variants where offered. Do not add an extra application splash delay.

Official references: [Bot features](https://core.telegram.org/bots/features),
[Main Mini Apps](https://core.telegram.org/api/bots/webapps#main-mini-apps).

## Privacy policy

After deployment, open `@BotFather` → `/mybots` → OyinQ → **Edit Bot** →
**Edit Privacy Policy** and set:

```text
{production-base-url}/privacy
```

The URL is public and is the canonical policy; `/privacy` in the private bot chat only links to it.

## Commands: code is the source of truth

Do not duplicate these lists manually in BotFather. Every application start configures them through
the Bot API, in both webhook and long-polling modes:

```text
Private chats: /start, /menu, /help, /privacy, /admin
Group chats:   /oiynq
```

The application also owns the private-chat Mini App menu button and synchronizes About/Description.
BotFather remains responsible for the Main Mini App, previews, profile/description pictures,
privacy URL, splash colors, and username.

## Current username and optional future migration

Current username: `@OiynQ_bot`. Public brand: `OyinQ`. The username transposes `i` and `y`.
Telegram documents that bot usernames normally cannot be changed after creation. Runtime deep links
therefore use `getMe()` and never hard-code this username.

Do not migrate automatically. If the owner later creates a clean username such as `@OyinQBot` (if
available), plan a separate migration:

1. Store the new token as `Telegram__Token`; never commit it.
2. Reconfigure profile, Main Mini App, previews, privacy URL, splash, commands, and webhook.
3. Add the new bot to every managed Club/Camp group and grant the required permissions.
4. Switch the production token and verify webhook delivery and group membership checks.
5. Tell users how to discover and start the new bot.

PostgreSQL participant Telegram user IDs and managed group chat IDs remain useful. Old recruitment
messages were authored by the old bot and generally cannot be edited or deleted by a different bot.

## Совместимость команды

Публичное имя — OyinQ; текущий username — @OiynQ_bot. В групповых командах BotFather и автосинхронизации рекламируется только `/oiynq`. Старый `/oyinq` временно распознаётся, включая `topic`, но не публикуется в подсказках. Проверяйте `/OIYNQ@CurrentBot` с текущим username. Все deep links получают username из runtime/getMe(), а не из документации.
