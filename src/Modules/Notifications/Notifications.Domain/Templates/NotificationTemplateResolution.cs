namespace Notifications.Domain;

/// <summary>D'où vient le modèle retenu pour un envoi.</summary>
public enum TemplateSource
{
    /// <summary>Aucun modèle publié et actif : rien à envoyer pour ce type, ce canal et cette langue.</summary>
    None = 0,

    Tenant = 1,

    Platform = 2,
}

/// <summary>Le modèle retenu (null si <see cref="Source"/> vaut <see cref="TemplateSource.None"/>).</summary>
public sealed record TemplateResolution(NotificationTemplate? Template, TemplateSource Source)
{
    public static TemplateResolution NotFound { get; } = new(null, TemplateSource.None);

    public bool IsFound => Template is not null;
}

/// <summary>
/// Règle de choix du modèle d'un envoi (tenant ou plateforme, type, canal, langue), appliquée par
/// <c>INotificationTemplateResolver</c> (Application) aux versions qu'il a chargées :
/// 1. la plus HAUTE version PUBLIÉE du modèle du restaurant ; si elle est active, c'est elle ; désactivée, le restaurant a
///    renoncé à son modèle : on passe à la plateforme (une ancienne version n'est jamais ressuscitée) ;
/// 2. sinon, de même avec le modèle de la plateforme ;
/// 3. sinon, aucun modèle. Pas de repli entre langues ni entre canaux : c'est le choix de l'Application.
/// Les versions d'un autre restaurant, d'un autre type, canal ou langue, les brouillons et les archivées sont ignorés,
/// quoi qu'on passe en entrée.
/// </summary>
public static class NotificationTemplateResolution
{
    /// <param name="tenantId">Null : envoi de la plateforme, seul l'étape 2 s'applique.</param>
    public static TemplateResolution Resolve(
        TenantId? tenantId,
        TemplateKey key,
        NotificationChannel channel,
        NotificationLanguage language,
        IEnumerable<NotificationTemplate> candidates)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(candidates);

        var matching = candidates
            .Where(template => template.Key == key && template.Channel == channel && template.Language == language)
            .Where(template => template.Status == TemplateStatus.Published)
            .ToList();

        if (tenantId is { } tenant && Latest(matching, tenant) is { } own && own.IsEnabled)
        {
            return new TemplateResolution(own, TemplateSource.Tenant);
        }

        return Latest(matching, null) is { IsEnabled: true } platform
            ? new TemplateResolution(platform, TemplateSource.Platform)
            : TemplateResolution.NotFound;
    }

    private static NotificationTemplate? Latest(IEnumerable<NotificationTemplate> published, TenantId? owner) =>
        published.Where(template => template.TenantId == owner).MaxBy(template => template.Version);
}
