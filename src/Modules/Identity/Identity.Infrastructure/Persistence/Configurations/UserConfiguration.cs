using System.Text.Json;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Identity.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public const string EmailIndex = "ix_users_email";

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => UserId.From(value))
            .ValueGeneratedNever();

        builder.Property(user => user.Email)
            .HasColumnName("email")
            .HasConversion(email => email.Value, value => Email.Create(value))
            .HasMaxLength(Email.MaxLength);
        // Garde-fou final contre deux inscriptions concurrentes sur la même adresse (normalisée).
        builder.HasIndex(user => user.Email).IsUnique().HasDatabaseName(EmailIndex);

        builder.Property(user => user.PasswordHash)
            .HasColumnName("password_hash")
            .HasConversion(hash => hash!.Value, value => PasswordHash.Create(value))
            .HasMaxLength(PasswordHash.MaxLength);

        builder.Property(user => user.Status).HasColumnName("status");
        builder.Property(user => user.StatusBeforeLock).HasColumnName("status_before_lock");
        builder.Property(user => user.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(user => user.EmailVerifiedAtUtc).HasColumnName("email_verified_at_utc");
        builder.Property(user => user.PasswordChangedAtUtc).HasColumnName("password_changed_at_utc");
        builder.Property(user => user.ErasedAtUtc).HasColumnName("erased_at_utc");
        builder.Property(user => user.FirstName).HasColumnName("first_name").HasMaxLength(User.NameMaxLength);
        builder.Property(user => user.LastName).HasColumnName("last_name").HasMaxLength(User.NameMaxLength);
        builder.Property(user => user.Phone)
            .HasColumnName("phone")
            .HasConversion(new ValueConverter<PhoneNumber?, string>(phone => phone!.Value, value => PhoneNumber.Create(value)))
            .HasMaxLength(32);
        builder.Property(user => user.City).HasColumnName("city").HasMaxLength(User.CityMaxLength);
        builder.Property(user => user.Language).HasColumnName("language").HasMaxLength(8).HasDefaultValue(User.DefaultLanguage);
        builder.Property(user => user.TimeZone).HasColumnName("time_zone").HasMaxLength(User.TimeZoneMaxLength).HasDefaultValue(User.DefaultTimeZone);
        builder.Property(user => user.ProductNewsEmails).HasColumnName("product_news_emails");
        builder.Property(user => user.AvatarUpdatedAtUtc).HasColumnName("avatar_updated_at_utc");
        // Secret de l'application d'authentification : chiffré par ITwoFactorSecrets avant d'arriver ici, jamais en clair.
        builder.Property(user => user.TwoFactorSecret).HasColumnName("two_factor_secret").HasMaxLength(User.MaxProtectedSecretLength);
        builder.Property(user => user.PendingTwoFactorSecret).HasColumnName("pending_two_factor_secret").HasMaxLength(User.MaxProtectedSecretLength);
        builder.Property(user => user.TwoFactorEnabledAtUtc).HasColumnName("two_factor_enabled_at_utc");
        builder.Property(user => user.LastTwoFactorStep).HasColumnName("last_two_factor_step");
        builder.Property<List<string>>("_recoveryCodeHashes")
            .HasColumnName("recovery_code_hashes")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .HasConversion(
                new ValueConverter<List<string>, string>(
                    hashes => JsonSerializer.Serialize(hashes, (JsonSerializerOptions?)null),
                    json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>()),
                new ValueComparer<List<string>>(
                    (left, right) => left!.SequenceEqual(right!), hashes => hashes.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)), hashes => hashes.ToList()));

        // Concurrence optimiste sur la colonne système xmin de PostgreSQL.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasMany(user => user.Sessions)
            .WithOne()
            .HasForeignKey("UserId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(user => user.Sessions).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(user => user.DomainEvents);
        builder.Ignore(user => user.IsEmailVerified);
        builder.Ignore(user => user.HasPassword);
        builder.Ignore(user => user.IsErased);
        builder.Ignore(user => user.CanStartSession);
        builder.Ignore(user => user.HasAvatar);
        builder.Ignore(user => user.IsTwoFactorEnabled);
        builder.Ignore(user => user.RecoveryCodesLeft);
    }
}

internal sealed class RefreshSessionConfiguration : IEntityTypeConfiguration<RefreshSession>
{
    public void Configure(EntityTypeBuilder<RefreshSession> builder)
    {
        var sessionId = new ValueConverter<SessionId, Guid>(id => id.Value, value => SessionId.From(value));

        builder.ToTable("sessions");

        builder.HasKey(session => session.Id);
        // Jamais généré : une session atteinte via son utilisateur avec un id déjà défini est une NOUVELLE ligne.
        builder.Property(session => session.Id).HasColumnName("id").HasConversion(sessionId).ValueGeneratedNever();
        builder.Property<UserId>("UserId")
            .HasColumnName("user_id")
            .HasConversion(id => id.Value, value => UserId.From(value));

        builder.Property(session => session.Hash)
            .HasColumnName("token_hash")
            .HasConversion(hash => hash.Value, value => TokenHash.Create(value))
            .HasMaxLength(TokenHash.MaxLength);
        // L'endpoint de refresh retrouve l'utilisateur à partir du token présenté.
        builder.HasIndex(session => session.Hash).IsUnique().HasDatabaseName("ix_sessions_token_hash");

        builder.Property(session => session.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(session => session.ExpiresAtUtc).HasColumnName("expires_at_utc");
        builder.Property(session => session.RevokedAtUtc).HasColumnName("revoked_at_utc");
        builder.Property(session => session.ReplacedBy).HasColumnName("replaced_by").HasConversion(sessionId);
        builder.Property(session => session.RevocationReason).HasColumnName("revocation_reason");
        builder.Property(session => session.DeviceLabel).HasColumnName("device_label").HasMaxLength(RefreshSession.DeviceLabelMaxLength);

        builder.Ignore(session => session.IsRevoked);
        builder.Ignore(session => session.WasRotated);
    }
}
