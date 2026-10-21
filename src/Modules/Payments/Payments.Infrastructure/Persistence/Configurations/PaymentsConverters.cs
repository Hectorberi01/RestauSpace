using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Payments.Domain;

namespace Payments.Infrastructure.Persistence.Configurations;

/// <summary>
/// Conversions entre les types du domaine et les colonnes. Toute valeur relue repasse par la fabrique du domaine
/// (<c>Create</c>, <c>From</c>, <c>Of</c>) : une ligne modifiée à la main qui viole une règle est refusée au chargement
/// au lieu de produire un agrégat invalide.
/// </summary>
internal static class PaymentsConverters
{
    public static readonly ValueConverter<TenantId, Guid> TenantKey = new(id => id.Value, value => TenantId.From(value));
    public static readonly ValueConverter<OrderId, Guid> OrderKey = new(id => id.Value, value => OrderId.From(value));
    public static readonly ValueConverter<PaymentId, Guid> PaymentKey = new(id => id.Value, value => PaymentId.From(value));
    public static readonly ValueConverter<PaymentAttemptId, Guid> AttemptKey = new(id => id.Value, value => PaymentAttemptId.From(value));
    public static readonly ValueConverter<RefundId, Guid> RefundKey = new(id => id.Value, value => RefundId.From(value));
    public static readonly ValueConverter<Currency, string> CurrencyCode = new(currency => currency.Code, value => Currency.FromCode(value));
    public static readonly ValueConverter<PaymentProviderCode, string> Provider = new(code => code.Value, value => PaymentProviderCode.Create(value));
    public static readonly ValueConverter<ProviderReference, string> Reference = new(reference => reference.Value, value => ProviderReference.Create(value));

    /// <summary>Null (pas de référence donnée) n'est jamais converti : la colonne reste NULL.</summary>
    public static readonly ValueConverter<ProviderReference?, string> OptionalReference = new(
        reference => reference!.Value, value => ProviderReference.Create(value));

    public static readonly ValueConverter<PaymentFailureCode?, string> FailureCode = new(
        code => code!.Value, value => PaymentFailureCode.Create(value));
    public static readonly ValueConverter<PaymentProviderId, Guid> ProviderKey = new(id => id.Value, value => PaymentProviderId.From(value));
    public static readonly ValueConverter<RestaurantPaymentConfigurationId, Guid> ConfigurationKey =
        new(id => id.Value, value => RestaurantPaymentConfigurationId.From(value));
    public static readonly ValueConverter<PaymentMethodAssignmentId, Guid> AssignmentKey =
        new(id => id.Value, value => PaymentMethodAssignmentId.From(value));
    public static readonly ValueConverter<LocationId, Guid> LocationKey = new(id => id.Value, value => LocationId.From(value));

    /// <summary>Null (sur place, mode plateforme, ou pas encore configurée) n'est jamais converti : la colonne reste NULL.</summary>
    public static readonly ValueConverter<PaymentCredentialReference?, string> SecretReference = new(
        reference => reference!.Value, value => PaymentCredentialReference.Parse(value));

    /// <summary>Moyens retenus par le restaurant : une liste jsonb de codes numériques (valeurs stables de l'enum).</summary>
    public static readonly ValueConverter<List<PaymentMethodType>, string> Methods = new(
        methods => JsonSerializer.Serialize(methods, (JsonSerializerOptions?)null),
        json => JsonSerializer.Deserialize<List<PaymentMethodType>>(json, (JsonSerializerOptions?)null) ?? new List<PaymentMethodType>());

    /// <summary>
    /// Borne de montant facultative : <c>{"amount":12.5,"currency":"XOF"}</c>, relue par <see cref="Money.Of"/> (précision
    /// revérifiée). Une valeur jsonb plutôt qu'un type complexe facultatif. Null n'est jamais converti.
    /// </summary>
    public static readonly ValueConverter<Money?, string> OptionalMoney = new(
        money => WriteMoney(money!), json => ReadMoney(json));

