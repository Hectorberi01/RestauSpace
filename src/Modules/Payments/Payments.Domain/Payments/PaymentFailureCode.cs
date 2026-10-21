using System.Text;

namespace Payments.Domain;

/// <summary>
/// Pourquoi une tentative a échoué, sous forme d'un code technique stable (<c>card_declined</c>,
/// <c>insufficient_funds</c>, <c>authorization_expired</c>...) : minuscules, chiffres, <c>_</c>, <c>.</c> ou <c>-</c>,
/// commence par une lettre, 64 caractères au plus. Sert aux statistiques et aux décisions (nouvel essai, autre moyen).
/// JAMAIS de donnée bancaire : un code, pas un message du prestataire (celui-ci va, borné, dans
/// <see cref="PaymentAttempt.FailureReason"/>).
/// </summary>
public sealed class PaymentFailureCode : ValueObject
{
    public const int MaxLength = 64;

    /// <summary>Le prestataire a refusé sans donner de motif.</summary>
    public static readonly PaymentFailureCode ProviderDeclined = new("provider_declined");

    /// <summary>La tentative a été close parce que le paiement a été annulé.</summary>
    public static readonly PaymentFailureCode PaymentCancelled = new("payment_cancelled");

    private PaymentFailureCode(string value) => Value = value;

    public string Value { get; }

    /// <summary>Un code déjà normalisé (saisi par l'Application ou lu en base). Refuse tout autre format.</summary>
    public static PaymentFailureCode Create(string? value)
    {
        var trimmed = value?.Trim().ToLowerInvariant();
        DomainException.ThrowIf(!IsValid(trimmed), PaymentErrors.FailureCodeInvalid);
        return new PaymentFailureCode(trimmed!);
    }

    /// <summary>
    /// Le code tiré de ce que dit un prestataire (« Card Declined », « INSUFFICIENT_FUNDS »...) : minuscules, tout autre
    /// caractère remplacé par <c>_</c>, borné. Rien d'exploitable : <see cref="ProviderDeclined"/>.
    /// </summary>
    public static PaymentFailureCode FromProvider(string? providerCode)
    {
        if (string.IsNullOrWhiteSpace(providerCode))
        {
            return ProviderDeclined;
        }

        var builder = new StringBuilder(MaxLength);
        foreach (var character in providerCode.Trim().ToLowerInvariant())
        {
            if (builder.Length == MaxLength)
            {
                break;
            }

            var kept = character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '.' or '-' ? character : '_';
            // Pas deux « _ » de suite : « card  declined » donne « card_declined ».
            if (kept != '_' || builder.Length == 0 || builder[^1] != '_')
            {
                builder.Append(kept);
            }
        }

        var normalized = builder.ToString().Trim('_');
        return IsValid(normalized) ? new PaymentFailureCode(normalized) : ProviderDeclined;
    }

    private static bool IsValid(string? value) =>
        value is { Length: > 0 and <= MaxLength }
        && value[0] is >= 'a' and <= 'z'
        && value.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '.' or '-');

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
