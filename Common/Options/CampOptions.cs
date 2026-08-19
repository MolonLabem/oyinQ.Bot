using System.Globalization;

namespace oyinQ.Bot.Common.Options;

public sealed class CampOptions
{
    public long? BoardCampChatId { get; init; }
    public IReadOnlySet<long> AdminTelegramIds { get; init; } = new HashSet<long>();
    public decimal AccommodationPricePerDay { get; init; } = 3000m;

    public static CampOptions FromConfiguration(IConfiguration configuration)
    {
        long? boardCampChatId = long.TryParse(
            configuration["BOARD_CAMP_CHAT_ID"],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsedChatId)
            ? parsedChatId
            : null;

        var adminTelegramIds = (configuration["ADMIN_TELEGRAM_IDS"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                ? (long?)id
                : null)
            .Where(static id => id.HasValue)
            .Select(static id => id.GetValueOrDefault())
            .ToHashSet();

        var accommodationPrice = decimal.TryParse(
            configuration["ACCOMMODATION_PRICE_PER_DAY"],
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var parsedPrice)
            ? parsedPrice
            : 3000m;

        return new CampOptions
        {
            BoardCampChatId = boardCampChatId,
            AdminTelegramIds = adminTelegramIds,
            AccommodationPricePerDay = accommodationPrice
        };
    }
}
