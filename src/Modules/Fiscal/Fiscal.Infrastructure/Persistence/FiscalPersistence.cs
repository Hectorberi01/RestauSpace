using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fiscal.Application;
using Fiscal.Domain;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Fiscal.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Fiscal (schéma <c>fiscal</c>). Profils et factures appartiennent à un tenant et sont filtrés
/// sur le tenant courant ; la file de traitement lit sans filtre et le déclare. Le coffre des jetons n'est jamais chargé
/// avec un agrégat.
/// </summary>
public sealed class FiscalDbContext(DbContextOptions<FiscalDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    public DbSet<FiscalProfile> Profiles => Set<FiscalProfile>();

    public DbSet<FiscalInvoice> Invoices => Set<FiscalInvoice>();

    internal DbSet<FiscalToken> Tokens => Set<FiscalToken>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FiscalDbContext).Assembly);
        modelBuilder.ApplyOutbox();
        ApplyTenantQueryFilters(modelBuilder);
    }
}

internal sealed class FiscalDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<FiscalDbContext>
{
    /// <summary>Pour <c>dotnet ef</c> uniquement : pas de requête, donc pas de tenant.</summary>
    public FiscalDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("FISCAL_DB")
            ?? "Host=localhost;Port=5433;Database=saasrestaurant;Username=saasrestaurant;Password=saasrestaurant";
        var options = new DbContextOptionsBuilder<FiscalDbContext>();
        options.UseFiscalNpgsql(connectionString);
        return new FiscalDbContext(options.Options, new FixedTenantContext(null));
    }
}

/// <summary>Ligne du coffre : le jeton chiffré d'un restaurant. Table à part, jamais exposée hors de l'Infrastructure.</summary>
internal sealed class FiscalToken
{
    public Guid TenantId { get; set; }

    public string CipherText { get; set; } = null!;

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal static class FiscalConverters
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static readonly ValueConverter<TenantId, Guid> TenantKey = new(id => id.Value, value => TenantId.From(value));
    public static readonly ValueConverter<FiscalProfileId, Guid> ProfileKey = new(id => id.Value, value => FiscalProfileId.From(value));
    public static readonly ValueConverter<FiscalInvoiceId, Guid> InvoiceKey = new(id => id.Value, value => FiscalInvoiceId.From(value));
    public static readonly ValueConverter<FiscalInvoiceId?, Guid?> OptionalInvoiceKey = new(id => id == null ? null : id.Value.Value, value => value == null ? null : FiscalInvoiceId.From(value.Value));

    public static readonly ValueConverter<List<FiscalLine>, string> Lines = new(list => Write(list), json => Read<FiscalLine>(json));
    public static readonly ValueConverter<List<FiscalPayment>, string> Payments = new(list => Write(list), json => Read<FiscalPayment>(json));
    public static readonly ValueConverter<FiscalSecurity?, string?> Security = new(security => WriteSecurity(security), json => ReadSecurity(json));

    public static ValueComparer<List<T>> ListComparer<T>() => new(
        (left, right) => left!.SequenceEqual(right!), list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item!.GetHashCode())), list => list.ToList());

    /// <summary>« column IN (1, 2...) », généré depuis l'enum : ajouter une valeur change le modèle, donc la migration.</summary>
    public static string In<TEnum>(string column)
        where TEnum : struct, Enum =>
        column + " IN (" + string.Join(", ", Enum.GetValues<TEnum>().Select(value => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture))) + ")";

    internal static string Write<T>(List<T> list) => JsonSerializer.Serialize(list, Json);

    internal static List<T> Read<T>(string json) => JsonSerializer.Deserialize<List<T>>(json, Json) ?? [];

    internal static string? WriteSecurity(FiscalSecurity? security) => security is null ? null : JsonSerializer.Serialize(security, Json);

    internal static FiscalSecurity? ReadSecurity(string? json) => string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<FiscalSecurity>(json, Json);
}

internal sealed class FiscalProfileConfiguration : IEntityTypeConfiguration<FiscalProfile>
{
    public const string TenantIndex = "ux_fiscal_profiles_tenant_id";

