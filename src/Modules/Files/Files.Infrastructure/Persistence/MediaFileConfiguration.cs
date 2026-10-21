using System.Globalization;
using Files.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Files.Infrastructure.Persistence;

/// <summary>
/// Agrégat <see cref="MediaFile"/> : table <c>media_files</c>. Toute valeur relue repasse par la fabrique du domaine ;
/// la clé de stockage est unique (deux fichiers ne partagent jamais un objet).
/// </summary>
internal sealed class MediaFileConfiguration : IEntityTypeConfiguration<MediaFile>
{
    public const string StorageKeyIndex = "ix_media_files_storage_key";

    private static readonly ValueConverter<FileId, Guid> FileKey = new(id => id.Value, value => FileId.From(value));
    private static readonly ValueConverter<TenantId, Guid> TenantKey = new(id => id.Value, value => TenantId.From(value));
    private static readonly ValueConverter<FileName, string> Name = new(name => name.Value, value => FileName.Create(value));
    private static readonly ValueConverter<ContentType, string> Type = new(type => type.Value, value => ContentType.Create(value));
    private static readonly ValueConverter<StorageKey, string> Key = new(key => key.Value, value => StorageKey.Create(value));
    private static readonly ValueConverter<FileOwner, string> Owner = new(owner => WriteOwner(owner), value => ReadOwner(value));

    public static string WriteOwner(FileOwner owner) => owner.OwnerType + "/" + owner.OwnerId.ToString("D", CultureInfo.InvariantCulture);

    public static FileOwner ReadOwner(string value)
    {
        var separator = value.LastIndexOf('/');
        return FileOwner.Create(value[..separator], Guid.Parse(value[(separator + 1)..], CultureInfo.InvariantCulture));
    }

    public void Configure(EntityTypeBuilder<MediaFile> builder)
    {
        var deleted = ((int)MediaFileStatus.Deleted).ToString(CultureInfo.InvariantCulture);
        var quarantined = ((int)MediaFileStatus.Quarantined).ToString(CultureInfo.InvariantCulture);
        builder.ToTable("media_files", table =>
        {
            table.HasCheckConstraint(
                "ck_media_files_status",
                "status IN (" + string.Join(", ", Enum.GetValues<MediaFileStatus>().Select(value => ((int)value).ToString(CultureInfo.InvariantCulture))) + ")");
            table.HasCheckConstraint("ck_media_files_size", "size_in_bytes > 0 AND size_in_bytes <= max_size_in_bytes");
            table.HasCheckConstraint("ck_media_files_quarantine_reason", $"(status = {quarantined}) = (quarantine_reason IS NOT NULL) OR status = {deleted}");
            table.HasCheckConstraint("ck_media_files_deleted_at", $"(status = {deleted}) = (deleted_at_utc IS NOT NULL)");
        });

        builder.HasKey(file => file.Id);
        builder.Property(file => file.Id).HasColumnName("id").HasConversion(FileKey).ValueGeneratedNever();
        builder.Property(file => file.TenantId).HasColumnName("tenant_id").HasConversion(TenantKey);
        // Le propriétaire en une colonne « type/uuid » : comparée telle quelle (filtre par propriétaire, index).
        builder.Property(file => file.Owner).HasColumnName("owner").HasConversion(Owner).HasMaxLength(FileOwner.MaxTypeLength + 37);
        builder.Property(file => file.FileName).HasColumnName("file_name").HasConversion(Name).HasMaxLength(FileName.MaxLength);
        builder.Property(file => file.ContentType).HasColumnName("content_type").HasConversion(Type).HasMaxLength(ContentType.MaxLength);
        builder.Property(file => file.SizeInBytes).HasColumnName("size_in_bytes");
        builder.Property(file => file.MaxSizeInBytes).HasColumnName("max_size_in_bytes");
        builder.Property(file => file.StorageKey).HasColumnName("storage_key").HasConversion(Key).HasMaxLength(StorageKey.MaxLength);
        builder.Property(file => file.Status).HasColumnName("status");
        builder.Property(file => file.QuarantineReason).HasColumnName("quarantine_reason").HasMaxLength(MediaFile.MaxQuarantineReasonLength);
        builder.Property(file => file.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(file => file.AvailableAtUtc).HasColumnName("available_at_utc");
        builder.Property(file => file.QuarantinedAtUtc).HasColumnName("quarantined_at_utc");
        builder.Property(file => file.DeletedAtUtc).HasColumnName("deleted_at_utc");
        builder.Property(file => file.ThumbnailOf).HasColumnName("thumbnail_of").HasConversion(FileKey);
        builder.Ignore(file => file.IsThumbnail);
        builder.Ignore(file => file.CanBeServed);

        builder.HasIndex(file => file.StorageKey).IsUnique().HasDatabaseName(StorageKeyIndex);
        // Les fichiers d'un propriétaire ; et le job des uploads abandonnés (par statut et date).
        builder.HasIndex(file => new { file.TenantId, file.Owner }).HasDatabaseName("ix_media_files_tenant_id_owner");
        builder.HasIndex(file => new { file.Status, file.CreatedAtUtc }).HasDatabaseName("ix_media_files_status_created_at_utc");

        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(file => file.DomainEvents);
    }
}
