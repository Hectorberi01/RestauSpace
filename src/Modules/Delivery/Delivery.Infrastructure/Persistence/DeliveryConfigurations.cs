using System.Globalization;
using Delivery.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Delivery.Infrastructure.Persistence;

internal static class DeliveryConverters
{
    public static readonly ValueConverter<TenantId, Guid> TenantKey = new(id => id.Value, value => TenantId.From(value));
    public static readonly ValueConverter<LocationId, Guid> LocationKey = new(id => id.Value, value => LocationId.From(value));
    public static readonly ValueConverter<OrderId, Guid> OrderKey = new(id => id.Value, value => OrderId.From(value));
    public static readonly ValueConverter<DeliverySettingsId, Guid> SettingsKey = new(id => id.Value, value => DeliverySettingsId.From(value));
    public static readonly ValueConverter<DeliveryZoneId, Guid> ZoneKey = new(id => id.Value, value => DeliveryZoneId.From(value));
    public static readonly ValueConverter<FulfillmentId, Guid> FulfillmentKey = new(id => id.Value, value => FulfillmentId.From(value));
    public static readonly ValueConverter<CourierId, Guid> CourierKey = new(id => id.Value, value => CourierId.From(value));
    public static readonly ValueConverter<CourierRemittanceId, Guid> RemittanceKey = new(id => id.Value, value => CourierRemittanceId.From(value));
    public static readonly ValueConverter<Currency, string> CurrencyCode = new(currency => currency.Code, value => Currency.FromCode(value));
    public static readonly ValueConverter<Currency?, string> OptionalCurrencyCode = new(currency => currency!.Code, value => Currency.FromCode(value));
    public static readonly ValueConverter<PhoneNumber?, string> Phone = new(phone => phone!.Value, value => PhoneNumber.Create(value));

    /// <summary>Contrainte CHECK « colonne IN (valeurs de l'enum) ».</summary>
    public static string In<TEnum>(string column)
        where TEnum : struct, Enum =>
        column + " IN (" + string.Join(", ", Enum.GetValues<TEnum>().Select(value => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture))) + ")";

    /// <summary>Montant + devise en deux colonnes (19 chiffres dont 3 décimales, comme Catalog).</summary>
    public static void MapMoney(ComplexPropertyBuilder<Money> money, string amountColumn, string currencyColumn)
    {
        money.Property(m => m.Amount).HasColumnName(amountColumn).HasPrecision(19, 3);
        money.Property(m => m.Currency)
            .HasColumnName(currencyColumn)
            .HasConversion(CurrencyCode)
            .HasMaxLength(3)
            .IsFixedLength();
    }
}

internal sealed class DeliverySettingsConfiguration : IEntityTypeConfiguration<DeliverySettings>
{
    public const string OwnerProperty = "SettingsId";

    /// <summary>Un seul jeu de réglages par établissement : garde-fou derrière la création à la première modification.</summary>
    public const string LocationIndex = "ix_delivery_settings_tenant_id_location_id";

    public void Configure(EntityTypeBuilder<DeliverySettings> builder)
    {
        builder.ToTable("delivery_settings", table =>
        {
            table.HasCheckConstraint("ck_delivery_settings_pickup_lead", "pickup_lead_minutes BETWEEN 0 AND 240");
            table.HasCheckConstraint("ck_delivery_settings_delivery_lead", "delivery_lead_minutes BETWEEN 0 AND 240");
        });

        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id).HasColumnName("id").HasConversion(DeliveryConverters.SettingsKey).ValueGeneratedNever();
        builder.Property(settings => settings.TenantId).HasColumnName("tenant_id").HasConversion(DeliveryConverters.TenantKey);
        builder.Property(settings => settings.LocationId).HasColumnName("location_id").HasConversion(DeliveryConverters.LocationKey);
        builder.Property(settings => settings.Currency).HasColumnName("currency").HasConversion(DeliveryConverters.CurrencyCode).HasMaxLength(3).IsFixedLength();
        builder.Property(settings => settings.PickupEnabled).HasColumnName("pickup_enabled");
        builder.Property(settings => settings.PickupLeadMinutes).HasColumnName("pickup_lead_minutes");
        builder.Property(settings => settings.DeliveryEnabled).HasColumnName("delivery_enabled");
        builder.Property(settings => settings.DeliveryLeadMinutes).HasColumnName("delivery_lead_minutes");
        builder.Property(settings => settings.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(settings => settings.UpdatedAtUtc).HasColumnName("updated_at_utc");

        // Concurrence optimiste sur xmin ; chaque changement de zone réécrit aussi les réglages (date de mise à jour).
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasMany(settings => settings.Zones)
            .WithOne()
            .HasForeignKey(OwnerProperty)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(settings => settings.Zones).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(settings => new { settings.TenantId, settings.LocationId }).IsUnique().HasDatabaseName(LocationIndex);

        builder.Ignore(settings => settings.DomainEvents);
    }
}

