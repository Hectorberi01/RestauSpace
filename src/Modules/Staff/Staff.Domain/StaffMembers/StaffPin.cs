namespace Staff.Domain;

/// <summary>Règles du code PIN de caisse : 4 à 6 chiffres, ni répétés (« 0000 ») ni consécutifs (« 1234 », « 9876 »).</summary>
public static class StaffPin
{
    public const int MinLength = 4;
    public const int MaxLength = 6;
    public const int MaxFailures = 5;
    public static readonly TimeSpan Lockout = TimeSpan.FromMinutes(15);

    public static string Validate(string? pin)
    {
        var value = pin?.Trim() ?? string.Empty;
        DomainException.ThrowIf(
            value.Length is < MinLength or > MaxLength || !value.All(char.IsAsciiDigit),
            StaffMemberErrors.PinInvalidFormat);
        var steps = value.Zip(value.Skip(1), (a, b) => b - a).Distinct().ToList();
        DomainException.ThrowIf(steps.Count == 1 && steps[0] is 0 or 1 or -1, StaffMemberErrors.PinTooSimple);
        return value;
    }

    /// <summary>Forme acceptable à comparer ; un essai mal formé est simplement un mauvais code.</summary>
    public static bool IsWellFormed(string? pin) =>
        pin is { Length: >= MinLength and <= MaxLength } && pin.All(char.IsAsciiDigit);
}
