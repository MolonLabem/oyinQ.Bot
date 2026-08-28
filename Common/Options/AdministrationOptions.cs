using System.Globalization;

namespace oyinQ.Bot.Common.Options;

public sealed class AdministrationOptions
{
    public const string SectionName = "Administration";

    public IReadOnlySet<long> BootstrapTelegramUserIds { get; init; } = new HashSet<long>();

    public static AdministrationOptions FromConfiguration(IConfiguration configuration)
    {
        var values = (configuration[$"{SectionName}:BootstrapTelegramUserIds"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ids = new HashSet<long>();
        foreach (var value in values)
        {
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
            {
                throw new InvalidOperationException("Administration:BootstrapTelegramUserIds must contain comma-separated positive Telegram user IDs.");
            }

            ids.Add(id);
        }

        return new AdministrationOptions { BootstrapTelegramUserIds = ids };
    }
}
