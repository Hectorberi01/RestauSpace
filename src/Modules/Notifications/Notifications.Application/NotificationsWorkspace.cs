using Notifications.Application.Abstractions;
using Notifications.Domain;

namespace Notifications.Application;

/// <summary>
/// Étapes communes aux cas d'usage : le tenant vient du contexte (requête authentifiée, ou message traité en
/// arrière-plan), une écriture exige un tenant qui les accepte, chaque agrégat est chargé dans ce tenant seulement (404
/// sinon), puis on sauvegarde. Aucune règle métier ici : elles sont dans le domaine.
/// </summary>
public sealed class NotificationsWorkspace(
    ITenantContext tenantContext,
    INotificationTenants tenants,
    INotificationProviderRepository providers,
    IRestaurantNotificationConfigurationRepository configurations,
    INotificationTemplateRepository templates,
    INotificationPreferenceRepository preferences,
    INotificationRepository notifications,
    INotificationsUnitOfWork unitOfWork)
{
    public INotificationProviderRepository Providers => providers;

    public IRestaurantNotificationConfigurationRepository Configurations => configurations;

    public INotificationTemplateRepository Templates => templates;

    public INotificationPreferenceRepository Preferences => preferences;

    public INotificationRepository Notifications => notifications;

    /// <summary>Le tenant authentifié, ou 403 <c>common.tenant.required</c>.</summary>
    public TenantId CurrentTenant => tenantContext.RequireTenant();

    /// <summary>Le tenant du contexte, ou null pour un traitement de la plateforme.</summary>
    public TenantId? CurrentTenantOrPlatform => tenantContext.CurrentTenantId;

    public async Task RequireWritableTenantAsync(CancellationToken cancellationToken)
    {
        if (!await tenants.AcceptsBusinessWritesAsync(CurrentTenant, cancellationToken))
        {
            throw new DomainException(NotificationsApplicationErrors.TenantNotWritable);
        }
    }

    public async Task<NotificationProvider> LoadProviderAsync(Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await providers.FindAsync(NotificationProviderId.From(id), cancellationToken))
        ?? throw new NotFoundException(NotificationsApplicationErrors.ProviderNotFound);

    public async Task<RestaurantNotificationConfiguration> LoadConfigurationAsync(Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await configurations.FindAsync(CurrentTenant, RestaurantNotificationConfigurationId.From(id), cancellationToken))
        ?? throw new NotFoundException(NotificationsApplicationErrors.ConfigurationNotFound);

    /// <summary>Un template de CE propriétaire : un restaurant n'atteint jamais ceux de la plateforme ni d'un autre restaurant.</summary>
    public async Task<NotificationTemplate> LoadTemplateAsync(TenantId? owner, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await templates.FindAsync(owner, TemplateId.From(id), cancellationToken))
        ?? throw new NotFoundException(NotificationsApplicationErrors.TemplateNotFound);

    public async Task<Notification> LoadNotificationAsync(TenantId? owner, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await notifications.FindAsync(owner, NotificationId.From(id), cancellationToken))
        ?? throw new NotFoundException(NotificationsApplicationErrors.NotificationNotFound);

    public Task SaveAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync(cancellationToken);

    public static void EnsurePage(int skip, int take) =>
        DomainException.ThrowIf(skip < 0 || take is < 1 or > 100, NotificationsApplicationErrors.PageInvalid);
}
