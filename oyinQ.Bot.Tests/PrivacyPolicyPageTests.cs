using Microsoft.AspNetCore.Http;
using oyinQ.Bot.Features.PublicSite;

namespace oyinQ.Bot.Tests;

public sealed class PrivacyPolicyPageTests
{
    [Fact]
    public async Task PublicRoute_ReturnsHtmlWithoutTelegramAuthentication()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await PrivacyPolicyPage.HandleAsync(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var html = await reader.ReadToEndAsync();
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", context.Response.ContentType);
        Assert.Contains("Политика конфиденциальности", html);
        Assert.DoesNotContain("initData", context.Request.QueryString.Value ?? string.Empty);
    }

    [Fact]
    public void PublicPolicy_DoesNotExposeConfigurationOrSecrets()
    {
        var html = PrivacyPolicyPage.BuildHtml();

        Assert.DoesNotContain("Database__ConnectionString", html);
        Assert.DoesNotContain("Telegram__Token", html);
        Assert.DoesNotContain("WebhookSecret", html);
        Assert.DoesNotContain("BoardGameGeek__ApiToken", html);
    }
}