    public void Configure(EntityTypeBuilder<FiscalProfile> builder)
    {
        builder.ToTable("fiscal_profiles", table =>
        {
            table.HasCheckConstraint("ck_fiscal_profiles_ifu", "ifu ~ '^[0-9]{13}$'");
            table.HasCheckConstraint("ck_fiscal_profiles_regime", FiscalConverters.In<TaxRegime>("regime"));
            table.HasCheckConstraint("ck_fiscal_profiles_aib", FiscalConverters.In<AibRate>("aib"));
            table.HasCheckConstraint("ck_fiscal_profiles_mode", FiscalConverters.In<FiscalMode>("mode"));
        });
        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.Id).HasColumnName("id").HasConversion(FiscalConverters.ProfileKey).ValueGeneratedNever();
        builder.Property(profile => profile.TenantId).HasColumnName("tenant_id").HasConversion(FiscalConverters.TenantKey);
        builder.HasIndex(profile => profile.TenantId).IsUnique().HasDatabaseName(TenantIndex);
        builder.Property(profile => profile.Ifu).HasColumnName("ifu").HasMaxLength(FiscalProfile.IfuLength).IsFixedLength();
        builder.Property(profile => profile.Rccm).HasColumnName("rccm").HasMaxLength(FiscalProfile.MaxRccmLength);
        builder.Property(profile => profile.Regime).HasColumnName("regime");
        builder.Property(profile => profile.Aib).HasColumnName("aib");
        builder.Property(profile => profile.Mode).HasColumnName("mode");
        builder.Property(profile => profile.HasToken).HasColumnName("has_token");
        builder.Property(profile => profile.TokenUpdatedAtUtc).HasColumnName("token_updated_at_utc");
        builder.Property(profile => profile.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(profile => profile.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(profile => profile.IsActive);
        builder.Ignore(profile => profile.DomainEvents);
    }
}

internal sealed class FiscalInvoiceConfiguration : IEntityTypeConfiguration<FiscalInvoice>
{
    public const string SourceIndex = "ux_fiscal_invoices_tenant_id_kind_source_id";

    public void Configure(EntityTypeBuilder<FiscalInvoice> builder)
    {
        builder.ToTable("fiscal_invoices", table =>
        {
            table.HasCheckConstraint("ck_fiscal_invoices_kind", FiscalConverters.In<FiscalInvoiceKind>("kind"));
            table.HasCheckConstraint("ck_fiscal_invoices_status", FiscalConverters.In<FiscalInvoiceStatus>("status"));
            table.HasCheckConstraint("ck_fiscal_invoices_mode", FiscalConverters.In<FiscalMode>("mode"));
            table.HasCheckConstraint("ck_fiscal_invoices_total_positive", "total > 0");
        });
        builder.HasKey(invoice => invoice.Id);
        builder.Property(invoice => invoice.Id).HasColumnName("id").HasConversion(FiscalConverters.InvoiceKey).ValueGeneratedNever();
        builder.Property(invoice => invoice.TenantId).HasColumnName("tenant_id").HasConversion(FiscalConverters.TenantKey);
        // Références par id vers Orders, Payments et Locations, sans clé étrangère (autres modules).
        builder.Property(invoice => invoice.OrderId).HasColumnName("order_id");
        builder.Property(invoice => invoice.LocationId).HasColumnName("location_id");
        builder.Property(invoice => invoice.SourceId).HasColumnName("source_id");
        builder.Property(invoice => invoice.Kind).HasColumnName("kind");
        builder.HasIndex(invoice => new { invoice.TenantId, invoice.Kind, invoice.SourceId }).IsUnique().HasDatabaseName(SourceIndex);
        builder.HasIndex(invoice => new { invoice.TenantId, invoice.CreatedAtUtc }).HasDatabaseName("ix_fiscal_invoices_tenant_id_created_at_utc");
        // La file : factures en attente, par échéance.
        builder.HasIndex(invoice => invoice.NextAttemptAtUtc).HasFilter("status IN (1, 2)").HasDatabaseName("ix_fiscal_invoices_due");
        builder.Property(invoice => invoice.OrderNumber).HasColumnName("order_number").HasMaxLength(40);
        builder.Property(invoice => invoice.Mode).HasColumnName("mode");
        builder.Property(invoice => invoice.Total).HasColumnName("total");
        builder.Property(invoice => invoice.OriginalInvoiceId).HasColumnName("original_invoice_id").HasConversion(FiscalConverters.OptionalInvoiceKey);
        builder.Property<List<FiscalLine>>("_lines").HasColumnName("lines").HasColumnType("jsonb")
            .HasConversion(FiscalConverters.Lines, FiscalConverters.ListComparer<FiscalLine>());
        builder.Property<List<FiscalPayment>>("_payments").HasColumnName("payments").HasColumnType("jsonb")
            .HasConversion(FiscalConverters.Payments, FiscalConverters.ListComparer<FiscalPayment>());
        builder.Property(invoice => invoice.Status).HasColumnName("status");
        builder.Property(invoice => invoice.Attempts).HasColumnName("attempts");
        builder.Property(invoice => invoice.NextAttemptAtUtc).HasColumnName("next_attempt_at_utc");
        builder.Property(invoice => invoice.Uid).HasColumnName("uid").HasMaxLength(64);
        builder.Property(invoice => invoice.SubmittedAtUtc).HasColumnName("submitted_at_utc");
        builder.Property(invoice => invoice.Security).HasColumnName("security").HasColumnType("jsonb").HasConversion(FiscalConverters.Security);
        builder.Property(invoice => invoice.ErrorCode).HasColumnName("error_code").HasMaxLength(40);
        builder.Property(invoice => invoice.ErrorMessage).HasColumnName("error_message").HasMaxLength(FiscalInvoice.MaxErrorLength);
        builder.Property(invoice => invoice.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(invoice => invoice.CertifiedAtUtc).HasColumnName("certified_at_utc");
        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(invoice => invoice.Lines);
        builder.Ignore(invoice => invoice.Payments);
        builder.Ignore(invoice => invoice.DomainEvents);
    }
}

internal sealed class FiscalTokenConfiguration : IEntityTypeConfiguration<FiscalToken>
{
    public void Configure(EntityTypeBuilder<FiscalToken> builder)
    {
        builder.ToTable("fiscal_tokens");
        builder.HasKey(token => token.TenantId);
        builder.Property(token => token.TenantId).HasColumnName("tenant_id").ValueGeneratedNever();
        builder.Property(token => token.CipherText).HasColumnName("cipher_text").HasMaxLength(8192);
        builder.Property(token => token.UpdatedAtUtc).HasColumnName("updated_at_utc");
    }
}

internal sealed class FiscalProfileRepository(FiscalDbContext context) : IFiscalProfileRepository
{
    public Task<FiscalProfile?> FindAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        context.Profiles.SingleOrDefaultAsync(profile => profile.TenantId == tenantId, cancellationToken);

