namespace Payments.Domain;

/// <summary>
/// Un prestataire que la PLATEFORME sait traiter : nom, code unique, nature (<see cref="PaymentProviderType"/>), ce qu'il
/// sait faire (<see cref="PaymentProviderCapability"/> : pays, devises, moyens, remboursement, autorisation/capture,
/// webhooks, limites...) et s'il est proposé aux restaurants. Agrégat de la plateforme, pas d'un tenant : un restaurant
/// ne peut que choisir parmi eux, jamais en créer un. Le retirer n'efface rien : les paiements en cours restent suivis,
/// mais aucun nouveau paiement ne peut l'utiliser.
/// </summary>
public sealed class PaymentProvider : AggregateRoot<PaymentProviderId>
{
    public const int MinDisplayNameLength = 2;
    public const int MaxDisplayNameLength = 60;

    private PaymentProvider(
        PaymentProviderId id,
        PaymentProviderCode code,
        PaymentProviderType type,
        string displayName,
        PaymentProviderCapability capability,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        Code = code;
        Type = type;
        DisplayName = displayName;
        Capability = capability;
        IsEnabled = true;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    // Réservé à l'ORM.
    private PaymentProvider()
    {
        Code = null!;
        DisplayName = null!;
        Capability = null!;
    }

    public PaymentProviderCode Code { get; private set; }

    /// <summary>Fixée à l'enregistrement : changer de nature serait un autre prestataire.</summary>
    public PaymentProviderType Type { get; private set; }

    public string DisplayName { get; private set; }

    public PaymentProviderCapability Capability { get; private set; }

    /// <summary>Raccourci de <see cref="PaymentProviderCapability.SupportedCurrencies"/> (vide = toutes).</summary>
    public IReadOnlyList<Currency> SupportedCurrencies => Capability.SupportedCurrencies;

    /// <summary>Proposé aux restaurants (statut du prestataire).</summary>
    public bool IsEnabled { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Ajoute le prestataire au catalogue, proposé aussitôt.</summary>
    /// <param name="isCodeUnique">Réponse de l'Application : aucun autre prestataire ne porte ce code.</param>
    public static PaymentProvider Register(
        PaymentProviderCode code,
        PaymentProviderType type,
        string? displayName,
        PaymentProviderCapability capability,
        bool isCodeUnique,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!Enum.IsDefined(type), PaymentProviderErrors.TypeUnknown);
        DomainException.ThrowIf(!isCodeUnique, PaymentProviderErrors.CodeNotUnique);
        EnsureFits(type, capability);

        var now = clock.UtcNow;
        var provider = new PaymentProvider(PaymentProviderId.New(), code, type, CleanName(displayName), capability, now);
        provider.Raise(new PaymentProviderRegistered(provider.Id, code, type, now));
        return provider;
    }

    /// <summary>Sans effet ni événement si le nom ne change pas.</summary>
    public void Rename(string? displayName, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var name = CleanName(displayName);
        if (name == DisplayName)
        {
            return;
        }

        DisplayName = name;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new PaymentProviderRenamed(Id, Code, name, UpdatedAtUtc));
    }

    /// <summary>
    /// Remplace ce que le prestataire sait faire (nouveau pays, nouvelle devise, remboursement partiel...). Les paiements
    /// en cours ne sont pas touchés ; les nouveaux suivent le nouveau modèle. Sans effet ni événement si rien ne change.
    /// </summary>
    public void ChangeCapabilities(PaymentProviderCapability capability, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureFits(Type, capability);
        if (capability == Capability)
        {
            return;
        }

        Capability = capability;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new PaymentProviderCapabilitiesChanged(Id, Code, UpdatedAtUtc));
    }

    public void Enable(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(IsEnabled, PaymentProviderErrors.AlreadyEnabled);
        IsEnabled = true;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new PaymentProviderEnabled(Id, Code, UpdatedAtUtc));
    }

    public void Disable(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!IsEnabled, PaymentProviderErrors.AlreadyDisabled);
        IsEnabled = false;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new PaymentProviderDisabled(Id, Code, UpdatedAtUtc));
    }

    /// <summary>Proposé, et dans cette devise : utilisable pour un nouveau paiement (le contexte complet est vu par la policy).</summary>
    public bool CanTakePaymentsIn(Currency currency) => IsEnabled && Capability.Supports(currency);

    /// <summary>Moyens compatibles avec la nature du prestataire ; sur place : ni autorisation, ni webhook, ni récurrence, ni mode plateforme, ni expiration.</summary>
    private static void EnsureFits(PaymentProviderType type, PaymentProviderCapability capability)
    {
        DomainException.ThrowIf(
            capability.SupportedMethods.Any(method => !method.FitsProviderType(type)),
            PaymentProviderErrors.MethodNotForType);
        DomainException.ThrowIf(
            type == PaymentProviderType.OnSite
            && (capability.SupportsAuthorization || capability.SupportsWebhooks || capability.SupportsRecurring
                || capability.SupportsPlatformManagedMode || capability.PaymentExpiration is not null),
            PaymentProviderErrors.OnlineCapabilityOnSite);
    }

    private static string CleanName(string? displayName)
    {
        var name = displayName?.Trim() ?? string.Empty;
        DomainException.ThrowIf(
            name.Length is < MinDisplayNameLength or > MaxDisplayNameLength,
            PaymentProviderErrors.DisplayNameInvalid);
        return name;
    }
}
