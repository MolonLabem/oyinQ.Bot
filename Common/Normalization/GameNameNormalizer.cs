using System.Text;

namespace oyinQ.Bot.Common.Normalization;

public sealed class GameNameNormalizer
{
    public string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(character);
                pendingSpace = false;
                continue;
            }

            pendingSpace = builder.Length > 0;
        }

        return builder.ToString();
    }
}
