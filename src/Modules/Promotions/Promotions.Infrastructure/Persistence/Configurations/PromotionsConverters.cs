using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Promotions.Domain;

namespace Promotions.Infrastructure.Persistence.Configurations;

/// <summary>
/// Conversions entre les types du domaine et les colonnes. Toute valeur relue repasse par la fabrique du domaine
/// (<c>From</c>, <c>Create</c>, <c>Of</c>) : une ligne modifiée à la main qui viole une règle est refusée au chargement
/// au lieu de produire un agrégat invalide.
/// </summary>
internal static class PromotionsConverters
{
    public static readonly ValueConverter<TenantId, Guid> TenantKey = new(id => id.Value, value => TenantId.From(value));
    public static readonly ValueConverter<PromotionId, Guid> PromotionKey = new(id => id.Value, value => PromotionId.From(value));
    public static readonly ValueConverter<CouponId, Guid> CouponKey = new(id => id.Value, value => CouponId.From(value));
    public static readonly ValueConverter<OrderId, Guid> OrderKey = new(id => id.Value, value => OrderId.From(value));
    public static readonly ValueConverter<Currency, string> CurrencyCode = new(currency => currency.Code, value => Currency.FromCode(value));
    public static readonly ValueConverter<CouponCode, string> Code = new(code => code.Value, value => CouponCode.Create(value));

    /// <summary>
    /// Les règles d'une promotion en UNE valeur jsonb : elles changent toujours ensemble (<c>ChangeRules</c> remplace le
    /// bloc entier) et ne sont jamais interrogées champ par champ. Ce que les requêtes filtrent (période, réservée aux
    /// coupons) est recopié dans des colonnes dédiées par l'unit of work.
    /// </summary>
    public static readonly ValueConverter<PromotionRules, string> Rules = new(rules => WriteRules(rules), json => ReadRules(json));

    public const int MoneyPrecision = 19;
    public const int MoneyScale = 3;

    /// <summary>Lecture insensible à la casse : une ligne corrigée à la main en PascalCase reste lisible.</summary>
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

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

    internal static string WriteRules(PromotionRules rules)
    {
        var benefit = rules.Benefit;
        var conditions = rules.Conditions;
        return JsonSerializer.Serialize(new RulesRow(
            rules.ValidityPeriod.StartUtc,
            rules.ValidityPeriod.EndUtc,
            rules.RequiresCoupon,
            rules.UsageLimit?.Value,
            benefit is null ? null : new BenefitRow(benefit.Type, benefit.Percentage, ToRow(benefit.Amount), ToRow(benefit.MaxDiscount)),
            new ConditionsRow(
                ToRow(conditions.MinimumOrderAmount),
                conditions.ProductIds.Select(id => id.Value).ToList(),
                conditions.LocationIds.Select(id => id.Value).ToList(),
                conditions.OrderTypes.ToList())));
    }

    internal static PromotionRules ReadRules(string json)
    {
        var row = JsonSerializer.Deserialize<RulesRow>(json, ReadOptions) ?? throw new InvalidOperationException("Empty promotion rules.");
        var conditions = row.Conditions is null
            ? Conditions.None
            : Conditions.Create(
                FromRow(row.Conditions.MinimumOrderAmount),
                (row.Conditions.ProductIds ?? new List<Guid>()).Select(ProductId.From),
                (row.Conditions.LocationIds ?? new List<Guid>()).Select(LocationId.From),
                row.Conditions.OrderTypes ?? new List<OrderType>());

        return PromotionRules.Create(
            Period.Create(row.ValidFromUtc, row.ValidUntilUtc),
            conditions,
            ReadBenefit(row.Benefit),
            row.UsageLimit is { } limit ? UsageLimit.Of(limit) : null,
            row.RequiresCoupon);
    }

    private static Benefit? ReadBenefit(BenefitRow? row)
    {
        if (row is null)
        {
            return null;
        }

        return row.Type switch
        {
            BenefitType.Percentage => Benefit.PercentageOff(
                row.Percentage ?? throw new InvalidOperationException("A percentage benefit without percentage."),
                FromRow(row.MaxDiscount)),
            BenefitType.FixedAmount => Benefit.FixedAmountOff(
                FromRow(row.Amount) ?? throw new InvalidOperationException("A fixed benefit without amount.")),
            _ => throw new InvalidOperationException("Unknown benefit type " + ((int)row.Type).ToString(CultureInfo.InvariantCulture) + "."),
        };
    }

    private static MoneyRow? ToRow(Money? money) => money is null ? null : new MoneyRow(money.Amount, money.Currency.Code);

    private static Money? FromRow(MoneyRow? row) => row is null ? null : Money.Of(row.Amount, Currency.FromCode(row.Currency));

    /// <summary>
    /// Forme stockée des règles. Les noms JSON sont FIXÉS par attribut : renommer une propriété C# ne doit jamais rendre
    /// illisibles les lignes déjà écrites. Les enums sont écrits en nombre (valeurs stables, jamais renumérotées).
    /// </summary>
    internal sealed record RulesRow(
        [property: JsonPropertyName("validFromUtc")] DateTimeOffset ValidFromUtc,
        [property: JsonPropertyName("validUntilUtc")] DateTimeOffset ValidUntilUtc,
        [property: JsonPropertyName("requiresCoupon")] bool RequiresCoupon,
        [property: JsonPropertyName("usageLimit")] int? UsageLimit,
        [property: JsonPropertyName("benefit")] BenefitRow? Benefit,
        [property: JsonPropertyName("conditions")] ConditionsRow? Conditions);

    internal sealed record BenefitRow(
        [property: JsonPropertyName("type")] BenefitType Type,
        [property: JsonPropertyName("percentage")] decimal? Percentage,
        [property: JsonPropertyName("amount")] MoneyRow? Amount,
        [property: JsonPropertyName("maxDiscount")] MoneyRow? MaxDiscount);

    internal sealed record ConditionsRow(
        [property: JsonPropertyName("minimumOrderAmount")] MoneyRow? MinimumOrderAmount,
        [property: JsonPropertyName("productIds")] List<Guid>? ProductIds,
        [property: JsonPropertyName("locationIds")] List<Guid>? LocationIds,
        [property: JsonPropertyName("orderTypes")] List<OrderType>? OrderTypes);

    internal sealed record MoneyRow(
        [property: JsonPropertyName("amount")] decimal Amount,
        [property: JsonPropertyName("currency")] string Currency);
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

    /// <summary>Code ISO 4217 : trois lettres majuscules.</summary>
    public static string Currency(string column) => column + " ~ '^[A-Z]{3}$'";

    public static IEnumerable<string> Values<TEnum>()
        where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().Select(value => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
}
