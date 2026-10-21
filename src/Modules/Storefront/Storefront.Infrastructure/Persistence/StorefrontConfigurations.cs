using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Storefront.Domain;

namespace Storefront.Infrastructure.Persistence;

/// <summary>Conversions des ids et objets-valeurs : toute valeur relue repasse par la fabrique du domaine.</summary>
internal static class StorefrontConverters
{
    public static readonly ValueConverter<TenantId, Guid> TenantKey = new(id => id.Value, value => TenantId.From(value));
    public static readonly ValueConverter<LocationId, Guid> LocationKey = new(id => id.Value, value => LocationId.From(value));
    public static readonly ValueConverter<SalesAppId, Guid> AppKey = new(id => id.Value, value => SalesAppId.From(value));
    public static readonly ValueConverter<OnlineOrderingSettingsId, Guid> SettingsKey = new(id => id.Value, value => OnlineOrderingSettingsId.From(value));
    public static readonly ValueConverter<OnlineOrderId, Guid> OnlineOrderKey = new(id => id.Value, value => OnlineOrderId.From(value));
    public static readonly ValueConverter<OrderId, Guid> OrderKey = new(id => id.Value, value => OrderId.From(value));

    public static readonly ValueConverter<List<string>, string> Strings =
        new(values => JsonSerializer.Serialize(values, (JsonSerializerOptions?)null), json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>());

    public static readonly ValueComparer<List<string>> StringsComparer = new(
        (left, right) => left!.SequenceEqual(right!), values => values.Aggregate(0, (hash, value) => HashCode.Combine(hash, value)), values => values.ToList());

    public static readonly ValueConverter<List<LocationId>, string> Locations =
        new(ids => JsonSerializer.Serialize(ids.Select(id => id.Value).ToList(), (JsonSerializerOptions?)null),
            json => (JsonSerializer.Deserialize<List<Guid>>(json, (JsonSerializerOptions?)null) ?? new List<Guid>()).Select(LocationId.From).ToList());

    public static readonly ValueComparer<List<LocationId>> LocationsComparer = new(
        (left, right) => left!.SequenceEqual(right!), ids => ids.Aggregate(0, (hash, id) => HashCode.Combine(hash, id)), ids => ids.ToList());

    public static readonly ValueConverter<List<ServiceMode>, string> Modes =
        new(modes => JsonSerializer.Serialize(modes.Select(mode => (int)mode).ToList(), (JsonSerializerOptions?)null),
            json => (JsonSerializer.Deserialize<List<int>>(json, (JsonSerializerOptions?)null) ?? new List<int>()).Select(value => (ServiceMode)value).ToList());

    public static readonly ValueComparer<List<ServiceMode>> ModesComparer = new(
        (left, right) => left!.SequenceEqual(right!), modes => modes.Aggregate(0, (hash, mode) => HashCode.Combine(hash, mode)), modes => modes.ToList());

    public static readonly ValueConverter<List<ChannelPolicy>, string> Policies =
        new(policies => JsonSerializer.Serialize(policies.Select(PolicyRow.From).ToList(), (JsonSerializerOptions?)null),
            json => (JsonSerializer.Deserialize<List<PolicyRow>>(json, (JsonSerializerOptions?)null) ?? new List<PolicyRow>()).Select(row => row.ToPolicy()).ToList());

    public static readonly ValueComparer<List<ChannelPolicy>> PoliciesComparer = new(
        (left, right) => left!.SequenceEqual(right!), policies => policies.Aggregate(0, (hash, policy) => HashCode.Combine(hash, policy)), policies => policies.ToList());

