using System.Text.Json.Serialization.Metadata;
using oyinQ.Bot.Features.Collections;

namespace oyinQ.Bot.Features.MiniApp;

internal static class ComplexityJsonPresentation
{
    // HTTP-only projection: persistence serializers retain only the provider's numeric data.
    public static void Add(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object || !typeof(IGameComplexity).IsAssignableFrom(typeInfo.Type)) return;
        var property = typeInfo.CreateJsonPropertyInfo(typeof(ComplexityInfo), "complexityInfo");
        property.Get = value => GameComplexityPresentation.Present((IGameComplexity)value);
        typeInfo.Properties.Add(property);
    }
}
