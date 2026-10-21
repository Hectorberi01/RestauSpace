using System.Text.Json;
using Audit.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Audit.Infrastructure.Persistence;

/// <summary>
/// <c>audit_entries</c>, en ajout seul. Toute valeur relue repasse par la fabrique du domaine ; l'écriture est
/// reconstruite par son constructeur complet. Colonnes de recherche CALCULÉES par la base (type et id de la cible,
/// module de l'action) : jamais écrites par l'application. Une même origine (corrélation, action, cible) n'est
/// journalisée qu'une fois : index unique (rejeu d'un événement).
/// </summary>
internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public const string Table = "audit_entries";
    public const string OriginIndex = "ix_audit_entries_origin";
    public const string ModuleProperty = "Module";
    public const string TargetTypeProperty = "SearchTargetType";
    public const string TargetIdProperty = "SearchTargetId";

    private static readonly ValueConverter<AuditId, Guid> Key = new(id => id.Value, value => AuditId.From(value));
    private static readonly ValueConverter<TenantId, Guid> Tenant = new(id => id.Value, value => TenantId.From(value));
    // Propriété nullable : le convertisseur est typé ActorId? (EF ne l'appelle jamais pour une valeur nulle).
    private static readonly ValueConverter<ActorId?, string> Actor = new(actor => WriteActor(actor!), value => ReadActor(value));
    private static readonly ValueConverter<AuditAction, string> ActionCode = new(action => action.Value, value => AuditAction.Create(value));
    private static readonly ValueConverter<AuditTarget, string> TargetRef = new(target => WriteTarget(target), value => ReadTarget(value));
    private static readonly ValueConverter<CorrelationId, string> Correlation = new(id => id.Value, value => CorrelationId.Create(value));
    private static readonly ValueConverter<AuditMetadata, string> MetadataJson = new(metadata => WriteMetadata(metadata), json => ReadMetadata(json));

    /// <summary>« User:… », « ApiCredential:… », « System:… » : le type puis l'identifiant (sans « : » dans le type).</summary>
    public static string WriteActor(ActorId actor) => actor.Type + ":" + actor.Value;

    public static ActorId ReadActor(string value)
    {
        var separator = value.IndexOf(':', StringComparison.Ordinal);
        return ActorId.Create(Enum.Parse<ActorType>(value[..separator]), value[(separator + 1)..]);
    }

    /// <summary>« type/id » : le type de cible ne contient jamais « / », l'id peut en contenir.</summary>
    public static string WriteTarget(AuditTarget target) => target.Type + "/" + target.Id;

    public static AuditTarget ReadTarget(string value)
    {
        var separator = value.IndexOf('/', StringComparison.Ordinal);
        return AuditTarget.Create(value[..separator], value[(separator + 1)..]);
    }

    public static string WriteMetadata(AuditMetadata metadata) =>
        JsonSerializer.Serialize(metadata.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

    // Relu par la fabrique : les valeurs déjà expurgées le restent, rien de plus ne peut apparaître.
    public static AuditMetadata ReadMetadata(string json) =>
        AuditMetadata.Create(JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>());

    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable(Table, table =>
            table.HasCheckConstraint("ck_audit_entries_occurred_before_recorded", "occurred_at_utc <= recorded_at_utc"));

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).HasColumnName("id").HasConversion(Key).ValueGeneratedNever();
        builder.Property(entry => entry.TenantId).HasColumnName("tenant_id").HasConversion(Tenant);
        builder.Property(entry => entry.ActorId).HasColumnName("actor").HasConversion(Actor).HasMaxLength(ActorId.MaxValueLength + 20);
        builder.Property(entry => entry.Action).HasColumnName("action").HasConversion(ActionCode).HasMaxLength(AuditAction.MaxLength);
        builder.Property(entry => entry.Target)
            .HasColumnName("target")
            .HasConversion(TargetRef)
            .HasMaxLength(AuditTarget.MaxTypeLength + 1 + AuditTarget.MaxIdLength);
        builder.Property(entry => entry.CorrelationId).HasColumnName("correlation_id").HasConversion(Correlation).HasMaxLength(CorrelationId.MaxLength);
        builder.Property(entry => entry.Metadata).HasColumnName("metadata").HasColumnType("jsonb").HasConversion(MetadataJson);
        builder.Property(entry => entry.OccurredAtUtc).HasColumnName("occurred_at_utc");
        builder.Property(entry => entry.RecordedAtUtc).HasColumnName("recorded_at_utc");
        builder.Ignore(entry => entry.TargetType);
        builder.Ignore(entry => entry.TargetId);

        // Colonnes de recherche calculées et stockées par PostgreSQL.
        builder.Property<string>(ModuleProperty)
            .HasColumnName("module")
            .HasMaxLength(AuditAction.MaxLength)
            .HasComputedColumnSql("split_part(action, '.', 1)", stored: true);
        builder.Property<string>(TargetTypeProperty)
            .HasColumnName("target_type")
            .HasMaxLength(AuditTarget.MaxTypeLength)
            .HasComputedColumnSql("split_part(target, '/', 1)", stored: true);
        builder.Property<string>(TargetIdProperty)
            .HasColumnName("target_id")
            .HasMaxLength(AuditTarget.MaxIdLength)
            .HasComputedColumnSql("substr(target, strpos(target, '/') + 1)", stored: true);

        builder.HasIndex(entry => new { entry.CorrelationId, entry.Action, entry.Target }).IsUnique().HasDatabaseName(OriginIndex);
        // Journal d'un restaurant (le plus fréquent), d'une cible, d'un acteur ; parcours par date (plateforme, purge).
        builder.HasIndex(entry => new { entry.TenantId, entry.OccurredAtUtc }).HasDatabaseName("ix_audit_entries_tenant_id_occurred_at_utc");
        builder.HasIndex(nameof(AuditEntry.TenantId), TargetTypeProperty, TargetIdProperty).HasDatabaseName("ix_audit_entries_tenant_id_target");
        builder.HasIndex(nameof(AuditEntry.TenantId), nameof(AuditEntry.ActorId)).HasDatabaseName("ix_audit_entries_tenant_id_actor");
        builder.HasIndex(entry => entry.OccurredAtUtc).HasDatabaseName("ix_audit_entries_occurred_at_utc");
    }
}