internal sealed class DeliveryZoneConfiguration : IEntityTypeConfiguration<DeliveryZone>
{
    public void Configure(EntityTypeBuilder<DeliveryZone> builder)
    {
        builder.ToTable("delivery_zones", table =>
        {
            table.HasCheckConstraint("ck_delivery_zones_minutes", "estimated_minutes BETWEEN 5 AND 240");
            table.HasCheckConstraint("ck_delivery_zones_amounts", "fee >= 0 AND minimum_order >= 0");
        });

        builder.HasKey(zone => zone.Id);
        // Jamais généré : une zone atteinte via ses réglages avec un id déjà défini est une NOUVELLE ligne.
        builder.Property(zone => zone.Id).HasColumnName("id").HasConversion(DeliveryConverters.ZoneKey).ValueGeneratedNever();
        builder.Property<DeliverySettingsId>(DeliverySettingsConfiguration.OwnerProperty).HasColumnName("settings_id").HasConversion(DeliveryConverters.SettingsKey);
        builder.Property(zone => zone.Name).HasColumnName("name").HasMaxLength(DeliveryZone.NameMaxLength);
        builder.ComplexProperty(zone => zone.Fee, fee => DeliveryConverters.MapMoney(fee, "fee", "fee_currency"));
        builder.ComplexProperty(zone => zone.MinimumOrder, minimum => DeliveryConverters.MapMoney(minimum, "minimum_order", "minimum_order_currency"));
        builder.Property(zone => zone.EstimatedMinutes).HasColumnName("estimated_minutes");
        builder.Property(zone => zone.IsActive).HasColumnName("is_active");

        builder.HasIndex(DeliverySettingsConfiguration.OwnerProperty).HasDatabaseName("ix_delivery_zones_settings_id");
    }
}

internal sealed class FulfillmentConfiguration : IEntityTypeConfiguration<Fulfillment>
{
    /// <summary>Une seule fiche active (non annulée) par commande : garde-fou derrière le contrôle fait avant d'écrire.</summary>
    public const string OrderIndex = "ix_fulfillments_tenant_id_order_id_active";

