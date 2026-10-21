using Payments.Application.Abstractions;
using Payments.Domain;
using Payments.Infrastructure.Persistence;

namespace Payments.Infrastructure.Secrets;

/// <summary>
/// Ligne du coffre : identifiants chiffrés rangés à une référence. Table à part (<c>payment_secrets</c>), jamais
/// chargée avec un agrégat ni exposée hors de l'Infrastructure.
/// </summary>
internal sealed class PaymentSecret
{
    public const int MaxCipherTextLength = 8192;

    public string Reference { get; set; } = null!;

    /// <summary>Le restaurant propriétaire (tiré de la référence) ; null pour un compte de la plateforme.</summary>
    public Guid? TenantId { get; set; }

    public string CipherText { get; set; } = null!;

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>
/// Coffre par défaut : PostgreSQL, valeurs chiffrées en AES-256-GCM avec la clé de l'hôte, la référence servant de
/// données authentifiées. L'écriture est IMMÉDIATE (hors du suivi des modifications) et ne déclenche donc pas la
/// sauvegarde des agrégats en cours : même comportement qu'un Vault. Si la sauvegarde de la configuration échoue ensuite,
/// le secret rangé reste sans effet et sera remplacé à la prochaine saisie.
/// </summary>
internal sealed class PostgresPaymentSecretStore(PaymentsDbContext context, SecretCipher cipher, IClock clock) : IPaymentSecretStore
{
    public async Task StoreAsync(
        PaymentCredentialReference reference, IReadOnlyDictionary<string, string> credentials, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(credentials);
        var cipherText = cipher.Encrypt(credentials, reference.Value);
        var now = clock.UtcNow;
        Guid? tenantId = reference.TenantId?.Value;

        // Requête paramétrée : ni la référence ni le chiffré ne sont concaténés au SQL.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO payments.payment_secrets (reference, tenant_id, cipher_text, updated_at_utc)
             VALUES ({reference.Value}, {tenantId}, {cipherText}, {now})
             ON CONFLICT (reference) DO UPDATE SET cipher_text = EXCLUDED.cipher_text, updated_at_utc = EXCLUDED.updated_at_utc
             """,
            cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, string>?> ReadAsync(
        PaymentCredentialReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var cipherText = await context.Secrets.AsNoTracking()
            .Where(secret => secret.Reference == reference.Value)
            .Select(secret => secret.CipherText)
            .SingleOrDefaultAsync(cancellationToken);
        return cipherText is null ? null : cipher.Decrypt(cipherText, reference.Value);
    }
}
