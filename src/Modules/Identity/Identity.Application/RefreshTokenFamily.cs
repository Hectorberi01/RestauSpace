using Identity.Application.Abstractions;

namespace Identity.Application;

/// <summary>
/// Un refresh token renouvelé porte le premier jeton de sa famille : « {premier jeton}.{secret} ». Le premier jeton
/// (déjà remplacé) sert d'ancre : n'importe quel ancien jeton de la famille la retrouve et révoque tout.
/// </summary>
public static class RefreshTokenFamily
{
    private const char Separator = '.';

    /// <summary>Le premier jeton de la famille, ou null pour un jeton qui ouvre sa famille.</summary>
    public static string? AnchorOf(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        var dot = token.IndexOf(Separator, StringComparison.Ordinal);
        return dot > 0 && dot < token.Length - 1 ? token[..dot] : null;
    }

    /// <summary>Nouveau jeton de la famille dont <paramref name="anchor"/> est le premier jeton.</summary>
    public static RefreshToken Next(IRefreshTokens tokens, string anchor)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentException.ThrowIfNullOrWhiteSpace(anchor);
        var value = $"{anchor}{Separator}{tokens.Generate().Value}";
        return new RefreshToken(value, tokens.Hash(value));
    }
}
