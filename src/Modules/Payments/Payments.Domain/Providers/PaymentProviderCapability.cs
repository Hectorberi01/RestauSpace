namespace Payments.Domain;

/// <summary>
/// Ce qu'un prestataire sait faire, déclaré par la plateforme : pays, devises, moyens de paiement, autorisation puis
/// capture, remboursement (partiel ou non), webhooks, paiement récurrent, expiration d'une intention, montants min/max par
/// devise. Le métier interroge CE modèle, jamais le nom du prestataire. Remplacé en bloc (valeur immuable).
/// </summary>
public sealed class PaymentProviderCapability : ValueObject
{
    public const int MaxCountries = 100;
    public const int MaxCurrencies = 50;
    public static readonly TimeSpan MinPaymentExpiration = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxPaymentExpiration = TimeSpan.FromDays(30);

    private PaymentProviderCapability(
        IReadOnlyList<PaymentMethodType> methods,
        IReadOnlyList<CountryCode> countries,
        IReadOnlyList<Currency> currencies,
        bool supportsAuthorization,
        bool supportsRefund,
        bool supportsPartialRefund,
        bool supportsWebhooks,
        bool supportsRecurring,
        bool supportsPlatformManagedMode,
        TimeSpan? paymentExpiration,
        IReadOnlyList<PaymentAmountLimit> amountLimits)
    {
        SupportedMethods = methods;
        SupportedCountries = countries;
        SupportedCurrencies = currencies;
        SupportsAuthorization = supportsAuthorization;
        SupportsRefund = supportsRefund;
        SupportsPartialRefund = supportsPartialRefund;
        SupportsWebhooks = supportsWebhooks;
        SupportsRecurring = supportsRecurring;
        SupportsPlatformManagedMode = supportsPlatformManagedMode;
        PaymentExpiration = paymentExpiration;
        AmountLimits = amountLimits;
    }

    /// <summary>Au moins un ; triés.</summary>
    public IReadOnlyList<PaymentMethodType> SupportedMethods { get; }

    /// <summary>Vide = tous les pays ; triés.</summary>
    public IReadOnlyList<CountryCode> SupportedCountries { get; }

    /// <summary>Vide = toutes les devises ; triées.</summary>
    public IReadOnlyList<Currency> SupportedCurrencies { get; }

    /// <summary>Autorisation puis capture en deux temps. Sinon, la capture est immédiate.</summary>
    public bool SupportsAuthorization { get; }

    public bool SupportsRefund { get; }

    /// <summary>Exige <see cref="SupportsRefund"/>.</summary>
    public bool SupportsPartialRefund { get; }

    /// <summary>Le prestataire notifie ses résultats par webhook signé.</summary>
    public bool SupportsWebhooks { get; }

    public bool SupportsRecurring { get; }

    /// <summary>
    /// Le contrat avec le prestataire autorise la PLATEFORME à encaisser pour le compte des restaurants
    /// (<see cref="PaymentMode.PlatformManaged"/>). Faux par défaut : beaucoup de prestataires l'interdisent.
    /// </summary>
    public bool SupportsPlatformManagedMode { get; }

    /// <summary>Durée de vie d'une intention de paiement chez le prestataire ; null = pas d'expiration connue.</summary>
    public TimeSpan? PaymentExpiration { get; }

    /// <summary>Bornes de montant, au plus une par devise ; triées par devise.</summary>
    public IReadOnlyList<PaymentAmountLimit> AmountLimits { get; }

