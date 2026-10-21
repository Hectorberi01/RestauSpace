namespace Notifications.Domain;

/// <summary>
/// Port qui répond à ce qu'un modèle seul ne peut savoir : si (tenant, clé, canal, langue[, version]) est libre.
/// Un tenant null désigne les modèles de niveau plateforme. L'Application l'appelle et transmet la réponse à la
/// factory ; l'index unique est le garde-fou final.
/// </summary>
public interface ITemplateUniquenessChecker
{
    Task<bool> IsKeyUniqueAsync(
        TenantId? tenantId,
        TemplateKey key,
        NotificationChannel channel,
        NotificationLanguage language,
        CancellationToken cancellationToken = default);

    Task<bool> IsVersionUniqueAsync(
        TenantId? tenantId,
        TemplateKey key,
        NotificationChannel channel,
        NotificationLanguage language,
        int version,
        CancellationToken cancellationToken = default);
}
