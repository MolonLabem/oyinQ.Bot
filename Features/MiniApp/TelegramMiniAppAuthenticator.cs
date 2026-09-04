using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;

namespace oyinQ.Bot.Features.MiniApp;

public sealed record TelegramMiniAppIdentity(
    long TelegramUserId,
    string? TelegramUsername = null,
    string? DisplayName = null);

public sealed class TelegramMiniAppAuthenticator(
    IOptions<BotOptions> options,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan MaximumAge = TimeSpan.FromHours(24);

    public TelegramMiniAppIdentity? Authenticate(string? initData)
    {
        if (string.IsNullOrWhiteSpace(initData))
        {
            return null;
        }

        var values = QueryHelpers.ParseQuery(initData);
        if (!values.TryGetValue("hash", out var suppliedHash)
            || suppliedHash.Count != 1
            || !values.TryGetValue("auth_date", out var authDateValue)
            || authDateValue.Count != 1
            || !values.TryGetValue("user", out var userValue)
            || userValue.Count != 1)
        {
            return null;
        }

        var dataCheckString = string.Join(
            '\n',
            values
                .Where(value => !string.Equals(value.Key, "hash", StringComparison.Ordinal))
                .OrderBy(value => value.Key, StringComparer.Ordinal)
                .Select(value => $"{value.Key}={value.Value}"));

        using var secretHmac = new HMACSHA256(Encoding.UTF8.GetBytes("WebAppData"));
        var secretKey = secretHmac.ComputeHash(Encoding.UTF8.GetBytes(options.Value.Token));
        using var dataHmac = new HMACSHA256(secretKey);
        var expectedHash = dataHmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString));

        byte[] actualHash;
        try
        {
            actualHash = Convert.FromHexString(suppliedHash.ToString());
        }
        catch (FormatException)
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash)
            || !long.TryParse(authDateValue, NumberStyles.None, CultureInfo.InvariantCulture, out var authDateSeconds))
        {
            return null;
        }

        var authenticatedAt = DateTimeOffset.FromUnixTimeSeconds(authDateSeconds);
        var age = timeProvider.GetUtcNow() - authenticatedAt;
        if (age < TimeSpan.FromMinutes(-5) || age > MaximumAge)
        {
            return null;
        }

        try
        {
            using var user = JsonDocument.Parse(userValue.ToString());
            if (!user.RootElement.TryGetProperty("id", out var id)
                || !id.TryGetInt64(out var telegramUserId) || telegramUserId <= 0)
            {
                return null;
            }

            var username = user.RootElement.TryGetProperty("username", out var usernameValue)
                ? usernameValue.GetString()
                : null;
            var firstName = user.RootElement.TryGetProperty("first_name", out var firstNameValue)
                ? firstNameValue.GetString()
                : null;
            var lastName = user.RootElement.TryGetProperty("last_name", out var lastNameValue)
                ? lastNameValue.GetString()
                : null;
            var displayName = string.Join(' ', new[] { firstName, lastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
            return new TelegramMiniAppIdentity(
                telegramUserId,
                username,
                string.IsNullOrWhiteSpace(displayName) ? null : displayName);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
