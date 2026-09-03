# Current OyinQ follow-up work

`AGENTS.md` is the architecture source of truth. This file records only current follow-up work.

- Validate the clean baseline and forward migrations through `20260903073138_AddGatheringGuests` on a production clone before deployment. Existing gatherings must acquire zero guest rows without changing participant data.
- Deploy with the owner in `Administration__SuperAdminTelegramUserIds`, then grant normal administrators from each chat's `Администраторы` section.
- Observe live Telegram webhook, Mini App authentication, BGG, and PostgreSQL behavior after deployment; mocked tests do not prove external integrations.
- Live-test prepared Mini App peer selection and the native DM fallback against supported Telegram clients; mocked/static checks do not prove client behavior.
- Observe Camp BGG import and Club metadata-refresh leases, retries, callback duplicate resolution, cancellation, and large-collection duration against live BGG before tuning worker intervals.
- Review future `Data/Imports/RollMove/club-collection.v2.json` refreshes by running `dotnet run --project oyinQ.Bot.csproj -- --refresh-club-collection` with `BoardGameGeek__ApiToken`; compare source-ID omissions and BGG-classified expansion parents before committing the artifact.
- Verify the due-gathering lifecycle live: minimum reached becomes Completed; below minimum is hard-deleted, its Telegram announcement is cleaned up, and affected users receive a best-effort DM. Do not describe the underfilled path as automatic cancellation.
- Complete and live-verify the BotFather checklist in `docs/botfather-setup.md`: Main Mini App `/app/`, safe previews, public `/privacy`, profile artwork, and native light/dark splash configuration.
- Confirm command propagation in real clients: private `/start`, `/menu`, `/help`, `/privacy`, `/admin`; managed and unmanaged group `/oyinq`; no private-only group suggestions.
- Decide separately whether to keep `@OiynQ_bot` or migrate to a new clean username. Do not change the production bot without an approved migration.
- Keep gathering reminders deferred until separately approved; never infer attendance outcomes.
