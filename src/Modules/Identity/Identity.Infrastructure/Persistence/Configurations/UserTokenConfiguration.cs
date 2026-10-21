using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

internal sealed class UserTokenConfiguration : IEntityTypeConfiguration<UserToken>
{
    public const string UserPurposeIndex = "ix_user_tokens_user_purpose";

    public void Configure(EntityTypeBuilder<UserToken> builder)
    {
        builder.ToTable("user_tokens");

        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(token => token.UserId)
            .HasColumnName("user_id")
            .HasConversion(id => id.Value, value => UserId.From(value));
        builder.Property(token => token.Purpose).HasColumnName("purpose");
        builder.Property(token => token.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsFixedLength();
        builder.Property(token => token.ExpiresAtUtc).HasColumnName("expires_at_utc");

        builder.HasIndex(token => token.TokenHash).IsUnique().HasDatabaseName("ix_user_tokens_token_hash");
        // Un nouveau token remplace le précédent du même utilisateur et du même usage.
        builder.HasIndex(token => new { token.UserId, token.Purpose }).IsUnique().HasDatabaseName(UserPurposeIndex);

        builder.HasOne<User>().WithMany().HasForeignKey(token => token.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
