# Current OyinQ follow-up work

`AGENTS.md` is the architecture source of truth. This file records only current follow-up work.

- Validate the additive Club/Camp migration against a production backup before deployment.
- Deploy the additive administrator migration with at least one `Administration__BootstrapTelegramUserIds` value, verify the administrator list in the Mini App, then remove the bootstrap variable unless permanent recovery access is desired.
- Observe live Telegram webhook, Mini App authentication, BGG, and PostgreSQL behavior after deployment; mocked tests do not prove external integrations.
- Audit retained legacy database rows (`GameSessions`, `CollectionImports`, `GameInterests`, `GameCopies`, and participant legacy registration fields) with production data owners before designing a destructive retirement migration.
- Validate migration `20260829000852_StabilizeClubCampMiniApp` against a production backup. It is additive and retains all legacy production objects.
- Live-test prepared Mini App peer selection and the native DM fallback against supported Telegram clients; mocked/static checks do not prove client behavior.
- Observe Camp BGG import leases, retries, cancellation, and large-collection duration against live BGG before tuning worker intervals.
- Add persisted gathering reminders only when separately approved; do not infer automatic cancellation or attendance.
