using Backups.Domain;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Backups.Infrastructure.Persistence;

/// <summary>
/// Schéma <c>backups</c> : la file lue par l'agent du serveur de base (contrat partagé : noms, colonnes et statuts en
/// texte ne changent pas). Données de la plateforme, sans tenant. Index uniques partiels et CHECK de la ligne de l'agent :
/// posés en SQL par la migration.
/// </summary>
public sealed class BackupsDbContext(DbContextOptions<BackupsDbContext> options) : DbContext(options)
{
    public DbSet<Backup> Backups => Set<Backup>();

    public DbSet<Restore> Restores => Set<Restore>();

    internal DbSet<BackupAgentRow> Agent => Set<BackupAgentRow>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BackupsDbContext).Assembly);
        modelBuilder.ApplyOutbox();
    }
}

/// <summary>Pour <c>dotnet ef</c> uniquement.</summary>
internal sealed class BackupsDbContextFactory : IDesignTimeDbContextFactory<BackupsDbContext>
{
    public BackupsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("BACKUPS_DB")
            ?? "Host=localhost;Port=5433;Database=saasrestaurant;Username=saasrestaurant;Password=saasrestaurant";
        var options = new DbContextOptionsBuilder<BackupsDbContext>();
        options.UseBackupsNpgsql(connectionString);
        return new BackupsDbContext(options.Options);
    }
}

/// <summary>La ligne unique (id = 1) écrite par l'agent ; lue seulement par l'API.</summary>
internal sealed class BackupAgentRow
{
    public short Id { get; set; }

    public string Host { get; set; } = null!;

    public string Version { get; set; } = null!;

    public DateTimeOffset LastSeenAtUtc { get; set; }

    public bool RestoreEnabled { get; set; }
}

internal static class BackupsConverters
{
    public static readonly ValueConverter<BackupId, Guid> BackupKey = new(id => id.Value, value => BackupId.From(value));
    public static readonly ValueConverter<RestoreId, Guid> RestoreKey = new(id => id.Value, value => RestoreId.From(value));
    public static readonly ValueConverter<UserId, Guid> User = new(id => id.Value, value => UserId.From(value));
    public static readonly ValueConverter<UserId?, Guid?> OptionalUser =
        new(id => id == null ? null : id.Value.Value, value => value == null ? null : UserId.From(value.Value));
}

internal sealed class BackupConfiguration : IEntityTypeConfiguration<Backup>
{
    /// <summary>Une seule sauvegarde active (migration, en SQL).</summary>
    public const string ActiveIndex = "ux_backups_active";

    public void Configure(EntityTypeBuilder<Backup> builder)
    {
        builder.ToTable("backups");
        builder.HasKey(backup => backup.Id);
        builder.Property(backup => backup.Id).HasColumnName("id").HasConversion(BackupsConverters.BackupKey).ValueGeneratedNever();
        builder.Property(backup => backup.DatabaseName).HasColumnName("database_name");
        builder.Property(backup => backup.Trigger).HasColumnName("trigger").HasConversion<string>();
        builder.Property(backup => backup.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(backup => backup.StorageKey).HasColumnName("storage_key");
        builder.Property(backup => backup.SizeBytes).HasColumnName("size_bytes");
        builder.Property(backup => backup.ChecksumSha256).HasColumnName("checksum_sha256");
        builder.Property(backup => backup.RequestedBy).HasColumnName("requested_by").HasConversion(BackupsConverters.OptionalUser);
        builder.Property(backup => backup.FailureCode).HasColumnName("failure_code");
        builder.Property(backup => backup.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(backup => backup.StartedAtUtc).HasColumnName("started_at_utc");
        builder.Property(backup => backup.HeartbeatAtUtc).HasColumnName("heartbeat_at_utc");
        builder.Property(backup => backup.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(backup => backup.ExpiresAtUtc).HasColumnName("expires_at_utc");
        // Concurrence optimiste sur la colonne système xmin : l'agent écrit les mêmes lignes.
        builder.Property<uint>("Version").IsRowVersion();
        builder.HasIndex(backup => backup.Status).HasDatabaseName("ix_backups_status");
        builder.HasIndex(backup => backup.CreatedAtUtc).IsDescending().HasDatabaseName("ix_backups_created_at_utc");
        builder.Ignore(backup => backup.IsActive);
        builder.Ignore(backup => backup.DomainEvents);
    }
}

internal sealed class RestoreConfiguration : IEntityTypeConfiguration<Restore>
{
    /// <summary>Une seule restauration active (migration, en SQL).</summary>
    public const string ActiveIndex = "ux_restores_active";

    public void Configure(EntityTypeBuilder<Restore> builder)
    {
        builder.ToTable("restores");
        builder.HasKey(restore => restore.Id);
        builder.Property(restore => restore.Id).HasColumnName("id").HasConversion(BackupsConverters.RestoreKey).ValueGeneratedNever();
        builder.Property(restore => restore.BackupId).HasColumnName("backup_id").HasConversion(BackupsConverters.BackupKey);
        builder.HasOne<Backup>().WithMany().HasForeignKey(restore => restore.BackupId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_restores_backup");
        builder.HasIndex(restore => restore.BackupId).HasDatabaseName("ix_restores_backup_id");
        builder.Property(restore => restore.DatabaseName).HasColumnName("database_name");
        builder.Property(restore => restore.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(restore => restore.RequestedBy).HasColumnName("requested_by").HasConversion(BackupsConverters.User);
        builder.Property(restore => restore.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(restore => restore.NotBeforeUtc).HasColumnName("not_before_utc");
        builder.Property(restore => restore.StartedAtUtc).HasColumnName("started_at_utc");
        builder.Property(restore => restore.HeartbeatAtUtc).HasColumnName("heartbeat_at_utc");
        builder.Property(restore => restore.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(restore => restore.PreviousDatabaseName).HasColumnName("previous_database_name");
        builder.Property(restore => restore.FailureCode).HasColumnName("failure_code");
        builder.Property<uint>("Version").IsRowVersion();
        builder.HasIndex(restore => restore.CreatedAtUtc).IsDescending().HasDatabaseName("ix_restores_created_at_utc");
        builder.Ignore(restore => restore.IsActive);
        builder.Ignore(restore => restore.DomainEvents);
    }
}

internal sealed class BackupAgentConfiguration : IEntityTypeConfiguration<BackupAgentRow>
{
    public void Configure(EntityTypeBuilder<BackupAgentRow> builder)
    {
        builder.ToTable("agent");
        builder.HasKey(agent => agent.Id);
        builder.Property(agent => agent.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(agent => agent.Host).HasColumnName("host");
        builder.Property(agent => agent.Version).HasColumnName("version");
        builder.Property(agent => agent.LastSeenAtUtc).HasColumnName("last_seen_at_utc");
        builder.Property(agent => agent.RestoreEnabled).HasColumnName("restore_enabled");
    }
}
