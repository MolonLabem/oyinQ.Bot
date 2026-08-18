namespace oyinQ.Bot.Common.Options;

public sealed class BggOptions
{
    public string ApiToken { get; init; } = string.Empty;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(ApiToken);

    public static BggOptions FromConfiguration(IConfiguration configuration) =>
        new()
        {
            ApiToken = configuration["BGG_API_TOKEN"]?.Trim() ?? string.Empty
        };
}