    /// <summary>« column IN (1, 2...) », généré depuis l'enum : ajouter une valeur change le modèle, donc la migration.</summary>
    public static string In<TEnum>(string column)
        where TEnum : struct, Enum =>
        column + " IN (" + string.Join(", ", Enum.GetValues<TEnum>().Select(value => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture))) + ")";

    /// <summary>Une règle de canal telle que stockée (jsonb) ; relue par la fabrique du domaine.</summary>
    internal sealed record PolicyRow(int Channel, bool PayOnline, bool PayAtCounter, decimal? CounterLimit, int Acceptance)
    {
        public static PolicyRow From(ChannelPolicy policy) =>
            new((int)policy.Channel, policy.PayOnline, policy.PayAtCounter, policy.CounterLimit, (int)policy.Acceptance);

        public ChannelPolicy ToPolicy() =>
            ChannelPolicy.Create((SalesAppKind)Channel, PayOnline, PayAtCounter, CounterLimit, (OrderAcceptance)Acceptance);
    }
}

/// <summary>
/// Agrégat <see cref="SalesApp"/> : <c>sales_apps</c>. Seule l'empreinte de la clé est stockée ; elle est unique (c'est
/// par elle qu'une requête publique est authentifiée). Origines, établissements et pages de retour : tableaux JSON courts.
/// </summary>
internal sealed class SalesAppConfiguration : IEntityTypeConfiguration<SalesApp>
{
    public const string KeyHashIndex = "ux_sales_apps_key_hash";

