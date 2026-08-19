using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Tests;

public sealed class CallbackDataValidatorTests
{
    [Theory]
    [InlineData("admin:menu")]
    [InlineData("admin:export")]
    [InlineData("reg:edit")]
    [InlineData("reg:profile")]
    [InlineData("reg:payment")]
    [InlineData("reg:name:skip")]
    [InlineData("reg:days:3")]
    [InlineData("reg:accommodation:yes")]
    [InlineData("collection:menu")]
    [InlineData("collection:cancel")]
    [InlineData("collection:add:single")]
    [InlineData("collection:import:bgg:personal")]
    [InlineData("collection:import:tesera:club")]
    [InlineData("interest:toggle:42")]
    [InlineData("session:menu")]
    [InlineData("session:active:0")]
    [InlineData("session:view:42")]
    [InlineData("session:list:p:0")]
    [InlineData("session:game:42")]
    [InlineData("session:create:42:4")]
    [InlineData("session:join:42")]
    [InlineData("session:leave:42")]
    [InlineData("session:pjoin:42")]
    [InlineData("session:pleave:42")]
    [InlineData("session:close:42")]
    [InlineData("session:cancel:42")]
    [InlineData("game:menu")]
    [InlineData("game:my:menu")]
    [InlineData("game:wishlist:menu")]
    [InlineData("game:wishlist:popular:0")]
    [InlineData("game:wishlist:mine:1")]
    [InlineData("game:collections:0")]
    [InlineData("game:collection:7:0")]
    [InlineData("game:collectionall:7")]
    [InlineData("game:list:b:0")]
    [InlineData("game:wanted:0")]
    [InlineData("game:mywanted:0")]
    [InlineData("game:my:d:0")]
    [InlineData("game:search:catalog")]
    [InlineData("game:card:42:cp:0")]
    [InlineData("game:availability:42:cp:0")]
    [InlineData("game:add:42")]
    [InlineData("copy:add:42:b")]
    [InlineData("copy:confirm:42:m")]
    [InlineData("copy:set:42:b")]
    public void IsValid_AcceptsProductionPayloadShapes(string value)
    {
        Assert.True(CallbackDataValidator.IsValid(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown:action")]
    [InlineData("admin:unknown")]
    [InlineData("reg:days:4")]
    [InlineData("reg:accommodation:maybe")]
    [InlineData("collection:import:bgg:other")]
    [InlineData("interest:toggle:0")]
    [InlineData("session:create:42:5")]
    [InlineData("session:join:not-a-number")]
    [InlineData("session:active:-1")]
    [InlineData("game:wishlist:other:0")]
    [InlineData("game:wishlist:mine:-1")]
    [InlineData("game:collections:-1")]
    [InlineData("game:collection:0:0")]
    [InlineData("game:list:b:-1")]
    [InlineData("game:card:0:cp:0")]
    [InlineData("game:availability:0:cp:0")]
    [InlineData("copy:set:42:x")]
    [InlineData("copy:set:42:b:extra")]
    public void IsValid_RejectsMalformedPayloads(string? value)
    {
        Assert.False(CallbackDataValidator.IsValid(value));
    }
}
