# Administrative recruitment requests

Club and Camp settings expose an explicitly confirmed request for the same community recruitment digest used by organizers. `RecruitmentDigestService.RequestAsAdminAsync` calls canonical `IAdminAuthorizationService` for the exact community on every request. Global Super Admin access requires no membership; normal admins require both the persisted permission and live Telegram administrator status.

Admin requests do not require an organizer-owned trigger gathering. They require an active, undeleted community (an active, unexpired Camp) and at least one open upcoming gathering with free seats including guest occupancy. Camp administrator requests cover all upcoming Camp gatherings; Club administrator and organizer requests retain the 36-hour horizon. Organizer triggers must still be below desired occupancy.

`RecruitmentDigest.IncludeAllUpcoming` persists the Camp administrator scope through queueing, preparation, ranking, formatting and send-boundary revalidation. The additive `CampAdminRecruitmentScope` migration defaults existing rows to false. The 36-hour expiration of a queued request remains a delivery freshness limit, independent of the gathering horizon.

Both routes reserve the same community cooldown and outbox under `CommunityMutationLock`. Existing worker preparation, current-content checks, destination resolution and ambiguous-delivery protection apply unchanged. There is no scheduled broadcast and no administrative cooldown bypass.

Mini App nested gathering, game and admin screens use Telegram's header back button with an in-page fallback outside Telegram. The bot username in Telegram's bottom chrome is not application HTML and has no supported hide control; do not obscure it or force fullscreen to remove it.

## Shared candidate selection and CI verification

`GatheringRecruitment.CandidatePredicate` owns the upcoming time/status rule for both SQL selection and in-memory ranking. `LoadCandidatesAsync` is shared by request validation and both worker preparation checks. Change this owner when changing the eligibility window; do not add endpoint or worker copies. Organizer eligibility, Camp administrator scope, cooldown, and request expiration remain distinct policies.

CI provisions a disposable PostgreSQL service and requires `OYINQ_TEST_POSTGRES`. With `OYINQ_REQUIRE_POSTGRES_TESTS=true`, missing configuration fails discovery. The TRX report guard additionally requires every discovered PostgreSQL integrity test to pass. Local runs may omit PostgreSQL, but skipped integration tests do not validate database behavior.
