using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Loyalty.Domain;

namespace Loyalty.Infrastructure.Persistence.Configurations;

/// <summary>
/// Agrégat <see cref="StoredValueCard"/> : table <c>stored_value_cards</c>. Le code n'y est jamais : son empreinte
/// (unique par restaurant) et ses 4 derniers caractères. Les mouvements, bornés, sont une valeur jsonb de la carte.
/// </summary>
internal sealed class StoredValueCardConfiguration : IEntityTypeConfiguration<StoredValueCard>
{
    public const string CodeHashIndex = "ix_stored_value_cards_tenant_id_code_hash";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly ValueConverter<StoredValueCardId, Guid> Key = new(id => id.Value, value => StoredValueCardId.From(value));

    private static readonly ValueConverter<List<StoredValueMovement>, string> Movements = new(
        movements => JsonSerializer.Serialize(movements, Json),
        json => Read(json));

    private static readonly ValueComparer<List<StoredValueMovement>> MovementsComparer = new(
        (left, right) => left!.SequenceEqual(right!),
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
        list => list.ToList());

    public void Configure(EntityTypeBuilder<StoredValueCard> builder)
    {
        var cancelled = Checks.Value(StoredValueStatus.Cancelled);
        builder.ToTable("stored_value_cards", table =>
        {
            table.HasCheckConstraint("ck_stored_value_cards_kind", Checks.In<StoredValueKind>("kind"));
            table.HasCheckConstraint("ck_stored_value_cards_status", Checks.In<StoredValueStatus>("status"));
            table.HasCheckConstraint("ck_stored_value_cards_currency", Checks.Currency("currency"));
            table.HasCheckConstraint("ck_stored_value_cards_amounts", "initial_amount > 0 AND balance >= 0 AND balance <= initial_amount");
            table.HasCheckConstraint("ck_stored_value_cards_cancelled", "(status = " + cancelled + ") = (cancelled_at_utc IS NOT NULL)");
            table.HasCheckConstraint("ck_stored_value_cards_cancelled_balance", "status <> " + cancelled + " OR balance = 0");
        });

        builder.HasKey(card => card.Id);
        builder.Property(card => card.Id).HasColumnName("id").HasConversion(Key).ValueGeneratedNever();
        builder.Property(card => card.TenantId).HasColumnName("tenant_id").HasConversion(LoyaltyConverters.TenantKey);
        builder.Property(card => card.Kind).HasColumnName("kind");
        builder.Property(card => card.CodeHash).HasColumnName("code_hash").HasMaxLength(StoredValueCard.CodeHashLength).IsFixedLength();
        builder.Property(card => card.CodeHint).HasColumnName("code_hint").HasMaxLength(StoredValueCard.CodeHintLength).IsFixedLength();
        builder.Property(card => card.Currency).HasColumnName("currency").HasConversion(LoyaltyConverters.CurrencyCode).HasMaxLength(3).IsFixedLength();
        builder.Property(card => card.InitialAmount).HasColumnName("initial_amount").HasPrecision(LoyaltyConverters.MoneyPrecision, LoyaltyConverters.MoneyScale);
        builder.Property(card => card.BalanceAmount).HasColumnName("balance").HasPrecision(LoyaltyConverters.MoneyPrecision, LoyaltyConverters.MoneyScale);
        // Référence par id vers un client du module Customers, sans clé étrangère (autre module).
        builder.Property(card => card.CustomerId).HasColumnName("customer_id").HasConversion(LoyaltyConverters.CustomerKey);
        builder.Property(card => card.ExpiresAtUtc).HasColumnName("expires_at_utc");
        builder.Property(card => card.Note).HasColumnName("note").HasMaxLength(StoredValueCard.MaxNoteLength);
        builder.Property(card => card.Status).HasColumnName("status");
        builder.Property(card => card.IssuedAtUtc).HasColumnName("issued_at_utc");
        builder.Property(card => card.CancelledAtUtc).HasColumnName("cancelled_at_utc");
        builder.Property(card => card.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(StoredValueCard.MaxNoteLength);
        builder.Property<List<StoredValueMovement>>("_movements")
            .HasColumnName("movements")
            .HasColumnType("jsonb")
            .HasConversion(Movements, MovementsComparer);

        // Recherche par code ; garde finale contre deux cartes au même code dans un restaurant.
        builder.HasIndex(card => new { card.TenantId, card.CodeHash }).IsUnique().HasDatabaseName(CodeHashIndex);
        builder.HasIndex(card => new { card.TenantId, card.IssuedAtUtc }).HasDatabaseName("ix_stored_value_cards_tenant_id_issued_at_utc");
        builder.HasIndex(card => new { card.TenantId, card.CustomerId })
            .HasFilter("customer_id IS NOT NULL")
            .HasDatabaseName("ix_stored_value_cards_tenant_id_customer_id");

        // Concurrence optimiste sur xmin : deux caisses qui débitent la même carte.
        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(card => card.Balance);
        builder.Ignore(card => card.Movements);
        builder.Ignore(card => card.DomainEvents);
    }

    private static List<StoredValueMovement> Read(string json)
    {
        var movements = JsonSerializer.Deserialize<List<StoredValueMovement>>(json, Json) ?? [];
        // Une ligne modifiée à la main avec un type inconnu échoue bruyamment au chargement.
        return movements.All(movement => Enum.IsDefined(movement.Type) && movement.Id != Guid.Empty)
            ? movements
            : throw new InvalidOperationException("Unknown stored value movement.");
    }
}
