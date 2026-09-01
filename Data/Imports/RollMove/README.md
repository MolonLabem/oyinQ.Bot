# Roll-Move initial collection migration

This directory contains the reviewed inputs for the one-time migration from the Tesera collection of user `John90` to the dedicated BoardGameGeek account `RollMoveClub`.

Tesera is not a runtime OyinQ integration. `club-collection.v1.json` is the reviewed historical migration snapshot. `club-collection.v2.json` is the current recovery/bootstrap snapshot rebuilt from the authoritative positive BGG IDs in only `club.json`, `guests.json`, `john.json`, and `sergei.json` under `BarinDwalin/board-games-club/public/data/collections`. Restore a snapshot only through the Club collection JSON import for an empty or deliberately replaced Club; PostgreSQL `Club.CollectionJson` is authoritative during normal operation.

The v2 refresh reads IDs from the four source documents but never copies their game metadata. It fetches current metadata, versions, and item classification from BGG in throttled batches, deduplicates by canonical BGG ID, and writes through `ClubCollectionSerializer`. `name` prefers the single confidently associated Russian-version canonical title while `originalName` retains the primary BGG title; both base games and expansions use the centralized resolver. BGG-classified expansions are nested only under included official inbound parents; unresolved IDs and orphan expansions are omitted and reported.

## Audit status

`match-audit.csv` accounts for all 363 Tesera collection entries:

- 340 unique canonical BGG items are verified in the `RollMoveClub` Owned collection.
- 22 Tesera entries have no defensible BGG match and remain `needs-review`.
- Seven owned Similo decks resolve to the single canonical BGG item `268620`; one row records the upload and six rows record duplicate source identities.
- Tesera BGG value `5179628` for `Similo: Мифы` was corrected to canonical item `268620`.
- Tesera BGG value `215545` for `Jackal: Treasure Island` redirects to canonical item `119729`.
- Tesera incorrectly linked `За бортом. 2-е издание` to Cascadia promo `477191`; that item was removed from Owned and the source row returned to `needs-review`.
- The Russian Lifeboat second-edition expansion bundle is represented by official expansion items `37036`, `85927`, and `105046`. The Star Realms: United bundle is represented by `208503`, `208501`, `202247`, and `208502`.

The historical BGG-account enrichment resolved 219 base games and 121 expansions. Twelve expansions had no officially linked owned base and were excluded as orphans; the other 109 expansions produced 119 nested links. Those numbers describe v1 and the earlier RollMove account snapshot, not the current four-source v2 refresh.

`bgg-owned-ids.csv` is the semicolon-delimited historical reviewed set of 340 unique RollMove Owned IDs. It is retained as migration audit evidence and is not an input to the current v2 refresh.

`club-collection.v1.json` contains 219 base games and 119 nested expansion links representing 109 unique owned expansions. The current v2 snapshot contains 337 BGG-resolved base games from the four requested sources. The source IDs `215545` and `5179628` do not resolve to included BGG items, while `477191` is classified by BGG as an expansion without an included official parent; all three are therefore omitted instead of guessed or copied from Tesera metadata.

## Operator workflow

1. Resolve the remaining 22 review-only rows separately; do not infer IDs from translated names alone.
2. Keep the server-side `BoardGameGeek__ApiToken` outside source control. The BGG account password is never used or stored by OyinQ.
3. Use the explicit BGG username import in Mini App administration when adding an account to a Club. It is additive: current games and selected expansions are retained.
4. Refresh the current-version recovery snapshot with a server-side BGG token:

   ```powershell
   $env:BoardGameGeek__ApiToken = '<server-side BGG token>'
   dotnet run --project oyinQ.Bot.csproj -- --refresh-club-collection
   Remove-Item Env:BoardGameGeek__ApiToken
   ```

   The command reads only the four named source JSONs, extracts and deduplicates positive BGG IDs recursively, fetches BGG metadata through typed base/expansion requests, links expansions through official BGG parent relationships, and reports invalid, unresolved, and orphan items. Use `--source-base-url=<url>` only for a reviewed mirror or test fixture and `--output=<path>` for an audit run.
   To reconcile the completed document into PostgreSQL atomically, add `--apply-club-key=<community-key>`. The command locks the Club row, replaces only `Club.CollectionJson`, increments the revision only when content changed, and reports added/retained/removed IDs and expansion-link changes. Existing gathering snapshots and participation data are not rewritten.

   On a fresh single-Club installation, `--apply-only-club` resolves the target only when exactly one Club exists. It fails before mutation when the database contains zero or multiple Clubs, so an operator never has to guess a community key.
5. Normal metadata refresh updates PostgreSQL only and does not rewrite recovery files automatically.

To update localized/original names in existing PostgreSQL Club collections and Camp contributions without changing BGG membership or immutable gathering history, run:

```powershell
dotnet run --project oyinQ.Bot.csproj -- --refresh-bgg-names
```

The command loads all distinct stored BGG IDs, completes every batched BGG request before opening its database transaction, and then changes only stored display/original names. A confidently resolved Russian title updates the display name; when Russian data is missing or ambiguous, the existing display name remains untouched while a resolved primary BGG title is retained as `originalName`. Repeating the command is idempotent.

Do not commit the BGG password, email address, application token, browser profile, or uploader logs.
