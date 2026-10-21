using System.Globalization;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Loyalty.Domain;

namespace Loyalty.Infrastructure.Persistence.Configurations;

/// <summary>
/// Conversions entre les types du domaine et les colonnes. Toute valeur relue repasse par la fabrique du domaine
/// (<c>From</c>, <c>Of</c>, <c>Create</c>) : une ligne modifiée à la main qui viole une règle est refusée au chargement.
/// </summary>
internal static class LoyaltyConverters
{
    public static readonly ValueConverter<TenantId, Guid> TenantKey = new(id => id.Value, value => TenantId.From(value));
    public static readonly ValueConverter<CustomerId, Guid> CustomerKey = new(id => id.Value, value => CustomerId.From(value));
    public static readonly ValueConverter<LoyaltyAccountId, Guid> AccountKey = new(id => id.Value, value => LoyaltyAccountId.From(value));
    public static readonly ValueConverter<LoyaltyTransactionId, Guid> TransactionKey =
        new(id => id.Value, value => LoyaltyTransactionId.From(value));
    public static readonly ValueConverter<RewardId, Guid> RewardKey = new(id => id.Value, value => RewardId.From(value));
    public static readonly ValueConverter<Points, int> PointsValue = new(points => points.Value, value => Points.Of(value));
    /// <summary>Colonne facultative (une clôture n'a pas de référence) : null n'est jamais converti, la colonne reste NULL.</summary>
    public static readonly ValueConverter<TransactionReference?, string> OptionalReference =
        new(reference => reference!.Value, value => TransactionReference.Create(value));
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
        column + " IN (" + string.Join(", ", Values<TEnum>()) + ")";

    /// <summary>Code ISO 4217 : trois lettres majuscules.</summary>
    public static string Currency(string column) => column + " ~ '^[A-Z]{3}$'";

    /// <summary>La valeur numérique stockée d'une valeur d'enum.</summary>
    public static string Value<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

    public static IEnumerable<string> Values<TEnum>()
        where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().Select(Value);
}
