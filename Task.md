# Current OyinQ follow-up work

`AGENTS.md` is the architecture source of truth. This file records only current follow-up work.

- Validate the full additive migration batch through `20260830210951_FinalConsistencyAndReliability` against a production backup before deployment. Review duplicate active-job reconciliation before applying the new partial unique indexes.
- Deploy the additive administrator migration with at least one `Administration__BootstrapTelegramUserIds` value, verify the administrator list in the Mini App, then remove the bootstrap variable unless permanent recovery access is desired.
- Observe live Telegram webhook, Mini App authentication, BGG, and PostgreSQL behavior after deployment; mocked tests do not prove external integrations.
- Audit retained legacy database rows (`GameSessions`, `CollectionImports`, `GameInterests`, `GameCopies`, and participant legacy registration fields) with production data owners before designing a destructive retirement migration.
- Review `20260831002206_ExactCampAttendanceDates` against a PostgreSQL backup before production. Existing registrations intentionally receive no inferred dates and must be completed by their owners.
- Live-test prepared Mini App peer selection and the native DM fallback against supported Telegram clients; mocked/static checks do not prove client behavior.
- Observe Camp BGG import and Club metadata-refresh leases, retries, callback duplicate resolution, cancellation, and large-collection duration against live BGG before tuning worker intervals.
- Generate and review `Data/Imports/RollMove/club-collection.v2.json` in an environment with `BoardGameGeek__ApiToken` by running `dotnet run --project oyinQ.Bot.csproj -- --generate-rollmove-recovery`, then verify exact base/expansion membership against v1 before committing the artifact.
- Verify the due-gathering lifecycle live: minimum reached becomes Completed; below minimum is hard-deleted, its Telegram announcement is cleaned up, and affected users receive a best-effort DM. Do not describe the underfilled path as automatic cancellation.
- Complete and live-verify the BotFather checklist in `docs/botfather-setup.md`: Main Mini App `/app/`, safe previews, public `/privacy`, profile artwork, and native light/dark splash configuration.
- Confirm command propagation in real clients: private `/start`, `/menu`, `/help`, `/privacy`, `/admin`; managed and unmanaged group `/oyinq`; no private-only group suggestions.
- Decide separately whether to keep `@OiynQ_bot` or migrate to a new clean username. Do not change the production bot without an approved migration.
- Keep gathering reminders deferred until separately approved; never infer attendance outcomes.
