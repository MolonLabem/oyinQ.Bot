# Game cards and personal collection modes

`MiniApp/src/components/GameCard.tsx` owns the full card layout: cover, name,
server-projected difficulty, contextual secondary text, players and duration.
Its context selects game facts or gathering occupancy. Interactive controls stay
outside the opening button; selection uses one native checkbox label for the card.

| Consumer | Context and data | Actions |
| --- | --- | --- |
| Club/Camp catalog | Catalog projection, including the viewer's personal items | Open authorized catalog details |
| Gathering list and game gatherings | Immutable gathering snapshot, occupied/max seats, community-local time, signup state | Open gathering; shared rules/status/warnings remain server-owned |
| Global/Club/Camp profile collection | Authenticated owner's saved base/expansion snapshot | Open personal details, independently select/delete, Camp box control |
| Club wishlist | Scoped catalog projection | Open catalog details and toggle wish |
| Camp wishlist and personal Camp wishes | Scoped wishlist projection | Open wishlist details; separate requests and box controls |

`GameOverview` and `GameDescription` share metadata presentation between catalog,
personal details and Camp wish details. Profile details do not call scoped catalog
endpoints, depend on registration, or require new provider data. Opening and closing
them preserves search, page, selection context and restores focus to the source card.
Expansion navigation uses only saved official parent relationships.

Difficulty comes exclusively from backend `complexityInfo`. Existing imports and
HTTP projection retain the canonical BGG classification. Older snapshots can lack
it; cards show `Сложность —` and personal details explain the missing saved value.
No difficulty is inferred from rating, title or player count. Rendering does not
trigger provider refreshes or rewrite historical snapshots. Missing player counts
and duration stay absent; they do not receive gathering scheduling defaults.

Search suggestions, import selection rows and administrative editors remain task-
specific rows/forms. They retain `GameMeta`, `ComplexityBadge`, `GamePicker` and
`ExpansionPicker`; full browse cards do not replace those interaction contracts.

Profile has one mode: browse, add or select. `ModeButton` uses the same highlighted
exit treatment for both active modes. Add mode retains its chosen game, expansion
selection and import job when hidden. Telegram Back closes the active mode before
leaving the profile. The existing import cancellation remains a separate action.

`Выбрать всё` selects the deduplicated set in the current search/filter across all
pages, including grouped expansions. Changing filters drops hidden selections.
Deletion still requires explicit confirmation and uses the existing owner-checked
endpoint one item at a time; accepted removals are excluded from retries. Camp
commitment restrictions and independently selectable expansions are unchanged.

Verification covers shared metadata and unknown values, gathering occupancy,
profile details without community/BGG, returning to the same page/focus, all-page
selection, cancellation, partial removal failures, imports, and Camp controls.
Visual QA uses fixture data in both themes, mobile/desktop widths and enlarged text.
