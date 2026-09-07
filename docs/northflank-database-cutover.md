# Northflank PostgreSQL production cutover

Verified on 2026-09-07. Service: `oyinq/oyinqbot`; addon: `oyinq/oyinq-postgres`.
Deployed and local commit at verification: `4c29dd4b5b207d23e7cdc1d0da91fa7d2450ae3d`.

## Verified state

- Paused OyinQ and confirmed zero instances and no live service containers before SQL verification/configuration changes.
- PostgreSQL 18 addon is running with TLS enabled and external access disabled. Used authenticated Northflank CLI forwarding, never enabled public database access.
- Actual databases: `_1a9c43c1dbb4`, `neondb`, `postgres`.
- Restored production data and all 30 public tables are in `neondb`.
- Application-role TLS connection to `neondb` succeeded; role has no `CREATEDB`. No privilege changes were needed.
- Found duplicate `Database__ConnectionString` definitions: old Neon value in `oyinq-prod-secrets`, correct Northflank template in `oyinq-postgres`, both priority 10. Removed only the old definition, retaining Telegram/BGG secrets.
- The effective connection now has one source, `oyinq-postgres`, with zero overridden definitions. No service-level DB definition, `DATABASE_URL`, runtime secret files, or effective values containing `neon.tech` were present after cleanup. The `neon` substring in database name `neondb` is expected and does not identify a Neon connection.
- Resumed exactly one instance; deployment completed. Startup reported zero pending migrations and successful migration completion. No migrations were applied.
- `/health`, `/ready`, and `/app/` returned HTTP 200 after rollout. `/ready` executes PostgreSQL `SELECT 1` in the application.
- Post-start SQL showed eight private-address client connections to `neondb`, all using TLS. Verification used only SELECT/read-only transactions.
- MiniApp rendered its OyinQ entry screen in a browser; authenticated screens could not be tested without valid Telegram `initData`. It correctly displayed the expired/missing Telegram session message. This is not a complete authenticated smoke test.
- New startup logs contained no database/migration exceptions, `DbUpdateException`, unhandled exception, background-service failure, or notification-worker iteration failure. Telegram logged `Webhook configured`. The HTTP_PORTS override warning and `libgssapi_krb5.so.2` diagnostics were present; connections and readiness succeeded afterward.
- Final log review at 08:52:53 UTC covered 29 returned entries from the new startup (first entry 08:47:38 UTC; last entry 08:51:44 UTC). `/health` and `/ready` both still returned 200. This is a bounded cutover observation, not ongoing monitoring.
- Neon was not accessed or modified by this cutover. Neither database nor addon was deleted.

## Production row counts

| Table | Before resume | After resume |
|---|---:|---:|
| `__EFMigrationsHistory` | 12 | 12 |
| `Participants` | 86 | 86 |
| `GameGatherings` | 29 | 29 |
| `ParticipantCollectionItems` | 182 | 182 |
| `Camps` | 2 | 2 |
| `Clubs` | 2 | 2 |
| `CampRegistrations` | 24 | 24 |
| `CampBggImports` | 18 | 18 |
| `GameWishes` | 33 | 33 |

Additional post-start counts: `CampGameContributions` 67; `OyinQCommunities` 4.
These are cutover observations, not permanent assertions as production continues.
Only deployment configuration and documentation changed. `git diff --check` passed; no application build or test suite was rerun for this documentation-only repository change.

## Migration history

All entries have EF ProductVersion `10.0.10`, match the repository, and were already applied:

```text
20260901073247_CleanBaseline
20260901125924_ForumPostingTopics
20260901133621_CommunityDeletionAndTelegramPhotos
20260903073138_AddGatheringGuests
20260904072034_PersistentParticipantCollection
20260904080727_CampOperatingInstants
20260904081750_NotificationDelivery
20260904084352_GatheringPlayRecords
20260904092743_PlayOutcomesReferencesAndReleases
20260904131050_WishlistAndRecruitment
20260904182048_BgStatsPlayResults
20260905020448_BggImportProgressStages
```

## Authoritative runtime configuration

