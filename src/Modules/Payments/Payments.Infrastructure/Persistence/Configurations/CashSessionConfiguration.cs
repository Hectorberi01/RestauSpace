using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Payments.Domain;

namespace Payments.Infrastructure.Persistence.Configurations;

/// <summary>
/// Agrégat <see cref="CashSession"/> : table <c>cash_sessions</c>. Mouvements, lignes du ticket Z et coupures sont des
/// valeurs sans identité, gardées en jsonb avec la session (jamais modifiées une fois la session close).
/// </summary>
internal sealed class CashSessionConfiguration : IEntityTypeConfiguration<CashSession>
{
    public const string OpenIndex = "ix_cash_sessions_tenant_id_location_id_open";
    public const string NumberIndex = "ix_cash_sessions_tenant_id_location_id_number";

    public void Configure(EntityTypeBuilder<CashSession> builder)
    {
        builder.ToTable("cash_sessions", table =>
        {
            table.HasCheckConstraint("ck_cash_sessions_number_positive", "number > 0");
            table.HasCheckConstraint("ck_cash_sessions_opening_float_not_negative", "opening_float >= 0");
            table.HasCheckConstraint("ck_cash_sessions_opening_float_currency", Checks.Currency("opening_float_currency"));
            table.HasCheckConstraint("ck_cash_sessions_status", Checks.In<CashSessionStatus>("status"));
            table.HasCheckConstraint("ck_cash_sessions_movements", "jsonb_typeof(movements) = 'array'");
            table.HasCheckConstraint("ck_cash_sessions_lines", "jsonb_typeof(lines) = 'array'");
            table.HasCheckConstraint("ck_cash_sessions_denominations", "jsonb_typeof(denominations) = 'array'");
            table.HasCheckConstraint("ck_cash_sessions_closed", "(status = 2) = (closed_at_utc IS NOT NULL)");
        });

        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).HasColumnName("id").HasConversion(CashSessionConverters.Key).ValueGeneratedNever();
        builder.Property(session => session.TenantId).HasColumnName("tenant_id").HasConversion(PaymentsConverters.TenantKey);
        builder.Property(session => session.LocationId).HasColumnName("location_id").HasConversion(PaymentsConverters.LocationKey);
        builder.Property(session => session.Number).HasColumnName("number");
        builder.Property(session => session.Status).HasColumnName("status");
        builder.ComplexProperty(session => session.OpeningFloat, money => PaymentsConverters.MapMoney(money, "opening_float"));
        builder.Property(session => session.OpenedAtUtc).HasColumnName("opened_at_utc");
        builder.Property(session => session.OpenedBy).HasColumnName("opened_by");
        builder.Property(session => session.ClosedAtUtc).HasColumnName("closed_at_utc");
        builder.Property(session => session.ClosedBy).HasColumnName("closed_by");
        builder.Property(session => session.ClosingNote).HasColumnName("closing_note").HasMaxLength(CashSession.MaxNoteLength);

        builder.Property<List<CashMovement>>("_movements")
            .HasColumnName("movements")
            .HasColumnType("jsonb")
            .HasConversion(CashSessionConverters.Movements, PaymentsConverters.ListComparer<CashMovement>());
        builder.Property<List<CashCountLine>>("_lines")
            .HasColumnName("lines")
            .HasColumnType("jsonb")
            .HasConversion(CashSessionConverters.Lines, PaymentsConverters.ListComparer<CashCountLine>());
        builder.Property<List<CashDenominationCount>>("_denominations")
            .HasColumnName("denominations")
            .HasColumnType("jsonb")
            .HasConversion(CashSessionConverters.Denominations, PaymentsConverters.ListComparer<CashDenominationCount>());