    public void Configure(EntityTypeBuilder<SalesApp> builder)
    {
        builder.ToTable("sales_apps", table =>
        {
            table.HasCheckConstraint("ck_sales_apps_kind", StorefrontConverters.In<SalesAppKind>("kind"));
            table.HasCheckConstraint("ck_sales_apps_status", StorefrontConverters.In<SalesAppStatus>("status"));
        });

        builder.HasKey(app => app.Id);
        builder.Property(app => app.Id).HasColumnName("id").HasConversion(StorefrontConverters.AppKey).ValueGeneratedNever();
        builder.Property(app => app.TenantId).HasColumnName("tenant_id").HasConversion(StorefrontConverters.TenantKey);
        builder.Property(app => app.Name).HasColumnName("name").HasMaxLength(SalesApp.MaxNameLength);
        builder.Property(app => app.Kind).HasColumnName("kind");
        builder.Property(app => app.Status).HasColumnName("status");
        builder.Property(app => app.KeyHash)
            .HasColumnName("key_hash")
            .HasConversion(hash => hash.Value, value => PublishableKeyHash.Create(value))
            .HasMaxLength(PublishableKeyHash.Length)
            .IsFixedLength();
        builder.Property(app => app.KeyHint).HasColumnName("key_hint").HasMaxLength(SalesApp.KeyHintLength).IsFixedLength();
        builder.Property(app => app.KeyIssuedAtUtc).HasColumnName("key_issued_at_utc");
        builder.Property(app => app.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(app => app.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.Property(app => app.RequiresHumanCheck).HasColumnName("requires_human_check");

        builder.Property<List<string>>("_allowedOrigins")
            .HasColumnName("allowed_origins")
            .HasColumnType("jsonb")
            .HasConversion(StorefrontConverters.Strings, StorefrontConverters.StringsComparer);
        builder.Property<List<LocationId>>("_locationIds")
            .HasColumnName("location_ids")
            .HasColumnType("jsonb")
            .HasConversion(StorefrontConverters.Locations, StorefrontConverters.LocationsComparer);
        builder.Property<List<string>>("_returnUrls")
            .HasColumnName("return_urls")
            .HasColumnType("jsonb")
            .HasConversion(StorefrontConverters.Strings, StorefrontConverters.StringsComparer);
        builder.Ignore(app => app.AllowedOrigins);
        builder.Ignore(app => app.LocationIds);
        builder.Ignore(app => app.ReturnUrls);
        builder.Ignore(app => app.IsActive);

        builder.HasIndex(app => app.KeyHash).IsUnique().HasDatabaseName(KeyHashIndex);
        builder.HasIndex(app => app.TenantId).HasDatabaseName("ix_sales_apps_tenant_id");

        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(app => app.DomainEvents);
    }
}

/// <summary>Agrégat <see cref="OnlineOrderingSettings"/> : <c>online_ordering_settings</c>, un par établissement.</summary>
internal sealed class OnlineOrderingSettingsConfiguration : IEntityTypeConfiguration<OnlineOrderingSettings>
{
    public const string LocationIndex = "ux_online_ordering_settings_tenant_id_location_id";

    public void Configure(EntityTypeBuilder<OnlineOrderingSettings> builder)
    {
        builder.ToTable("online_ordering_settings", table =>
        {
            table.HasCheckConstraint(
                "ck_online_ordering_settings_preparation",
                $"preparation_minutes BETWEEN {OnlineOrderingSettings.MinPreparationMinutes} AND {OnlineOrderingSettings.MaxPreparationMinutes}");
            table.HasCheckConstraint(
                "ck_online_ordering_settings_acceptance",
                $"manual_acceptance_minutes BETWEEN {OnlineOrderingSettings.MinAcceptanceMinutes} AND {OnlineOrderingSettings.MaxAcceptanceMinutes}");
            table.HasCheckConstraint(
                "ck_online_ordering_settings_expiry",
                $"unpaid_expiry_minutes BETWEEN {OnlineOrderingSettings.MinUnpaidExpiryMinutes} AND {OnlineOrderingSettings.MaxUnpaidExpiryMinutes}");
            table.HasCheckConstraint("ck_online_ordering_settings_minimum", "minimum_order_amount IS NULL OR minimum_order_amount > 0");
            table.HasCheckConstraint(
                "ck_online_ordering_settings_slot_capacity", $"slot_capacity IS NULL OR slot_capacity BETWEEN 1 AND {OnlineOrderingSettings.MaxSlotCapacity}");
        });

        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id).HasColumnName("id").HasConversion(StorefrontConverters.SettingsKey).ValueGeneratedNever();
        builder.Property(settings => settings.TenantId).HasColumnName("tenant_id").HasConversion(StorefrontConverters.TenantKey);
        builder.Property(settings => settings.LocationId).HasColumnName("location_id").HasConversion(StorefrontConverters.LocationKey);
        builder.Property(settings => settings.IsPaused).HasColumnName("is_paused");
        builder.Property(settings => settings.PreparationMinutes).HasColumnName("preparation_minutes");
        builder.Property(settings => settings.MinimumOrderAmount).HasColumnName("minimum_order_amount").HasPrecision(19, 3);
        builder.Property(settings => settings.ManualAcceptanceMinutes).HasColumnName("manual_acceptance_minutes");
        builder.Property(settings => settings.UnpaidExpiryMinutes).HasColumnName("unpaid_expiry_minutes");
        builder.Property(settings => settings.SlotCapacity).HasColumnName("slot_capacity");
        builder.Property(settings => settings.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.Property<List<ServiceMode>>("_serviceModes")
            .HasColumnName("service_modes")
            .HasColumnType("jsonb")
            .HasConversion(StorefrontConverters.Modes, StorefrontConverters.ModesComparer);
        builder.Property<List<ChannelPolicy>>("_policies")
            .HasColumnName("channel_policies")
            .HasColumnType("jsonb")
            .HasConversion(StorefrontConverters.Policies, StorefrontConverters.PoliciesComparer);
        builder.Ignore(settings => settings.ServiceModes);
        builder.Ignore(settings => settings.Policies);

        builder.HasIndex(settings => new { settings.TenantId, settings.LocationId }).IsUnique().HasDatabaseName(LocationIndex);

        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(settings => settings.DomainEvents);
    }
}

/// <summary>
/// Agrégat <see cref="OnlineOrder"/> : <c>online_orders</c>, une ligne par commande passée en ligne. Le job lit l'index
/// partiel de l'échéance en cours (paiement ou acceptation) ; une commande n'est enregistrée qu'une fois (tenant, commande).
/// </summary>
internal sealed class OnlineOrderConfiguration : IEntityTypeConfiguration<OnlineOrder>
{
    public const string OrderIndex = "ux_online_orders_tenant_id_order_id";

    public void Configure(EntityTypeBuilder<OnlineOrder> builder)
    {
        builder.ToTable("online_orders", table =>
        {
            table.HasCheckConstraint("ck_online_orders_channel", StorefrontConverters.In<SalesAppKind>("channel"));
            table.HasCheckConstraint("ck_online_orders_payment_mode", StorefrontConverters.In<PaymentMode>("payment_mode"));
            table.HasCheckConstraint("ck_online_orders_acceptance", StorefrontConverters.In<OrderAcceptance>("acceptance"));
            table.HasCheckConstraint("ck_online_orders_state", StorefrontConverters.In<OnlineOrderState>("state"));
            table.HasCheckConstraint("ck_online_orders_acceptance_deadline", "state <> 1 OR accept_before_utc IS NOT NULL");
            table.HasCheckConstraint("ck_online_orders_payment_deadline", "state <> 5 OR pay_before_utc IS NOT NULL");
            table.HasCheckConstraint("ck_online_orders_acceptance_minutes", "acceptance_minutes BETWEEN 0 AND 1440");
        });

        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).HasColumnName("id").HasConversion(StorefrontConverters.OnlineOrderKey).ValueGeneratedNever();
        builder.Property(order => order.TenantId).HasColumnName("tenant_id").HasConversion(StorefrontConverters.TenantKey);
        builder.Property(order => order.OrderId).HasColumnName("order_id").HasConversion(StorefrontConverters.OrderKey);
        builder.Property(order => order.AppId).HasColumnName("app_id").HasConversion(StorefrontConverters.AppKey);
        builder.Property(order => order.Channel).HasColumnName("channel");
        builder.Property(order => order.LocationId).HasColumnName("location_id").HasConversion(StorefrontConverters.LocationKey);
        builder.Property(order => order.PaymentMode).HasColumnName("payment_mode");
        builder.Property(order => order.Acceptance).HasColumnName("acceptance");
        builder.Property(order => order.State).HasColumnName("state");
        builder.Property(order => order.PlacedAtUtc).HasColumnName("placed_at_utc");
        builder.Property(order => order.AcceptanceMinutes).HasColumnName("acceptance_minutes");
        builder.Property(order => order.AcceptBeforeUtc).HasColumnName("accept_before_utc");
        builder.Property(order => order.PayBeforeUtc).HasColumnName("pay_before_utc");
        builder.Property(order => order.PaidAtUtc).HasColumnName("paid_at_utc");
        builder.Property(order => order.NextDeadlineUtc).HasColumnName("next_deadline_utc");
        builder.Property(order => order.ResolvedAtUtc).HasColumnName("resolved_at_utc");
        builder.Property(order => order.SlotStartUtc).HasColumnName("slot_start_utc");
        builder.Ignore(order => order.HoldsSlot);
        builder.Ignore(order => order.IsAwaitingAcceptance);
        builder.Ignore(order => order.IsAwaitingPayment);
        builder.Ignore(order => order.IsPaidOnline);

        builder.HasIndex(order => new { order.TenantId, order.OrderId }).IsUnique().HasDatabaseName(OrderIndex);
        builder.HasIndex(order => new { order.TenantId, order.LocationId, order.SlotStartUtc })
            .HasFilter("slot_start_utc IS NOT NULL")
            .HasDatabaseName("ix_online_orders_tenant_id_location_id_slot_start_utc");
        builder.HasIndex(order => order.NextDeadlineUtc)
            .HasFilter("next_deadline_utc IS NOT NULL")
            .HasDatabaseName("ix_online_orders_next_deadline_utc");

        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(order => order.DomainEvents);
    }
}
