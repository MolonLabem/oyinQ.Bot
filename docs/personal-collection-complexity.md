# Personal collection difficulty backfill

New manual selections and profile/Camp BGG imports already carry complexity through
`BggComplexityParser` → `BggGameMapper` → `CollectionItemSnapshot` →
`ParticipantCollectionService`. Older saved ownership predates these optional fields.
`ProfileCollectionEndpoints` presents the owner's snapshot through
`ComplexityJsonPresentation`; `GameCard` and personal details consume `complexityInfo`.
Club catalog composition and later Camp box selections also consume personal snapshots.

Explicit maintenance command, using `Database__ConnectionString` and
`BoardGameGeek__ApiToken` supplied securely by the operator:

```powershell
dotnet oyinQ.Bot.dll --refresh-personal-complexity
dotnet oyinQ.Bot.dll --refresh-personal-complexity --apply
```

The default is a dry run. Both modes fetch only distinct BGG IDs with incomplete
personal complexity, through the existing typed Thing client (20 IDs per request,
throttling/retries, canonical average/poll rules). Base games and expansions must
match the saved item type. Missing or unrated provider results remain unknown and
are reported as `UnresolvedIds`; no difficulty is invented.

Apply fills missing fields for all owners in one local transaction after provider
I/O completes. It preserves known difficulty, unrelated JSON fields, snapshot
version, ownership, source, parent relationships and creation time. Conditional
updates skip rows edited or removed during the fetch; `ConcurrentlyChangedRows`
reports those rows for a later explicit retry. Re-running is safe. A provider error
before the transaction leaves the database unchanged. A later incomplete ownership
import preserves already known complexity.

No schema migration, HTTP server or hosted worker runs in this command. There is no
periodic refresh. Existing Camp contributions, Club documents, wishes, import
drafts and immutable gathering/play history are not rewritten. Existing Camp box
commitments and announcements are unchanged; subsequent selections use the updated
ownership snapshot.

Before production apply, verify the private database target (`neondb` for the current
Northflank installation), inspect the dry-run counts and preserve a rollback export
of the affected rows. Do not substitute the addon's generated empty database.
After apply, verify counts and unchanged unrelated metadata/history. Regression
coverage is in `PostgreSqlCollectionComplexityTests` and `GameComplexityTests`.
