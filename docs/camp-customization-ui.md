# Camp customization and UI audit (2026-10-03)

Camp creation and editing share `CampConfigurationEditor`; participant registration and already-registered views share `CampInformation`, `CampRegistrationFields` and `CampQuote`. `CampConfigurationRules` owns normalization, answer validation, itemized pricing and saved-quote preservation. The Mini App sends choices, never an authoritative total.

## Consumer map

| Consumer | Contract and verification |
| --- | --- |
| Admin creation / editing / overview | Versioned configuration, shared sections, exact local dates and timezone; questions and option IDs persist through save/read/edit. Creation validates configuration and local times before consuming the Telegram selection ticket. |
| Registration gate / profile settings | Description, HTTPS map link, payment instructions, custom questions and quote. Details remain accessible after registration. Required answers participate in canonical registration completeness. |
| Camp catalog, wishlist, providers, gathering participation | Existing `CampParticipationPolicy` checks also validate required custom answers; no separate client-only registration gate. |
| Own registration GET / PUT / quote POST | Current community membership required; answers and saved quotes belong to the authenticated participant. Supplied participant IDs or totals do not delegate ownership or change server prices. Quote POST is read-only and accepts incomplete required answers for a live preview. |
| Authorized admin roster | Question labels and presented answers, plus each participant's saved amount and currency. Existing scoped admin authorization still protects every read/export/send. |
| Human CSV / XLSX / Telegram roster | Dynamic question columns and saved total/currency. XLSX amounts are numeric; answers remain text. CSV guards formula-leading text. Telegram keeps bounded messages and offers files when an answer is too long. |
| Technical CSV snapshot | Appends `registration_data` and `camp_configuration`, preserving existing column order and saved JSON. |
| Camp create / edit, gathering create / edit, played-party end | `DateTimeField` owns native date plus explicit 00–23 hour / 00–59 minute controls. Existing local timezone, Camp interval and DST rejection rules remain server authority. |
| Gathering/game/profile lists, conflict messages, import/admin timestamps | Central formatters use `hourCycle: h23`; backend presentation/export format strings already use `HH:mm`. No native time or datetime-local inputs remain. |

## Pricing and answers

Configuration v1 supports description (6000 characters), location name (200), HTTPS location URL (1000), payment instructions (2000), optional pricing and at most 20 custom questions. Questions support short text, multiline text, one choice from 2–32 options, and checkboxes; each can be required. A required checkbox must be checked. Short text answers allow 400 characters and multiline answers 2000. IDs are stable and independent of labels. URLs and text render as escaped React content, never HTML.

Pricing supports KZT, RUB, USD, EUR, KGS and UZS, amounts from 0 to 10,000,000 with up to two fractional digits, optional daily base price, optional early price, housing surcharge, choice surcharges and checkbox surcharges. Housing and question surcharges may be daily. Daily quantities count selected attendance dates, including partial opening/closing dates. One-time quantities are one. Early price applies to registrations on or before the specified date in the Camp timezone, including that entire day. This is a registration deadline; payment instructions may describe the organizer's separate payment process. The app does not verify transfers or record paid status.

Saving snapshots the currency, total, line quantities/unit prices and calculation time. Changes to name or city preserve the saved quote even after the early deadline or an organizer's price update. Changes to selected dates, housing or normalized custom answers recalculate using current tariffs. The preview uses the same saved-quote preservation rule as PUT. An older client omitting answers preserves the participant's existing answers; it cannot bypass required questions for a new registration. Failed writes retain entered choices; stale preview responses cannot replace the current amount.

After the first registration, question definitions/options/surcharges are immutable under the Camp row lock. Camp description, location, payment instructions and base pricing remain editable; edits never rewrite other participants' saved answers or amounts. The existing one-active-community-binding-per-Telegram-group model is unchanged. This feature configures individual Camps, not a recurring-event series or automatic reuse of archived registrations.

The referenced Halloween sheet uses name/contact, games brought, collection link, wishes, hourly arrival choice, costume choice and a comment. No extra input type or enforced capacity was present in the sheet. The optional Halloween question preset adds arrival (00:00–23:00), costume and a multiline comment. Collections and wishes retain their existing canonical sections. The preset does not copy participant data, event text, payment details or spreadsheet links.

## UI principles and audit scope

Audited all screen modules under `MiniApp/src/pages` and their shared controls. They use the same Telegram light/dark tokens, typography, primary/ghost/danger actions, semantic badges, page headings, fields, error notices, loading/retry states, navigation and responsive surfaces. Broad catalogs/rosters retain their browsing width; forms and detail views use a narrower reading width. Responsive overrides serve these different purposes without introducing separate palettes.

The Camp admin form was the principal inconsistency: a dense flat card and a browser-controlled time input. Creation now uses shared `FormSection` surfaces for group, name/dates, event information, pricing, questions, collection and review. Question editors expand individually. Mobile review labels wrap without squeezing values into a narrow column. Existing date and time consumers now share the same themed control and 24-hour formatting.

Browser verification covers creation, registration, gathering creation, gathering list, game catalog, profile settings and admin roster at 320, 390, 768 and 1440 pixels in light/dark themes, with en-US browser locale and Pacific/Honolulu browser timezone. Assertions check viewport overflow, containment of visible controls, color scheme, absence of AM/PM/native datetime-local inputs, and exact local-time creation payloads. The browser uses bounded representative API fixtures; automated route/domain/PostgreSQL tests separately verify real validation, authorization and persistence. Native Telegram peer selection and device-specific iOS/Android date dialogs still require device testing.

## Migration and regressions

`20261003063649_CampCustomization` adds two non-null JSONB columns with empty version-1 defaults. It does not alter registration dates, housing, city, collection or gathering data. A populated previous-schema database is migrated twice, reopened, and checked for intact registration eligibility and persisted Unicode configuration/answers/quote. Existing visibility migration tests seed the current model then restore their historical schema, explicitly restoring the historical visibility flags before upgrading.

Regression coverage includes inclusive early-price cutoff in a non-UTC timezone; daily and option/housing amounts; malformed fields/URLs/money; required/unknown answers; read-only previews and forged totals; private-response authorization; save/read/edit and saved-price preservation; locked questions; roster/CSV/XLSX/Telegram serialization; formula-safe answers; stale previews; and exact shared local-time input including midnight and Camp bounds.
