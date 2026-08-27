using Microsoft.Extensions.Configuration;
using oyinQ.Bot.Common.Options;

namespace oyinQ.Bot.Tests;

public sealed class TeseraOptionsTests
{
    [Fact]
    public void FromConfiguration_WithoutProxy_UsesTeseraDirectly()
    {
        var options = TeseraOptions.FromConfiguration(Configuration());

        Assert.Equal(new Uri("https://api.tesera.ru"), options.BaseAddress);
        Assert.Null(options.ProxySecret);
        Assert.False(options.UsesProxy);
    }

    [Fact]
    public void FromConfiguration_WithProxy_UsesConfiguredWorker()
    {
        var options = TeseraOptions.FromConfiguration(Configuration(
            ("TESERA_PROXY_BASE_URL", "https://tesera-proxy.example.workers.dev"),
            ("TESERA_PROXY_SECRET", "secret")));

        Assert.Equal(new Uri("https://tesera-proxy.example.workers.dev"), options.BaseAddress);
        Assert.Equal("secret", options.ProxySecret);
        Assert.True(options.UsesProxy);
    }

    [Theory]
    [InlineData("https://tesera-proxy.example.workers.dev", null)]
    [InlineData(null, "secret")]
    public void FromConfiguration_WithIncompleteProxyConfiguration_Throws(
        string? baseUrl,
        string? secret)
    {
        Assert.Throws<InvalidOperationException>(() => TeseraOptions.FromConfiguration(
            Configuration(("TESERA_PROXY_BASE_URL", baseUrl), ("TESERA_PROXY_SECRET", secret))));
    }

    [Fact]
    public void FromConfiguration_RejectsInsecureRemoteProxy()
    {
        Assert.Throws<InvalidOperationException>(() => TeseraOptions.FromConfiguration(Configuration(
            ("TESERA_PROXY_BASE_URL", "http://proxy.example.com"),
            ("TESERA_PROXY_SECRET", "secret"))));
    }

    [Fact]
    public void FromConfiguration_RejectsTeseraAsProxyToAvoidLeakingSecretUpstream()
    {
        Assert.Throws<InvalidOperationException>(() => TeseraOptions.FromConfiguration(Configuration(
            ("TESERA_PROXY_BASE_URL", "https://api.tesera.ru"),
            ("TESERA_PROXY_SECRET", "secret"))));
    }

    private static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(value => value.Key, value => value.Value))
            .Build();
}
