using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Features.MiniApp;

namespace oyinQ.Bot.Tests;

public sealed class TelegramMiniAppAuthenticatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Authenticate_ValidSignedData_ReturnsTelegramIdentity()
    {
        var authenticator = CreateAuthenticator();

        var identity = authenticator.Authenticate(CreateInitData(42, Now));

        Assert.Equal(42, identity?.TelegramUserId);
    }

    [Fact]
    public void Authenticate_TamperedUser_ReturnsNull()
    {
        var authenticator = CreateAuthenticator();
        var signed = CreateInitData(42, Now);

        Assert.Null(authenticator.Authenticate(signed.Replace("%3A42%2C", "%3A99%2C", StringComparison.Ordinal)));
    }

    [Fact]
    public void Authenticate_DataOlderThanOneDay_ReturnsNull()
    {
        var authenticator = CreateAuthenticator();

        Assert.Null(authenticator.Authenticate(CreateInitData(42, Now.AddDays(-2))));
    }

    private static TelegramMiniAppAuthenticator CreateAuthenticator() =>
        new(
            Options.Create(new BotOptions { Token = "123:test-token" }),
            new FixedTimeProvider(Now));

    private static string CreateInitData(long telegramUserId, DateTimeOffset authenticatedAt)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_date"] = authenticatedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            ["query_id"] = "query-1",
            ["user"] = JsonSerializer.Serialize(new { id = telegramUserId, first_name = "Sardar" })
        };
        var check = string.Join('\n', values.Select(value => $"{value.Key}={value.Value}"));
        using var secretHmac = new HMACSHA256(Encoding.UTF8.GetBytes("WebAppData"));
        var secret = secretHmac.ComputeHash(Encoding.UTF8.GetBytes("123:test-token"));
        using var dataHmac = new HMACSHA256(secret);
        var hash = Convert.ToHexStringLower(dataHmac.ComputeHash(Encoding.UTF8.GetBytes(check)));

        return string.Join('&', values.Select(value => $"{Uri.EscapeDataString(value.Key)}={Uri.EscapeDataString(value.Value)}"))
            + $"&hash={hash}";
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