    public void Add(FiscalProfile profile) => context.Profiles.Add(profile);
}

internal sealed class FiscalInvoiceRepository(FiscalDbContext context) : IFiscalInvoiceRepository
{
    public Task<FiscalInvoice?> FindAsync(TenantId tenantId, FiscalInvoiceId id, CancellationToken cancellationToken = default) =>
        context.Invoices.SingleOrDefaultAsync(invoice => invoice.TenantId == tenantId && invoice.Id == id, cancellationToken);

    public Task<FiscalInvoice?> FindBySourceAsync(TenantId tenantId, FiscalInvoiceKind kind, Guid sourceId, CancellationToken cancellationToken = default) =>
        context.Invoices.SingleOrDefaultAsync(invoice => invoice.TenantId == tenantId && invoice.Kind == kind && invoice.SourceId == sourceId, cancellationToken);

    public async Task<IReadOnlyList<FiscalInvoice>> ListAsync(
        TenantId tenantId, FiscalInvoiceStatus? status, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int take, CancellationToken cancellationToken = default)
    {
        var query = context.Invoices.AsNoTracking().Where(invoice => invoice.TenantId == tenantId);
        if (status is { } wanted)
        {
            query = query.Where(invoice => invoice.Status == wanted);
        }

        if (fromUtc is { } from)
        {
            query = query.Where(invoice => invoice.CreatedAtUtc >= from);
        }

        if (toUtc is { } to)
        {
            query = query.Where(invoice => invoice.CreatedAtUtc < to);
        }

        return await query.OrderByDescending(invoice => invoice.CreatedAtUtc).ThenByDescending(invoice => invoice.Id).Take(take).ToListAsync(cancellationToken);
    }