    public void Configure(EntityTypeBuilder<Fulfillment> builder)
    {
        builder.ToTable("fulfillments", table =>
        {
            table.HasCheckConstraint("ck_fulfillments_mode", DeliveryConverters.In<FulfillmentMode>("mode"));
            table.HasCheckConstraint("ck_fulfillments_status", DeliveryConverters.In<FulfillmentStatus>("status"));
            // Une livraison a une adresse, un téléphone, une zone et des frais ; un retrait n'en a pas.
            table.HasCheckConstraint(
                "ck_fulfillments_delivery_details",
                "(mode = 2 AND address_line IS NOT NULL AND customer_phone IS NOT NULL AND zone_id IS NOT NULL AND delivery_fee IS NOT NULL AND fee_currency IS NOT NULL) "
                + "OR (mode = 1 AND address_line IS NULL AND zone_id IS NULL AND delivery_fee IS NULL AND courier_name IS NULL)");
            table.HasCheckConstraint("ck_fulfillments_failed", "status <> 6 OR failure_reason IS NOT NULL");
            table.HasCheckConstraint("ck_fulfillments_collection_method", "collection_method IS NULL OR " + DeliveryConverters.In<CollectionMethod>("collection_method"));
            // Encaissé : un mode, un montant positif au plus égal au dû ; jamais sans montant à encaisser.
            table.HasCheckConstraint(
                "ck_fulfillments_collection",
                "(amount_collected IS NULL AND collection_method IS NULL) "
                + "OR (amount_collected > 0 AND collection_method IS NOT NULL AND amount_to_collect IS NOT NULL AND amount_collected <= amount_to_collect)");
            table.HasCheckConstraint("ck_fulfillments_amounts", "(amount_to_collect IS NULL OR amount_to_collect > 0) AND (courier_fee IS NULL OR courier_fee >= 0)");
        });

        builder.HasKey(fulfillment => fulfillment.Id);
        builder.Property(fulfillment => fulfillment.Id).HasColumnName("id").HasConversion(DeliveryConverters.FulfillmentKey).ValueGeneratedNever();
        builder.Property(fulfillment => fulfillment.TenantId).HasColumnName("tenant_id").HasConversion(DeliveryConverters.TenantKey);
        builder.Property(fulfillment => fulfillment.OrderId).HasColumnName("order_id").HasConversion(DeliveryConverters.OrderKey);
        builder.Property(fulfillment => fulfillment.LocationId).HasColumnName("location_id").HasConversion(DeliveryConverters.LocationKey);
        // Numériques : les valeurs des enums sont explicites et stables.
        builder.Property(fulfillment => fulfillment.Mode).HasColumnName("mode");
        builder.Property(fulfillment => fulfillment.Status).HasColumnName("status");
        builder.Property(fulfillment => fulfillment.CustomerName).HasColumnName("customer_name").HasMaxLength(Fulfillment.CustomerNameMaxLength);
        builder.Property(fulfillment => fulfillment.CustomerPhone).HasColumnName("customer_phone").HasConversion(DeliveryConverters.Phone).HasMaxLength(32);
        builder.Property(fulfillment => fulfillment.PromisedAtUtc).HasColumnName("promised_at_utc");
        builder.Property(fulfillment => fulfillment.AddressLine).HasColumnName("address_line").HasMaxLength(Fulfillment.AddressMaxLength);
        builder.Property(fulfillment => fulfillment.Landmark).HasColumnName("landmark").HasMaxLength(Fulfillment.LandmarkMaxLength);
        builder.Property(fulfillment => fulfillment.Instructions).HasColumnName("instructions").HasMaxLength(Fulfillment.InstructionsMaxLength);
        builder.Property(fulfillment => fulfillment.ZoneId).HasColumnName("zone_id").HasConversion(DeliveryConverters.ZoneKey);
        builder.Property(fulfillment => fulfillment.ZoneName).HasColumnName("zone_name").HasMaxLength(DeliveryZone.NameMaxLength);
        builder.Property(fulfillment => fulfillment.DeliveryFeeAmount).HasColumnName("delivery_fee").HasPrecision(19, 3);
        builder.Property(fulfillment => fulfillment.FeeCurrency).HasColumnName("fee_currency").HasConversion(DeliveryConverters.OptionalCurrencyCode).HasMaxLength(3).IsFixedLength();
        builder.Property(fulfillment => fulfillment.CourierName).HasColumnName("courier_name").HasMaxLength(Fulfillment.CourierNameMaxLength);
        builder.Property(fulfillment => fulfillment.CourierPhone).HasColumnName("courier_phone").HasConversion(DeliveryConverters.Phone).HasMaxLength(32);
        builder.Property(fulfillment => fulfillment.CourierId).HasColumnName("courier_id").HasConversion(DeliveryConverters.CourierKey);
        builder.Property(fulfillment => fulfillment.CourierFeeAmount).HasColumnName("courier_fee").HasPrecision(19, 3);
        builder.Property(fulfillment => fulfillment.AmountToCollect).HasColumnName("amount_to_collect").HasPrecision(19, 3);
        builder.Property(fulfillment => fulfillment.AmountCollected).HasColumnName("amount_collected").HasPrecision(19, 3);
        builder.Property(fulfillment => fulfillment.CollectionMethod).HasColumnName("collection_method");
        builder.Property(fulfillment => fulfillment.RemittanceId).HasColumnName("remittance_id").HasConversion(DeliveryConverters.RemittanceKey);
        builder.Property(fulfillment => fulfillment.RemittedAtUtc).HasColumnName("remitted_at_utc");
        builder.Property(fulfillment => fulfillment.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(fulfillment => fulfillment.ReadyAtUtc).HasColumnName("ready_at_utc");
        builder.Property(fulfillment => fulfillment.AssignedAtUtc).HasColumnName("assigned_at_utc");
        builder.Property(fulfillment => fulfillment.DepartedAtUtc).HasColumnName("departed_at_utc");
        builder.Property(fulfillment => fulfillment.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(fulfillment => fulfillment.FailedAtUtc).HasColumnName("failed_at_utc");
        builder.Property(fulfillment => fulfillment.FailureReason).HasColumnName("failure_reason").HasMaxLength(Fulfillment.ReasonMaxLength);
        builder.Property(fulfillment => fulfillment.CancelledAtUtc).HasColumnName("cancelled_at_utc");
        builder.Property(fulfillment => fulfillment.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(Fulfillment.ReasonMaxLength);

        // Concurrence optimiste sur la colonne système xmin de PostgreSQL (deux personnes qui font avancer la même fiche).
        builder.Property<uint>("Version").IsRowVersion();

        // Écran de suivi : par établissement, les fiches ouvertes de la promesse la plus proche à la plus lointaine.
        builder.HasIndex(fulfillment => new { fulfillment.TenantId, fulfillment.LocationId, fulfillment.Status, fulfillment.PromisedAtUtc })
            .HasDatabaseName("ix_fulfillments_tenant_id_location_id_status_promised_at_utc");
        builder.HasIndex(fulfillment => new { fulfillment.TenantId, fulfillment.CreatedAtUtc })
            .HasDatabaseName("ix_fulfillments_tenant_id_created_at_utc");
        builder.HasIndex(fulfillment => new { fulfillment.TenantId, fulfillment.OrderId })
            .IsUnique()
            .HasFilter("status <> 7")
            .HasDatabaseName(OrderIndex);

        // Argent des livreurs : les livraisons terminées pas encore remises, par établissement.
        builder.HasIndex(fulfillment => new { fulfillment.TenantId, fulfillment.LocationId, fulfillment.CompletedAtUtc })
            .HasFilter("mode = 2 AND status = 5 AND remittance_id IS NULL")
            .HasDatabaseName("ix_fulfillments_awaiting_remittance");

        builder.Ignore(fulfillment => fulfillment.DeliveryFee);
        builder.Ignore(fulfillment => fulfillment.IsClosed);
        builder.Ignore(fulfillment => fulfillment.CashHeld);
        builder.Ignore(fulfillment => fulfillment.AwaitsRemittance);
        builder.Ignore(fulfillment => fulfillment.DomainEvents);
    }
}

internal sealed class CourierConfiguration : IEntityTypeConfiguration<Courier>
{
    public void Configure(EntityTypeBuilder<Courier> builder)
    {
        builder.ToTable("couriers", table =>
        {
            table.HasCheckConstraint("ck_couriers_kind", DeliveryConverters.In<CourierKind>("kind"));
            table.HasCheckConstraint("ck_couriers_fee", "(fee IS NULL AND fee_currency IS NULL) OR (fee >= 0 AND fee_currency IS NOT NULL)");
        });

        builder.HasKey(courier => courier.Id);
        builder.Property(courier => courier.Id).HasColumnName("id").HasConversion(DeliveryConverters.CourierKey).ValueGeneratedNever();
        builder.Property(courier => courier.TenantId).HasColumnName("tenant_id").HasConversion(DeliveryConverters.TenantKey);
        builder.Property(courier => courier.Name).HasColumnName("name").HasMaxLength(Courier.NameMaxLength);
        builder.Property(courier => courier.Phone).HasColumnName("phone").HasConversion(DeliveryConverters.Phone).HasMaxLength(32);
        builder.Property(courier => courier.Kind).HasColumnName("kind");
        builder.Property(courier => courier.FeeAmount).HasColumnName("fee").HasPrecision(19, 3);
        builder.Property(courier => courier.FeeCurrency).HasColumnName("fee_currency").HasConversion(DeliveryConverters.OptionalCurrencyCode).HasMaxLength(3).IsFixedLength();
        builder.Property(courier => courier.IsActive).HasColumnName("is_active");
        builder.Property(courier => courier.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(courier => courier.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.Property<uint>("Version").IsRowVersion();

        builder.HasIndex(courier => new { courier.TenantId, courier.IsActive }).HasDatabaseName("ix_couriers_tenant_id_is_active");

        builder.Ignore(courier => courier.Fee);
        builder.Ignore(courier => courier.DomainEvents);
    }
}

internal sealed class CourierRemittanceConfiguration : IEntityTypeConfiguration<CourierRemittance>
{
    public const string OwnerProperty = "RemittanceId";

    /// <summary>Une course n'est soldée qu'une fois : garde-fou derrière la concurrence optimiste sur les fiches.</summary>
    public const string FulfillmentIndex = "ix_courier_remittance_items_fulfillment_id";

    public void Configure(EntityTypeBuilder<CourierRemittance> builder)
    {
        builder.ToTable("courier_remittances", table =>
            table.HasCheckConstraint("ck_courier_remittances_amounts", "cash_collected >= 0 AND courier_fees >= 0 AND expected = cash_collected - courier_fees"));

        builder.HasKey(remittance => remittance.Id);
        builder.Property(remittance => remittance.Id).HasColumnName("id").HasConversion(DeliveryConverters.RemittanceKey).ValueGeneratedNever();
        builder.Property(remittance => remittance.TenantId).HasColumnName("tenant_id").HasConversion(DeliveryConverters.TenantKey);
        builder.Property(remittance => remittance.LocationId).HasColumnName("location_id").HasConversion(DeliveryConverters.LocationKey);
        builder.Property(remittance => remittance.CourierId).HasColumnName("courier_id").HasConversion(DeliveryConverters.CourierKey);
        builder.Property(remittance => remittance.CourierName).HasColumnName("courier_name").HasMaxLength(Fulfillment.CourierNameMaxLength);
        builder.Property(remittance => remittance.Currency).HasColumnName("currency").HasConversion(DeliveryConverters.CurrencyCode).HasMaxLength(3).IsFixedLength();
        builder.Property(remittance => remittance.CashCollected).HasColumnName("cash_collected").HasPrecision(19, 3);
        builder.Property(remittance => remittance.CourierFees).HasColumnName("courier_fees").HasPrecision(19, 3);
        builder.Property(remittance => remittance.Expected).HasColumnName("expected").HasPrecision(19, 3);
        builder.Property(remittance => remittance.Counted).HasColumnName("counted").HasPrecision(19, 3);
        builder.Property(remittance => remittance.Note).HasColumnName("note").HasMaxLength(CourierRemittance.NoteMaxLength);
        builder.Property(remittance => remittance.RecordedBy).HasColumnName("recorded_by");
        builder.Property(remittance => remittance.RecordedAtUtc).HasColumnName("recorded_at_utc");

        builder.OwnsMany(remittance => remittance.Items, item =>
        {
            item.ToTable("courier_remittance_items");
            item.WithOwner().HasForeignKey(OwnerProperty);
            item.Property<CourierRemittanceId>(OwnerProperty).HasColumnName("remittance_id").HasConversion(DeliveryConverters.RemittanceKey);
            item.Property(line => line.FulfillmentId).HasColumnName("fulfillment_id").HasConversion(DeliveryConverters.FulfillmentKey);
            item.HasKey(OwnerProperty, nameof(CourierRemittanceItem.FulfillmentId));
            item.Property(line => line.OrderId).HasColumnName("order_id").HasConversion(DeliveryConverters.OrderKey);
            item.Property(line => line.CashCollected).HasColumnName("cash_collected").HasPrecision(19, 3);
            item.Property(line => line.CourierFee).HasColumnName("courier_fee").HasPrecision(19, 3);
            item.HasIndex(line => line.FulfillmentId).IsUnique().HasDatabaseName(FulfillmentIndex);
        });
        builder.Navigation(remittance => remittance.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(remittance => new { remittance.TenantId, remittance.LocationId, remittance.RecordedAtUtc })
            .HasDatabaseName("ix_courier_remittances_tenant_id_location_id_recorded_at_utc");

        builder.Ignore(remittance => remittance.Difference);
        builder.Ignore(remittance => remittance.DomainEvents);
    }
}
