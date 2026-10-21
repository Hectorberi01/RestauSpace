using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Printing.Domain;

namespace Printing.Infrastructure.Persistence;

/// <summary>Conversions des ids et objets-valeurs : toute valeur relue repasse par la fabrique du domaine.</summary>
internal static class PrintingConverters
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static readonly ValueConverter<TenantId, Guid> TenantKey = new(id => id.Value, value => TenantId.From(value));
    public static readonly ValueConverter<LocationId, Guid> LocationKey = new(id => id.Value, value => LocationId.From(value));
    public static readonly ValueConverter<PrinterId, Guid> PrinterKey = new(id => id.Value, value => PrinterId.From(value));
    public static readonly ValueConverter<PrintJobId, Guid> JobKey = new(id => id.Value, value => PrintJobId.From(value));
    public static readonly ValueConverter<TokenHash, string> Token = new(hash => hash.Value, value => TokenHash.Create(value));

    public static readonly ValueConverter<TicketDocument, string> Document = new(
        document => JsonSerializer.Serialize(document.Lines, Json),
        json => TicketDocument.Create(JsonSerializer.Deserialize<List<TicketLine>>(json, Json) ?? new List<TicketLine>()));

    public static readonly ValueComparer<TicketDocument> DocumentComparer = new(
        (left, right) => ReferenceEquals(left, right), document => document.GetHashCode(), document => document);

    /// <summary>« column IN (1, 2...) », généré depuis l'enum : ajouter une valeur change le modèle, donc la migration.</summary>
    public static string In<TEnum>(string column)
        where TEnum : struct, Enum =>
        column + " IN (" + string.Join(", ", Enum.GetValues<TEnum>().Select(value => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture))) + ")";
}

/// <summary>Agrégat <see cref="Printer"/> : <c>printers</c>. Seule l'empreinte du jeton est stockée ; elle est unique.</summary>
internal sealed class PrinterConfiguration : IEntityTypeConfiguration<Printer>
{
    public const string TokenIndex = "ux_printers_token_hash";

    public void Configure(EntityTypeBuilder<Printer> builder)
    {
        builder.ToTable("printers", table =>
        {
            table.HasCheckConstraint("ck_printers_protocol", PrintingConverters.In<PrinterProtocol>("protocol"));
            table.HasCheckConstraint("ck_printers_status", PrintingConverters.In<PrinterStatus>("status"));
        });

        builder.HasKey(printer => printer.Id);
        builder.Property(printer => printer.Id).HasColumnName("id").HasConversion(PrintingConverters.PrinterKey).ValueGeneratedNever();
        builder.Property(printer => printer.TenantId).HasColumnName("tenant_id").HasConversion(PrintingConverters.TenantKey);
        builder.Property(printer => printer.LocationId).HasColumnName("location_id").HasConversion(PrintingConverters.LocationKey);
        builder.Property(printer => printer.Name).HasColumnName("name").HasMaxLength(Printer.MaxNameLength);
        builder.Property(printer => printer.Protocol).HasColumnName("protocol");
        builder.Property(printer => printer.TokenHash).HasColumnName("token_hash").HasConversion(PrintingConverters.Token).HasMaxLength(TokenHash.Length).IsFixedLength();
        builder.Property(printer => printer.TokenHint).HasColumnName("token_hint").HasMaxLength(Printer.TokenHintLength).IsFixedLength();
        builder.Property(printer => printer.PrintsCustomerTickets).HasColumnName("prints_customer_tickets");
        builder.Property(printer => printer.PrintsKitchenTickets).HasColumnName("prints_kitchen_tickets").HasDefaultValue(false);
        // Référence par id vers un poste du module Kitchen, sans clé étrangère (autre module).
        builder.Property(printer => printer.KitchenStationId).HasColumnName("kitchen_station_id");
        builder.Property(printer => printer.Status).HasColumnName("status");
        builder.Property(printer => printer.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(printer => printer.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.Property(printer => printer.LastSeenAtUtc).HasColumnName("last_seen_at_utc");
        builder.Ignore(printer => printer.IsActive);

        builder.HasIndex(printer => printer.TokenHash).IsUnique().HasDatabaseName(TokenIndex);
        builder.HasIndex(printer => new { printer.TenantId, printer.LocationId }).HasDatabaseName("ix_printers_tenant_id_location_id");

        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(printer => printer.DomainEvents);
    }
}

/// <summary>
/// Agrégat <see cref="PrintJob"/> : <c>print_jobs</c>. Le ticket (lignes de texte) en jsonb ; une seule fois par
/// (imprimante, type, référence) ; l'index partiel des tickets ouverts sert la file de chaque imprimante.
/// </summary>
internal sealed class PrintJobConfiguration : IEntityTypeConfiguration<PrintJob>
{
    public const string ReferenceIndex = "ux_print_jobs_printer_id_kind_reference";

    public void Configure(EntityTypeBuilder<PrintJob> builder)
    {
        builder.ToTable("print_jobs", table =>
        {
            table.HasCheckConstraint("ck_print_jobs_kind", PrintingConverters.In<PrintJobKind>("kind"));
            table.HasCheckConstraint("ck_print_jobs_status", PrintingConverters.In<PrintJobStatus>("status"));
            table.HasCheckConstraint("ck_print_jobs_attempts", "attempts >= 0");
        });

        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id).HasColumnName("id").HasConversion(PrintingConverters.JobKey).ValueGeneratedNever();
        builder.Property(job => job.TenantId).HasColumnName("tenant_id").HasConversion(PrintingConverters.TenantKey);
        builder.Property(job => job.PrinterId).HasColumnName("printer_id").HasConversion(PrintingConverters.PrinterKey);
        builder.Property(job => job.LocationId).HasColumnName("location_id").HasConversion(PrintingConverters.LocationKey);
        builder.Property(job => job.Kind).HasColumnName("kind");
        builder.Property(job => job.Reference).HasColumnName("reference").HasMaxLength(PrintJob.MaxReferenceLength);
        builder.Property(job => job.Document).HasColumnName("document").HasColumnType("jsonb")
            .HasConversion(PrintingConverters.Document, PrintingConverters.DocumentComparer);
        builder.Property(job => job.Status).HasColumnName("status");
        builder.Property(job => job.Attempts).HasColumnName("attempts");
        builder.Property(job => job.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(job => job.SentAtUtc).HasColumnName("sent_at_utc");
        builder.Property(job => job.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(job => job.FailureCode).HasColumnName("failure_code").HasMaxLength(60);

        builder.HasIndex(job => new { job.PrinterId, job.Kind, job.Reference }).IsUnique().HasDatabaseName(ReferenceIndex);
        builder.HasIndex(job => new { job.PrinterId, job.CreatedAtUtc })
            .HasFilter("status IN (1, 2)")
            .HasDatabaseName("ix_print_jobs_open_printer_id_created_at_utc");
        builder.HasIndex(job => new { job.TenantId, job.LocationId, job.CreatedAtUtc }).HasDatabaseName("ix_print_jobs_tenant_id_location_id_created_at_utc");

        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(job => job.DomainEvents);
    }
}
