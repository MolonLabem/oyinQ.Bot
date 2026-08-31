namespace oyinQ.Bot.Features.MiniApp;

public static class CampParticipantPresentation
{
    public static string? RegistrationDisplayName(string? registrationName,
        string? participantName, string? telegramName) =>
        new[] { registrationName, participantName, telegramName }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
