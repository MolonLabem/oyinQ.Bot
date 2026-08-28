using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Tests;

public sealed class CallbackDataValidatorTests
{
    [Theory]
    [InlineData("admin:menu")]
    [InlineData("admin:participants")]
    [InlineData("admin:camp:create")]
    [InlineData("admin:camp:source:none")]
    [InlineData("admin:camp:source:42")]
    public void IsValid_AcceptsCurrentAdminCallbacks(string value) =>
        Assert.True(CallbackDataValidator.IsValid(value));

    [Theory]
    [InlineData("game:menu")]
    [InlineData("session:join:42")]
    [InlineData("collection:import:bgg:club")]
    [InlineData("admin:top")]
    [InlineData("admin:camp:source:0")]
    [InlineData("")]
    public void IsValid_RejectsLegacyOrMalformedCallbacks(string value) =>
        Assert.False(CallbackDataValidator.IsValid(value));
}
