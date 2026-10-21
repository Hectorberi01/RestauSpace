namespace Billing.Domain;

/// <summary>
/// Les prestataires qui encaissent en ligne les abonnements vendus dans une devise, choisis par la plateforme (Stripe et
/// PayPal pour l'euro, FedaPay pour le FCFA…), dans l'ordre où ils sont proposés au restaurant, qui choisit au moment de
/// payer. Une ligne par devise ; sans ligne, le réglage de l'hôte s'applique. Seuls les codes des prestataires sont gardés
/// ici : leurs identifiants restent au coffre du module Payments.
/// </summary>
public sealed class OnlinePaymentRoute
{
    public const int MaxProviderLength = SubscriptionCheckout.MaxProviderLength;
    public const int MaxProviders = 6;

    private List<string> _otherProviders = [];

    private OnlinePaymentRoute(string currencyCode, IReadOnlyList<string> providers, DateTimeOffset updatedAtUtc)
    {
        CurrencyCode = currencyCode;
        Provider = providers[0];
        _otherProviders = providers.Skip(1).ToList();
        UpdatedAtUtc = updatedAtUtc;
    }

    // Réservé à l'ORM.
    private OnlinePaymentRoute()
    {
        CurrencyCode = null!;
        Provider = null!;
    }

    public string CurrencyCode { get; private set; }

    /// <summary>Le premier prestataire proposé.</summary>
    public string Provider { get; private set; }

    /// <summary>Les suivants, dans l'ordre.</summary>
    public IReadOnlyList<string> OtherProviders => _otherProviders.AsReadOnly();

    /// <summary>Tous les prestataires proposés, dans l'ordre.</summary>
    public IReadOnlyList<string> Providers => [Provider, .. _otherProviders];

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static OnlinePaymentRoute Create(Currency currency, IEnumerable<string?> providers, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(clock);
        return new OnlinePaymentRoute(currency.Code, NormalizeAll(providers), clock.UtcNow);
    }

    public void Change(IEnumerable<string?> providers, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var list = NormalizeAll(providers);
        Provider = list[0];
        _otherProviders = list.Skip(1).ToList();
        UpdatedAtUtc = clock.UtcNow;
    }

    /// <summary>Codes normalisés, sans doublon, dans l'ordre donné : un au moins, <see cref="MaxProviders"/> au plus.</summary>
    public static IReadOnlyList<string> NormalizeAll(IEnumerable<string?> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var list = providers.Select(Normalize).Distinct(StringComparer.Ordinal).ToList();
        DomainException.ThrowIf(list.Count is 0 or > MaxProviders, OnlinePaymentRouteErrors.ProvidersInvalid);
        return list;
    }

    public static string Normalize(string? provider)
    {
        var code = provider?.Trim().ToLowerInvariant() ?? string.Empty;
        DomainException.ThrowIf(
            code.Length is 0 or > MaxProviderLength || !code.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'),
            OnlinePaymentRouteErrors.ProviderInvalid);
        return code;
    }
}

public static class OnlinePaymentRouteErrors
{
    public static readonly DomainError ProviderInvalid = new("billing.online_payment.provider_invalid", "Unknown payment provider code.");

    public static readonly DomainError ProvidersInvalid = new("billing.online_payment.providers_invalid", "Choose one to six payment providers.");
}