        // Une seule session ouverte par établissement, et des numéros sans doublon : garde-fous derrière le verrou.
        builder.HasIndex(session => new { session.TenantId, session.LocationId })
            .IsUnique()
            .HasFilter("status = 1")
            .HasDatabaseName(OpenIndex);
        builder.HasIndex(session => new { session.TenantId, session.LocationId, session.Number })
            .IsUnique()
            .HasDatabaseName(NumberIndex);
        builder.HasIndex(session => new { session.TenantId, session.OpenedAtUtc })
            .HasDatabaseName("ix_cash_sessions_tenant_id_opened_at_utc");

        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(session => session.DomainEvents);
        builder.Ignore(session => session.Currency);
        builder.Ignore(session => session.Movements);
        builder.Ignore(session => session.Lines);
        builder.Ignore(session => session.Denominations);
        builder.Ignore(session => session.PaidIn);
        builder.Ignore(session => session.PaidOut);
        builder.Ignore(session => session.TotalDifference);
    }
}

/// <summary>
/// Formes stockées des valeurs de la session. Les noms des champs JSON sont FIXÉS par attribut ; chaque montant garde sa
/// devise et repasse par <see cref="Money.Of"/> à la lecture.
/// </summary>
internal static class CashSessionConverters
{
    public static readonly ValueConverter<CashSessionId, Guid> Key = new(id => id.Value, value => CashSessionId.From(value));

    public static readonly ValueConverter<List<CashMovement>, string> Movements = new(list => WriteMovements(list), json => ReadMovements(json));

    public static readonly ValueConverter<List<CashCountLine>, string> Lines = new(list => WriteLines(list), json => ReadLines(json));

    public static readonly ValueConverter<List<CashDenominationCount>, string> Denominations = new(list => WriteCoins(list), json => ReadCoins(json));

    internal static string WriteMovements(List<CashMovement> list) =>
        JsonSerializer.Serialize(list.Select(item => new MovementRow(item.Kind, item.Amount.Amount, item.Amount.Currency.Code, item.Reason, item.AtUtc, item.By)).ToList());

    internal static List<CashMovement> ReadMovements(string json) =>
        (JsonSerializer.Deserialize<List<MovementRow>>(json) ?? [])
            .Select(row => new CashMovement(row.Kind, Money.Of(row.Amount, Currency.FromCode(row.Currency)), row.Reason, row.AtUtc, row.By))
            .ToList();

    internal static string WriteLines(List<CashCountLine> list) =>
        JsonSerializer.Serialize(list.Select(item => new LineRow(item.Method, item.Expected.Amount, item.Counted.Amount, item.Expected.Currency.Code)).ToList());

    internal static List<CashCountLine> ReadLines(string json) =>
        (JsonSerializer.Deserialize<List<LineRow>>(json) ?? [])
            .Select(row => new CashCountLine(row.Method, Money.Of(row.Expected, Currency.FromCode(row.Currency)), Money.Of(row.Counted, Currency.FromCode(row.Currency))))
            .ToList();

    internal static string WriteCoins(List<CashDenominationCount> list) =>
        JsonSerializer.Serialize(list.Select(item => new CoinRow(item.Value, item.Count)).ToList());

    internal static List<CashDenominationCount> ReadCoins(string json) =>
        (JsonSerializer.Deserialize<List<CoinRow>>(json) ?? []).Select(row => new CashDenominationCount(row.Value, row.Count)).ToList();

    internal sealed record MovementRow(
        [property: JsonPropertyName("kind")] CashMovementKind Kind,
        [property: JsonPropertyName("amount")] decimal Amount,
        [property: JsonPropertyName("currency")] string Currency,
        [property: JsonPropertyName("reason")] string Reason,
        [property: JsonPropertyName("atUtc")] DateTimeOffset AtUtc,
        [property: JsonPropertyName("by")] Guid? By);

    internal sealed record LineRow(
        [property: JsonPropertyName("method")] PaymentMethodType Method,
        [property: JsonPropertyName("expected")] decimal Expected,
        [property: JsonPropertyName("counted")] decimal Counted,
        [property: JsonPropertyName("currency")] string Currency);

    internal sealed record CoinRow(
        [property: JsonPropertyName("value")] decimal Value,
        [property: JsonPropertyName("count")] int Count);
}
