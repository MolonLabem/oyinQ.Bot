# Roll-Move initial collection migration

This directory contains the reviewed inputs for the one-time migration from the Tesera collection of user `John90` to the dedicated BoardGameGeek account `RollMoveClub`.

Tesera is not a runtime OyinQ integration. `club-collection.v1.json` is a reviewed disaster-recovery/bootstrap snapshot, not a synchronization source. Restore it only through the Club collection JSON import for an empty or deliberately replaced Club; PostgreSQL `Club.CollectionJson` is authoritative during normal operation.

## Audit status

`match-audit.csv` accounts for all 363 Tesera collection entries:

- 340 unique canonical BGG items are verified in the `RollMoveClub` Owned collection.
- 22 Tesera entries have no defensible BGG match and remain `needs-review`.
- Seven owned Similo decks resolve to the single canonical BGG item `268620`; one row records the upload and six rows record duplicate source identities.
- Tesera BGG value `5179628` for `Similo: Мифы` was corrected to canonical item `268620`.
- Tesera BGG value `215545` for `Jackal: Treasure Island` redirects to canonical item `119729`.
- Tesera incorrectly linked `За бортом. 2-е издание` to Cascadia promo `477191`; that item was removed from Owned and the source row returned to `needs-review`.
- The Russian Lifeboat second-edition expansion bundle is represented by official expansion items `37036`, `85927`, and `105046`. The Star Realms: United bundle is represented by `208503`, `208501`, `202247`, and `208502`.

The BGG-backed enrichment resolved 219 base games and 121 expansions. Twelve expansions have no officially linked owned base and are excluded as orphans; the other 109 expansions produce 119 nested links because official BGG relationships attach some expansions to multiple owned bases. Bundle components are listed in `additionalBggIds`, so the audit still has exactly one row for each Tesera source entry.

`bgg-owned-ids.csv` is the semicolon-delimited reviewed set of 340 unique Owned IDs. It contains no credentials.

`club-collection.v1.json` is the validated recovery snapshot generated exclusively from BGG metadata. It contains 219 base games and 119 nested expansion links representing 109 unique owned expansions.

## Operator workflow

1. Resolve the remaining 22 review-only rows separately; do not infer IDs from translated names alone.
2. Keep the server-side `BGG_API_TOKEN` outside source control. The BGG account password is never used or stored by OyinQ.
3. Preview the `RollMoveClub` collection in Mini App administration. Review removals, metadata changes, and orphan expansions before applying it.
4. Treat `club-collection.v1.json` as the initial audited recovery snapshot. Normal later synchronization updates PostgreSQL only and does not rewrite this file automatically.

Do not commit the BGG password, email address, application token, browser profile, or uploader logs.
