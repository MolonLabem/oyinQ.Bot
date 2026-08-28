# Current OyinQ backlog

`AGENTS.md` is the architecture source of truth. This file records only current follow-up work.

- Validate the additive Club/Camp migration against a production backup before deployment.
- Deploy the additive administrator migration with at least one `Administration__BootstrapTelegramUserIds` value, verify the administrator list in the Mini App, then remove the bootstrap variable unless permanent recovery access is desired.
- Observe live Telegram webhook, Mini App authentication, BGG, and PostgreSQL behavior after deployment; mocked tests do not prove external integrations.
- Audit retained legacy database rows (`GameSessions`, `CollectionImports`, `GameInterests`, `GameCopies`, and participant legacy registration fields) with production data owners before designing a destructive retirement migration.
- Add persisted, restart-safe gathering reminders and typed notification delivery only when product requirements are approved.
- Expand Mini App administration/statistics only from the Club/Camp and `GameGathering` model; never restore the Telegram reply-keyboard application UI.
