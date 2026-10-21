using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

internal sealed class PasswordAttemptConfiguration : IEntityTypeConfiguration<PasswordAttempt>
{
    public void Configure(EntityTypeBuilder<PasswordAttempt> builder)
    {
        builder.ToTable("password_attempts");

        builder.HasKey(attempt => attempt.Email);
        builder.Property(attempt => attempt.Email).HasColumnName("email").HasMaxLength(Email.MaxLength);
        builder.Property(attempt => attempt.Failures).HasColumnName("failures");
        builder.Property(attempt => attempt.WindowStartedAtUtc).HasColumnName("window_started_at_utc");

        // Les fenêtres expirées sont purgées à chaque échec enregistré.
        builder.HasIndex(attempt => attempt.WindowStartedAtUtc).HasDatabaseName("ix_password_attempts_window");
    }
}
