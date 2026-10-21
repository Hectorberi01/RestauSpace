using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Storefront.Domain;

namespace Storefront.Infrastructure.Persistence;

/// <summary>
/// Agrégat <see cref="CustomerSession"/> : <c>customer_sessions</c>. Seule l'empreinte du jeton est stockée ; elle est
/// unique, c'est par elle que le client est reconnu. Aucune coordonnée : le client est désigné par son id.
/// </summary>
internal sealed class CustomerSessionConfiguration : IEntityTypeConfiguration<CustomerSession>
{
    public const string TokenHashIndex = "ux_customer_sessions_token_hash";

    public void Configure(EntityTypeBuilder<CustomerSession> builder)
    {
        builder.ToTable("customer_sessions", table => table.HasCheckConstraint("ck_customer_sessions_expiry", "expires_at_utc > created_at_utc"));

        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<CustomerSessionId, Guid>(id => id.Value, value => CustomerSessionId.From(value)))
            .ValueGeneratedNever();
        builder.Property(session => session.TenantId).HasColumnName("tenant_id").HasConversion(StorefrontConverters.TenantKey);
        builder.Property(session => session.AppId).HasColumnName("app_id").HasConversion(StorefrontConverters.AppKey);
        builder.Property(session => session.CustomerId)
            .HasColumnName("customer_id")
            .HasConversion(new ValueConverter<CustomerId, Guid>(id => id.Value, value => CustomerId.From(value)));
        builder.Property(session => session.TokenHash)
            .HasColumnName("token_hash")
            .HasConversion(KioskConverters.Hash)
            .HasMaxLength(PublishableKeyHash.Length)
            .IsFixedLength();
        builder.Property(session => session.TokenHint).HasColumnName("token_hint").HasMaxLength(CustomerSession.TokenHintLength).IsFixedLength();
        builder.Property(session => session.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(session => session.ExpiresAtUtc).HasColumnName("expires_at_utc");
        builder.Property(session => session.RevokedAtUtc).HasColumnName("revoked_at_utc");

        builder.HasIndex(session => session.TokenHash).IsUnique().HasDatabaseName(TokenHashIndex);
        builder.HasIndex(session => new { session.TenantId, session.CustomerId }).HasDatabaseName("ix_customer_sessions_tenant_id_customer_id");

        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(session => session.DomainEvents);
    }
}
