# Administrative recruitment requests

Club and Camp settings expose an explicitly confirmed request for the same community recruitment digest used by organizers. `RecruitmentDigestService.RequestAsAdminAsync` calls canonical `IAdminAuthorizationService` for the exact community on every request. Global Super Admin access requires no membership; normal admins require both the persisted permission and live Telegram administrator status.

Admin requests do not require an organizer-owned trigger gathering. They require an active, undeleted community (an active, unexpired Camp) and at least one `GatheringRecruitment.IsRelevant` gathering in that community: open, within the next 36 hours, and with free seats including guest occupancy. Existing organizer trigger restrictions remain unchanged.

Both routes reserve the same community cooldown and outbox under `CommunityMutationLock`. Existing worker preparation, current-content checks, destination resolution and ambiguous-delivery protection apply unchanged. There is no scheduled broadcast and no administrative cooldown bypass.

Mini App nested gathering, game and admin screens use Telegram's header back button with an in-page fallback outside Telegram. The bot username in Telegram's bottom chrome is not application HTML and has no supported hide control; do not obscure it or force fullscreen to remove it.
