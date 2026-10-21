using System.Globalization;
using Billing.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Billing.Infrastructure.Persistence.Configurations;

/// <summary>
/// Conversions entre les types du domaine et les colonnes. Toute valeur relue repasse par la fabrique du domaine
/// (<c>From</c>, <c>Create</c>) : une ligne modifiée à la main qui viole une règle est refusée au chargement.
/// </summary>
internal static class BillingConverters
{
    public static readonly ValueConverter<TenantId, Guid> TenantKey = new(id => id.Value, value => TenantId.From(value));
    public static readonly ValueConverter<PlanId, Guid> PlanKey = new(id => id.Value, value => PlanId.From(value));
    public static readonly ValueConverter<SubscriptionId, Guid> SubscriptionKey = new(id => id.Value, value => SubscriptionId.From(value));
    public static readonly ValueConverter<UsageCounterId, Guid> CounterKey = new(id => id.Value, value => UsageCounterId.From(value));
    public static readonly ValueConverter<TenantAddOnId, Guid> AddOnKey = new(id => id.Value, value => TenantAddOnId.From(value));
    public static readonly ValueConverter<SubscriptionPaymentId, Guid> PaymentKey = new(id => id.Value, value => SubscriptionPaymentId.From(value));
    public static readonly ValueConverter<SubscriptionCheckoutId, Guid> CheckoutKey = new(id => id.Value, value => SubscriptionCheckoutId.From(value));
    public static readonly ValueConverter<FeatureCode, string> Feature = new(code => code.Value, value => FeatureCode.Create(value));
    public static readonly ValueConverter<Currency, string> CurrencyCode = new(currency => currency.Code, value => Currency.FromCode(value));

    public const int MoneyPrecision = 19;
    public const int MoneyScale = 3;

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

    /// <summary>Une période [début, fin) en deux colonnes.</summary>
    public static void MapPeriod(ComplexPropertyBuilder<Period> period, string prefix)
    {
        period.Property(p => p.StartUtc).HasColumnName(prefix + "_start_utc");
        period.Property(p => p.EndUtc).HasColumnName(prefix + "_end_utc");
        period.Ignore(p => p.Duration);
    }
}

/// <summary>
/// Contraintes CHECK du schéma : garde-fous en base DERRIÈRE les règles du domaine, pour une ligne écrite hors de
/// l'application (script, correction manuelle) ou par un bug de mapping.
/// </summary>
internal static class Checks
{
    /// <summary>Liste GÉNÉRÉE depuis l'enum : ajouter une valeur change le modèle, donc la migration.</summary>
    public static string In<TEnum>(string column)
        where TEnum : struct, Enum =>
        column + " IN (" + string.Join(", ", Enum.GetValues<TEnum>().Select(Value)) + ")";

    public static string NullOrIn<TEnum>(string column)
        where TEnum : struct, Enum =>
        column + " IS NULL OR " + In<TEnum>(column);

    /// <summary>Code ISO 4217 : trois lettres majuscules.</summary>
    public static string Currency(string column) => column + " ~ '^[A-Z]{3}$'";

    public static string Value<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
}