Key: `Database__ConnectionString`, defined only in secret group `oyinq-postgres`:

```text
Host=${NF_OYINQ_POSTGRES_HOST};Port=${NF_OYINQ_POSTGRES_PORT};Database=neondb;Username=${NF_OYINQ_POSTGRES_USERNAME};Password=${NF_OYINQ_POSTGRES_PASSWORD};SSL Mode=Require;Pooling=true;Maximum Pool Size=15
```

Resolved non-secret target: `primary.oyinq-postgres--668p7wnqfhrf.addon.code.run:5432`, database `neondb`.
The linked `NF_OYINQ_POSTGRES_DATABASE` still describes `_1a9c43c1dbb4`; it is intentionally not used by the connection template.
`Program.cs` reads `Database:ConnectionString` and passes it to EF's `UseNpgsql`; the installed Npgsql EF provider is `10.0.3`. The connection options were verified with Npgsql and the application. No code or custom data source was needed.

## Reverification

Use the current Northflank context and keep database access private:

```powershell
northflank pause service --projectId oyinq --serviceId oyinqbot --output json
northflank forward addon --projectId oyinq --addonId oyinq-postgres --skipHostnames
```

Connect a PostgreSQL client to the IP/port printed by the tunnel, with TLS and credentials supplied securely. Do not print credentials or use them in committed files. Connect to `postgres` first:

```sql
SELECT datname FROM pg_database WHERE NOT datistemplate ORDER BY datname;
```

Stop if `neondb` is absent. Then explicitly connect to `neondb`:

```sql
BEGIN READ ONLY;
SELECT current_database();
SELECT "MigrationId", "ProductVersion" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";
SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name;
SELECT COUNT(*) FROM "Participants";
SELECT COUNT(*) FROM "GameGatherings";
SELECT COUNT(*) FROM "ParticipantCollectionItems";
SELECT COUNT(*) FROM "CampRegistrations";
SELECT COUNT(*) FROM "GameWishes";
ROLLBACK;
```

Compare against migrations from the actual deployed commit. Stop for missing production data, unexpectedly ahead/mismatched history, or all historical migrations appearing pending. Do not initialize schema manually. Resume only after target/configuration checks:

```powershell
northflank resume service --projectId oyinq --serviceId oyinqbot --input '{"instances":1}' --output json
Invoke-WebRequest https://p01--oyinqbot--668p7wnqfhrf.code.run/health
Invoke-WebRequest https://p01--oyinqbot--668p7wnqfhrf.code.run/ready
```

Inspect only logs after the new startup timestamp. Do not paste raw logs or resolved runtime environments into reports. Stop the service immediately if it attempts to recreate the historical schema.

## Remaining manual validation

Open the MiniApp from Telegram using a real authenticated session. Verify existing profile and personal collection, communities, Camp registrations, Club/Camp catalogs, gatherings/details, wishes, and the authorized admin panel. SQL confirmed the underlying row counts, but does not prove those authenticated UI flows. Do not create/delete production records as smoke tests.

## Rollback

The previous Neon connection was preserved locally in a Windows DPAPI-encrypted file at `%TEMP%\oyinq-neon-rollback-20260907.dpapi`. It can only be decrypted under the original Windows user context. The temporary directory is not durable storage; retain this encrypted file securely while rollback is needed, or retrieve the existing connection securely from the unchanged Neon project. Never paste the decrypted value into logs, documentation, or Git.

1. Pause `oyinq/oyinqbot` using the command above and verify zero running instances.
2. Before rollback, account for writes accepted by Northflank since cutover. Neon is the older snapshot: a simple switch will not transfer those writes. Preserve/reconcile them deliberately; do not overwrite either database automatically.
3. Restore the previous Neon value into the **existing sole** `Database__ConnectionString` in `oyinq-postgres`. Do not recreate the duplicate in `oyinq-prod-secrets`. Use the Northflank secret editor without exposing the value.
4. Resume one instance, verify `/ready`, inspect new startup logs and production data.
5. Keep both databases intact until rollback and data reconciliation are resolved. No rollback was performed during this cutover.