    /// <summary>
    /// Règles de cohérence propres aux capacités : au moins un moyen connu ; remboursement partiel => remboursement ;
    /// expiration bornée ; limites dans des devises gérées, une par devise. La cohérence avec la nature du prestataire
    /// (sur place ou en ligne) est vérifiée par <see cref="PaymentProvider"/>.
    /// </summary>
    public static PaymentProviderCapability Create(
        IEnumerable<PaymentMethodType> methods,
        IEnumerable<CountryCode>? countries = null,
        IEnumerable<Currency>? currencies = null,
        bool supportsAuthorization = false,
        bool supportsRefund = false,
        bool supportsPartialRefund = false,
        bool supportsWebhooks = false,
        bool supportsRecurring = false,
        TimeSpan? paymentExpiration = null,
        IEnumerable<PaymentAmountLimit>? amountLimits = null,
        bool supportsPlatformManagedMode = false)
    {
        ArgumentNullException.ThrowIfNull(methods);

        var methodList = methods.Distinct().Order().ToArray();
        DomainException.ThrowIf(methodList.Length == 0, PaymentProviderErrors.MethodsMissing);
        DomainException.ThrowIf(methodList.Any(method => !Enum.IsDefined(method)), PaymentProviderErrors.MethodUnknown);

        var countryList = (countries ?? Enumerable.Empty<CountryCode>()).Distinct().OrderBy(country => country.Value, StringComparer.Ordinal).ToArray();
        DomainException.ThrowIf(countryList.Length > MaxCountries, PaymentProviderErrors.TooManyCountries);
        var currencyList = (currencies ?? Enumerable.Empty<Currency>()).Distinct().OrderBy(currency => currency.Code, StringComparer.Ordinal).ToArray();
        DomainException.ThrowIf(currencyList.Length > MaxCurrencies, PaymentProviderErrors.TooManyCurrencies);

        DomainException.ThrowIf(supportsPartialRefund && !supportsRefund, PaymentProviderErrors.PartialRefundWithoutRefund);
        DomainException.ThrowIf(
            paymentExpiration is { } expiration && (expiration < MinPaymentExpiration || expiration > MaxPaymentExpiration),
            PaymentProviderErrors.ExpirationOutOfRange);

        var limits = (amountLimits ?? Enumerable.Empty<PaymentAmountLimit>()).OrderBy(limit => limit.Currency.Code, StringComparer.Ordinal).ToArray();
        DomainException.ThrowIf(
            limits.Select(limit => limit.Currency).Distinct().Count() != limits.Length,
            PaymentProviderErrors.AmountLimitDuplicated);
        DomainException.ThrowIf(
            currencyList.Length > 0 && limits.Any(limit => !currencyList.Contains(limit.Currency)),
            PaymentProviderErrors.AmountLimitCurrencyNotSupported);

        return new PaymentProviderCapability(
            Array.AsReadOnly(methodList),
            Array.AsReadOnly(countryList),
            Array.AsReadOnly(currencyList),
            supportsAuthorization,
            supportsRefund,
            supportsPartialRefund,
            supportsWebhooks,
            supportsRecurring,
            supportsPlatformManagedMode,
            paymentExpiration,
            Array.AsReadOnly(limits));
    }

    public bool Supports(PaymentMethodType method) => SupportedMethods.Contains(method);

    public bool Supports(CountryCode country)
    {
        ArgumentNullException.ThrowIfNull(country);
        return SupportedCountries.Count == 0 || SupportedCountries.Contains(country);
    }

    public bool Supports(Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return SupportedCurrencies.Count == 0 || SupportedCurrencies.Contains(currency);
    }

    /// <summary>Devise gérée et montant dans les bornes déclarées pour cette devise (s'il y en a).</summary>
    public bool Allows(Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);
        if (!Supports(amount.Currency))
        {
            return false;
        }

        var limit = AmountLimits.SingleOrDefault(candidate => candidate.Currency == amount.Currency);
        return limit is null || limit.Allows(amount);
    }

    /// <summary>Un remboursement de ce montant est-il possible pour un paiement capturé de <paramref name="captured"/> ?</summary>
    public bool CanRefund(Money amount, Money captured)
    {
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(captured);
        return SupportsRefund && (SupportsPartialRefund || amount == captured);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        foreach (var method in SupportedMethods)
        {
            yield return method;
        }

        yield return "|";
        foreach (var country in SupportedCountries)
        {
            yield return country;
        }

        yield return "|";
        foreach (var currency in SupportedCurrencies)
        {
            yield return currency;
        }

        yield return "|";
        yield return SupportsAuthorization;
        yield return SupportsRefund;
        yield return SupportsPartialRefund;
        yield return SupportsWebhooks;
        yield return SupportsRecurring;
        yield return SupportsPlatformManagedMode;
        yield return PaymentExpiration;
        foreach (var limit in AmountLimits)
        {
            yield return limit;
        }
    }
}
