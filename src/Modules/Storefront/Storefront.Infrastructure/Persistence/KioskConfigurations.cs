using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Storefront.Domain;

namespace Storefront.Infrastructure.Persistence;

internal static class KioskConverters
{
    public static readonly ValueConverter<KioskDeviceId, Guid> DeviceKey = new(id => id.Value, value => KioskDeviceId.From(value));
    public static readonly ValueConverter<KioskPairingId, Guid> PairingKey = new(id => id.Value, value => KioskPairingId.From(value));
    public static readonly ValueConverter<PublishableKeyHash, string> Hash = new(hash => hash.Value, value => PublishableKeyHash.Create(value));

    public static readonly ValueConverter<LocationId?, Guid?> OptionalLocationKey =
        new(id => id.HasValue ? (Guid?)id.Value.Value : null, value => value.HasValue ? (LocationId?)LocationId.From(value.Value) : null);

    public static readonly ValueConverter<KioskDeviceId?, Guid?> OptionalDeviceKey =
        new(id => id.HasValue ? (Guid?)id.Value.Value : null, value => value.HasValue ? (KioskDeviceId?)KioskDeviceId.From(value.Value) : null);
}

/// <summary>
/// Agrégat <see cref="KioskDevice"/> : <c>kiosk_devices</c>. Seule l'empreinte du jeton d'appareil est stockée ; elle est
/// unique, c'est par elle que la borne est authentifiée.
/// </summary>
internal sealed class KioskDeviceConfiguration : IEntityTypeConfiguration<KioskDevice>
{
    public const string TokenHashIndex = "ux_kiosk_devices_token_hash";

    public void Configure(EntityTypeBuilder<KioskDevice> builder)
    {
        builder.ToTable("kiosk_devices", table =>
        {
            table.HasCheckConstraint("ck_kiosk_devices_status", StorefrontConverters.In<KioskDeviceStatus>("status"));
            table.HasCheckConstraint("ck_kiosk_devices_revoked", "status <> 2 OR revoked_at_utc IS NOT NULL");
        });

        builder.HasKey(device => device.Id);
        builder.Property(device => device.Id).HasColumnName("id").HasConversion(KioskConverters.DeviceKey).ValueGeneratedNever();
        builder.Property(device => device.TenantId).HasColumnName("tenant_id").HasConversion(StorefrontConverters.TenantKey);
        builder.Property(device => device.AppId).HasColumnName("app_id").HasConversion(StorefrontConverters.AppKey);
        builder.Property(device => device.LocationId).HasColumnName("location_id").HasConversion(StorefrontConverters.LocationKey);
        builder.Property(device => device.Name).HasColumnName("name").HasMaxLength(KioskDevice.MaxNameLength);
        builder.Property(device => device.TokenHash)
            .HasColumnName("token_hash")
            .HasConversion(KioskConverters.Hash)
            .HasMaxLength(PublishableKeyHash.Length)
            .IsFixedLength();
        builder.Property(device => device.TokenHint).HasColumnName("token_hint").HasMaxLength(KioskDevice.TokenHintLength).IsFixedLength();
        builder.Property(device => device.Status).HasColumnName("status");
        builder.Property(device => device.PairedAtUtc).HasColumnName("paired_at_utc");
        builder.Property(device => device.RevokedAtUtc).HasColumnName("revoked_at_utc");
        builder.Ignore(device => device.IsActive);

        builder.HasIndex(device => device.TokenHash).IsUnique().HasDatabaseName(TokenHashIndex);
        builder.HasIndex(device => device.TenantId).HasDatabaseName("ix_kiosk_devices_tenant_id");

        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(device => device.DomainEvents);
    }
}

/// <summary>
/// Agrégat <see cref="KioskPairing"/> : <c>kiosk_pairings</c>. Empreintes du code (salé par le tenant) et du secret
/// seulement. Les appairages expirés restent en base mais ne servent plus.
/// </summary>
internal sealed class KioskPairingConfiguration : IEntityTypeConfiguration<KioskPairing>
{
    public void Configure(EntityTypeBuilder<KioskPairing> builder)
    {
        builder.ToTable("kiosk_pairings", table =>
        {
            table.HasCheckConstraint("ck_kiosk_pairings_status", StorefrontConverters.In<KioskPairingStatus>("status"));
            table.HasCheckConstraint("ck_kiosk_pairings_approved", "status = 1 OR (location_id IS NOT NULL AND device_name IS NOT NULL)");
            table.HasCheckConstraint("ck_kiosk_pairings_completed", "status <> 3 OR device_id IS NOT NULL");
        });

        builder.HasKey(pairing => pairing.Id);
        builder.Property(pairing => pairing.Id).HasColumnName("id").HasConversion(KioskConverters.PairingKey).ValueGeneratedNever();
        builder.Property(pairing => pairing.TenantId).HasColumnName("tenant_id").HasConversion(StorefrontConverters.TenantKey);
        builder.Property(pairing => pairing.AppId).HasColumnName("app_id").HasConversion(StorefrontConverters.AppKey);
        builder.Property(pairing => pairing.CodeHash)
            .HasColumnName("code_hash")
            .HasConversion(KioskConverters.Hash)
            .HasMaxLength(PublishableKeyHash.Length)
            .IsFixedLength();
        builder.Property(pairing => pairing.SecretHash)
            .HasColumnName("secret_hash")
            .HasConversion(KioskConverters.Hash)
            .HasMaxLength(PublishableKeyHash.Length)
            .IsFixedLength();
        builder.Property(pairing => pairing.Status).HasColumnName("status");
        builder.Property(pairing => pairing.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(pairing => pairing.ExpiresAtUtc).HasColumnName("expires_at_utc");
        builder.Property(pairing => pairing.LocationId)
            .HasColumnName("location_id")
            .HasConversion(KioskConverters.OptionalLocationKey);
        builder.Property(pairing => pairing.DeviceName).HasColumnName("device_name").HasMaxLength(KioskDevice.MaxNameLength);
        builder.Property(pairing => pairing.DeviceId)
            .HasColumnName("device_id")
            .HasConversion(KioskConverters.OptionalDeviceKey);

        builder.HasIndex(pairing => new { pairing.TenantId, pairing.CodeHash })
            .HasFilter("status = 1")
            .HasDatabaseName("ix_kiosk_pairings_tenant_id_code_hash_pending");
        builder.HasIndex(pairing => new { pairing.TenantId, pairing.ExpiresAtUtc }).HasDatabaseName("ix_kiosk_pairings_tenant_id_expires_at_utc");

        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(pairing => pairing.DomainEvents);
    }
}