    public void Add(FiscalInvoice invoice) => context.Invoices.Add(invoice);
}

/// <summary>Tous restaurants (file de traitement), lecture seule : seulement l'id et le tenant de chaque facture due.</summary>
internal sealed class DueFiscalInvoices(FiscalDbContext context) : IDueFiscalInvoices
{
    public async Task<IReadOnlyList<DueFiscalInvoice>> ListAsync(DateTimeOffset nowUtc, int take, CancellationToken cancellationToken = default) =>
        (await context.Invoices.IgnoreQueryFilters().AsNoTracking()
            .Where(invoice => (invoice.Status == FiscalInvoiceStatus.Pending || invoice.Status == FiscalInvoiceStatus.Submitted) && invoice.NextAttemptAtUtc <= nowUtc)
            .OrderBy(invoice => invoice.NextAttemptAtUtc)
            .Take(take)
            .Select(invoice => new { invoice.TenantId, invoice.Id })
            .ToListAsync(cancellationToken))
        .Select(item => new DueFiscalInvoice(item.TenantId, item.Id))
        .ToList();
}

/// <summary>
/// Coffre des jetons : AES-256-GCM, « v1: » + base64(nonce | chiffré | tag), le tenant servant de données authentifiées
/// (un chiffré recopié sur une autre ligne ne se déchiffre pas). Écriture immédiate, hors du suivi des modifications.
/// </summary>
internal sealed class FiscalTokenStore(FiscalDbContext context, FiscalOptions options, IClock clock) : IFiscalTokenStore
{
    private const string Prefix = "v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public async Task StoreAsync(TenantId tenantId, string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        var cipherText = Encrypt(token, Purpose(tenantId));
        var now = clock.UtcNow;
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO fiscal.fiscal_tokens (tenant_id, cipher_text, updated_at_utc)
             VALUES ({tenantId.Value}, {cipherText}, {now})
             ON CONFLICT (tenant_id) DO UPDATE SET cipher_text = EXCLUDED.cipher_text, updated_at_utc = EXCLUDED.updated_at_utc
             """,
            cancellationToken);
    }

    public async Task<string?> ReadAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        var cipherText = await context.Tokens.AsNoTracking()
            .Where(token => token.TenantId == tenantId.Value)
            .Select(token => token.CipherText)
            .SingleOrDefaultAsync(cancellationToken);
        return cipherText is null ? null : Decrypt(cipherText, Purpose(tenantId));
    }

    private static string Purpose(TenantId tenantId) => "fiscal:" + tenantId.Value.ToString("N");

    private byte[] Key => options.TokenKey is { Length: 32 } key ? key : throw new InvalidOperationException("Fiscal token key must be exactly 32 bytes (AES-256).");

    private string Encrypt(string token, string purpose)
    {
        var plain = Encoding.UTF8.GetBytes(token);
        try
        {
            var output = new byte[NonceSize + plain.Length + TagSize];
            RandomNumberGenerator.Fill(output.AsSpan(0, NonceSize));
            using var aes = new AesGcm(Key, TagSize);
            aes.Encrypt(output.AsSpan(0, NonceSize), plain, output.AsSpan(NonceSize, plain.Length), output.AsSpan(NonceSize + plain.Length), Encoding.UTF8.GetBytes(purpose));
            return Prefix + Convert.ToBase64String(output);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <exception cref="InvalidOperationException">Chiffré altéré, recopié d'un autre restaurant, ou clé changée.</exception>
    private string Decrypt(string cipherText, string purpose)
    {
        try
        {
            if (!cipherText.StartsWith(Prefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unknown token format.");
            }

            var data = Convert.FromBase64String(cipherText[Prefix.Length..]);
            var length = data.Length - NonceSize - TagSize;
            if (length <= 0)
            {
                throw new InvalidOperationException("Token cipher text is too short.");
            }

            var plain = new byte[length];
            try
            {
                using var aes = new AesGcm(Key, TagSize);
                aes.Decrypt(data.AsSpan(0, NonceSize), data.AsSpan(NonceSize, length), data.AsSpan(NonceSize + length), plain, Encoding.UTF8.GetBytes(purpose));
                return Encoding.UTF8.GetString(plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            throw new InvalidOperationException("The e-MECeF token cannot be decrypted (altered, copied from another restaurant, or key changed).", exception);
        }
    }
}

internal sealed class FiscalUnitOfWork(FiscalDbContext context) : OutboxUnitOfWork<FiscalDbContext>(context), IFiscalUnitOfWork
{
    protected override DomainError ConcurrentUpdate => FiscalApplicationErrors.ConcurrentUpdate;

    /// <summary>Deux profils, ou deux factures pour la même source, créés en même temps.</summary>
    protected override Exception? Translate(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: FiscalProfileConfiguration.TenantIndex or FiscalInvoiceConfiguration.SourceIndex,
        }
            ? new ConcurrencyException(FiscalApplicationErrors.ConcurrentUpdate)
            : null;
}