    public static ValueComparer<List<T>> ListComparer<T>() => new(
        (left, right) => left!.SequenceEqual(right!),
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item!.GetHashCode())),
        list => list.ToList());

    /// <summary>
    /// Capacités d'un prestataire, en une valeur jsonb relue par la fabrique du domaine (règles revérifiées). Les noms des
    /// champs JSON sont FIXÉS par attribut : renommer une propriété C# ne doit jamais rendre illisibles les lignes déjà
    /// écrites. Un champ absent d'une ligne ancienne prend sa valeur par défaut (celle de la fabrique).
    /// </summary>
    public static readonly ValueConverter<PaymentProviderCapability, string> Capability = new(
        capability => WriteCapability(capability), json => ReadCapability(json));

    /// <summary>Lecture insensible à la casse : les lignes écrites avant l'étape 7 (noms en PascalCase) restent lisibles.</summary>
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    internal static string WriteCapability(PaymentProviderCapability capability) =>
        JsonSerializer.Serialize(new CapabilityRow(
            capability.SupportedMethods.ToList(),
            capability.SupportedCountries.Select(country => country.Value).ToList(),
            capability.SupportedCurrencies.Select(currency => currency.Code).ToList(),
            capability.SupportsAuthorization,
            capability.SupportsRefund,
            capability.SupportsPartialRefund,
            capability.SupportsWebhooks,
            capability.SupportsRecurring,
            capability.PaymentExpiration is { } expiration ? (long)expiration.TotalSeconds : null,
            capability.AmountLimits.Select(limit => new AmountLimitRow(limit.Currency.Code, limit.Minimum, limit.Maximum)).ToList(),
            capability.SupportsPlatformManagedMode));

    internal static PaymentProviderCapability ReadCapability(string json)
    {
        var row = JsonSerializer.Deserialize<CapabilityRow>(json, ReadOptions) ?? throw new InvalidOperationException("Empty provider capability.");
        return PaymentProviderCapability.Create(
            row.Methods,
            (row.Countries ?? new List<string>()).Select(CountryCode.Create),
            (row.Currencies ?? new List<string>()).Select(Currency.FromCode),
            row.SupportsAuthorization,
            row.SupportsRefund,
            row.SupportsPartialRefund,
            row.SupportsWebhooks,
            row.SupportsRecurring,
            row.PaymentExpirationSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
            (row.AmountLimits ?? new List<AmountLimitRow>())
                .Select(limit => PaymentAmountLimit.Create(Currency.FromCode(limit.Currency), limit.Minimum, limit.Maximum)),
            row.SupportsPlatformManagedMode);
    }

    /// <summary>
    /// Forme stockée des capacités. CORRECTION de l'étape 7 : <c>supportsPlatformManagedMode</c> n'était pas écrit, si
    /// bien qu'un prestataire autorisant le mode plateforme le perdait au rechargement.
    /// </summary>
    internal sealed record CapabilityRow(
        [property: JsonPropertyName("methods")] List<PaymentMethodType> Methods,
        [property: JsonPropertyName("countries")] List<string>? Countries,
        [property: JsonPropertyName("currencies")] List<string>? Currencies,
        [property: JsonPropertyName("supportsAuthorization")] bool SupportsAuthorization,
        [property: JsonPropertyName("supportsRefund")] bool SupportsRefund,
        [property: JsonPropertyName("supportsPartialRefund")] bool SupportsPartialRefund,
        [property: JsonPropertyName("supportsWebhooks")] bool SupportsWebhooks,
        [property: JsonPropertyName("supportsRecurring")] bool SupportsRecurring,
        [property: JsonPropertyName("paymentExpirationSeconds")] long? PaymentExpirationSeconds,
        [property: JsonPropertyName("amountLimits")] List<AmountLimitRow>? AmountLimits,
        [property: JsonPropertyName("supportsPlatformManagedMode")] bool SupportsPlatformManagedMode = false);

    internal sealed record AmountLimitRow(
        [property: JsonPropertyName("currency")] string Currency,
        [property: JsonPropertyName("minimum")] decimal? Minimum,
        [property: JsonPropertyName("maximum")] decimal? Maximum);

    private static string WriteMoney(Money money) => JsonSerializer.Serialize(new MoneyRow(money.Amount, money.Currency.Code));

    private static Money ReadMoney(string json)
    {
        var row = JsonSerializer.Deserialize<MoneyRow>(json) ?? throw new InvalidOperationException("Empty amount bound.");
        return Money.Of(row.Amount, Currency.FromCode(row.Currency));
    }

    internal sealed record MoneyRow(
        [property: JsonPropertyName("amount")] decimal Amount,
        [property: JsonPropertyName("currency")] string Currency);

    /// <summary>Montant + devise en deux colonnes ; 19 chiffres dont 3 décimales (devises à 3 décimales comprises).</summary>
    public static void MapMoney(ComplexPropertyBuilder<Money> money, string column)
    {
        money.Property(m => m.Amount).HasColumnName(column).HasPrecision(MoneyPrecision, MoneyScale);
        money.Property(m => m.Currency)
            .HasColumnName(column + "_currency")
            .HasConversion(CurrencyCode)
            .HasMaxLength(3)
            .IsFixedLength();
    }

    public const int MoneyPrecision = 19;
    public const int MoneyScale = 3;
}

/// <summary>
/// Contraintes CHECK du schéma : garde-fous en base DERRIÈRE les règles du domaine, pour une ligne écrite hors de
/// l'application (script, correction manuelle) ou par un bug de mapping.
/// </summary>
internal static class Checks
{
    /// <summary>
    /// Une colonne d'enum ne prend que les valeurs déclarées. La liste est GÉNÉRÉE depuis l'enum : ajouter une valeur
    /// change le modèle, donc la migration (le test <c>The_migration_matches_the_model</c> le signale).
    /// </summary>
    public static string In<TEnum>(string column)
        where TEnum : struct, Enum =>
        column + " IN (" + string.Join(", ", Values<TEnum>()) + ")";

    /// <summary>Même chose pour une colonne facultative.</summary>
    public static string NullOrIn<TEnum>(string column)
        where TEnum : struct, Enum =>
        column + " IS NULL OR " + In<TEnum>(column);

    /// <summary>Code ISO 4217 : trois lettres majuscules.</summary>
    public static string Currency(string column) => column + " ~ '^[A-Z]{3}$'";

    public static IEnumerable<string> Values<TEnum>()
        where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().Select(value => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
}

/// <summary>
/// Colonne <c>tenant_id</c> des tables ENFANTS (tentatives, remboursements, affectations) : propriété cachée, recopiée de
/// la racine par l'unit of work. Toute table d'un restaurant porte ainsi son tenant (requêtes, purge, contrôle).
/// </summary>
internal static class ChildTenant
{
    public const string Property = "TenantId";

    public static void Map<T>(EntityTypeBuilder<T> builder)
        where T : class =>
        builder.Property<TenantId>(Property).HasColumnName("tenant_id").HasConversion(PaymentsConverters.TenantKey);
}
