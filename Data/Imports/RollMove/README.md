# Roll-Move initial collection migration

This directory contains the reviewed inputs for the one-time migration from the Tesera collection of user `John90` to the dedicated BoardGameGeek account `RollMoveClub`.

Tesera is not a runtime OyinQ integration. The application synchronizes only from the BGG account after the initial migration.

## Audit status

`match-audit.csv` accounts for all 363 Tesera collection entries:

- 303 unique canonical BGG items were uploaded to and verified in the `RollMoveClub` Owned collection.
- 58 Tesera entries have no resolved BGG ID and remain `needs-review`.
- The three owned Similo decks resolve to the single canonical BGG item `268620`; one row records the upload and two rows record the duplicate source identities.
- Tesera BGG value `5179628` for `Similo: Мифы` was corrected to canonical item `268620`.
- Tesera BGG value `215545` for `Jackal: Treasure Island` redirects to canonical item `119729`.

The BGG-backed enrichment resolved 210 base games and 93 expansions. Eight expansions have no officially linked owned base and are recorded as `excluded-orphan-expansion`; the other 85 expansions produce 91 nested links because official BGG relationships attach some expansions to multiple owned bases.

`bgg-owned-ids.csv` is the semicolon-delimited first upload batch containing only the 303 unique, non-conflicting IDs. It contains no credentials.

`club-collection.v1.json` is the validated recovery snapshot generated exclusively from BGG metadata. It contains 210 base games and 91 nested expansion links representing 85 unique owned expansions.

## Operator workflow

1. Resolve the remaining 58 review-only rows separately; do not infer IDs from translated names alone.
2. Keep the server-side `BGG_API_TOKEN` outside source control. The BGG account password is never used or stored by OyinQ.
3. Preview the `RollMoveClub` collection in Mini App administration. Review removals, metadata changes, and orphan expansions before applying it.
4. Treat `club-collection.v1.json` as the initial audited recovery snapshot. Normal later synchronization updates PostgreSQL only and does not rewrite this file automatically.

Do not commit the BGG password, email address, application token, browser profile, or uploader logs.
