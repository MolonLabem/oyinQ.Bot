using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Notifications;

public static class NotificationPolicy
{
    public static bool CanReconsider(NotificationKind kind, NotificationState state) =>
        kind is NotificationKind.Reminder or NotificationKind.OrganizerMissingProvider
        && state is NotificationState.SuppressedByPreference or NotificationState.Expired;
    public static readonly int[] ReminderPresets = [0, 30, 60, 120, 360, 720, 1440];
    public static bool IsEssential(NotificationKind kind) => kind is NotificationKind.WaitlistPromotion
        or NotificationKind.GatheringTimeChanged or NotificationKind.GatheringCancelled or NotificationKind.GatheringFailed or NotificationKind.PostingTopicUnavailable;
    public static bool Allows(NotificationKind kind, NotificationPreferences preferences) => IsEssential(kind) || kind switch
    {
        NotificationKind.WishlistGathering => preferences.WishlistGathering,
        NotificationKind.GatheringFull => preferences.GatheringFull,
        NotificationKind.GatheringDetailsChanged => preferences.GatheringDetailsChanged,
        NotificationKind.OrganizerParticipantLeft => preferences.OrganizerParticipantLeft,
        NotificationKind.OrganizerReplacement => preferences.OrganizerReplacement,
        NotificationKind.OrganizerBelowMinimum => preferences.OrganizerBelowMinimum,
        NotificationKind.OrganizerMissingProvider => preferences.OrganizerMissingProvider,
        NotificationKind.ImportCompleted => preferences.ImportCompleted,
        NotificationKind.Reminder => preferences.ReminderLeadMinutes > 0,
        _ => false
    };
}
