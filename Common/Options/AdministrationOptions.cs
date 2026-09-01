using System.Globalization;

namespace oyinQ.Bot.Common.Options;

public sealed class AdministrationOptions
{
    public const string SectionName = "Administration";

    public IReadOnlySet<long> SuperAdminTelegramUserIds { get; init; } = new HashSet<long>();

    public IReadOnlySet<long> BootstrapTelegramUserIds { get; init; } = new HashSet<long>();

    public static AdministrationOptions FromConfiguration(IConfiguration configuration)
    {
        var superAdmins = ParseIds(configuration[$"{SectionName}:SuperAdminTelegramUserIds"],
            "Administration:SuperAdminTelegramUserIds");
        var legacyBootstrap = ParseIds(configuration[$"{SectionName}:BootstrapTelegramUserIds"],
            "Administration:BootstrapTelegramUserIds");

        // A single legacy bootstrap ID clearly represents the original owner. Multiple legacy global
        // administrators are deliberately not promoted to unrestricted Super Admins.
        if (superAdmins.Count == 0 && legacyBootstrap.Count == 1)
            superAdmins.Add(legacyBootstrap.Single());

        return new AdministrationOptions
        {
            SuperAdminTelegramUserIds = superAdmins,
            BootstrapTelegramUserIds = legacyBootstrap
        };
    }

    private static HashSet<long> ParseIds(string? configuredValue, string optionName)
    {
        var values = (configuredValue ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ids = new HashSet<long>();
        foreach (var value in values)
        {
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
            {
                throw new InvalidOperationException($"{optionName} must contain comma-separated positive Telegram user IDs.");
            }

            ids.Add(id);
        }

        return ids;
    }
}
