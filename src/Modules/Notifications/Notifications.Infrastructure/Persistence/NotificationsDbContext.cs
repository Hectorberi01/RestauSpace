using Notifications.Domain;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Notifications.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Notifications : son propre schéma, jamais joint aux tables d'un autre module.
/// Isolation par tenant (deuxième ligne de défense, derrière les repositories qui chargent toujours par tenant) :
/// <list type="bullet">
/// <item>configurations des restaurants et préférences (<see cref="ITenantOwned"/>) : le filtre de
/// <see cref="TenantDbContext"/>, aucune ligne sans tenant ;</item>
/// <item>notifications et codes à usage unique (tenant OU plateforme) : ceux du tenant courant ; sans tenant,
/// UNIQUEMENT ceux de la plateforme ;</item>
/// <item>templates (tenant OU plateforme) : ceux du tenant courant ET ceux de la plateforme (le repli de la
/// résolution) ; sans tenant, uniquement ceux de la plateforme. Un tenant LIT un template de la plateforme, il ne le
/// modifie jamais : les repositories ne le lui donnent pas à modifier ;</item>
/// <item>catalogue des prestataires et appareils (d'un utilisateur, lus par son id) : pas de filtre.</item>
/// </list>
/// Les jobs de plateforme qui doivent voir tous les tenants (worker d'envoi, webhooks, purge) le déclarent avec
/// <c>IgnoreQueryFilters()</c>. Les enfants (tentatives, affectations, choix) suivent leur racine.
/// </summary>
public sealed class NotificationsDbContext : TenantDbContext
{
    private readonly ITenantContext _tenantContext;

    public NotificationsDbContext(DbContextOptions<NotificationsDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);
        _tenantContext = tenantContext;
    }

    /// <summary>Catalogue de la plateforme : pas de tenant, donc pas de filtre.</summary>
    public DbSet<NotificationProvider> Providers => Set<NotificationProvider>();

    public DbSet<RestaurantNotificationConfiguration> Configurations => Set<RestaurantNotificationConfiguration>();

    public DbSet<NotificationTemplate> Templates => Set<NotificationTemplate>();

    public DbSet<NotificationPreference> Preferences => Set<NotificationPreference>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<DeviceRegistration> Devices => Set<DeviceRegistration>();

    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();

    public DbSet<Campaign> Campaigns => Set<Campaign>();

    public DbSet<MarketingRules> MarketingRules => Set<MarketingRules>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>Coffre des identifiants (chiffrés) : jamais chargé avec un agrégat.</summary>
    internal DbSet<NotificationSecret> Secrets => Set<NotificationSecret>();

    internal DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    internal DbSet<NotificationUsage> Usage => Set<NotificationUsage>();

    internal DbSet<NotificationWebhookEvent> Webhooks => Set<NotificationWebhookEvent>();

    /// <summary>
    /// Le tenant courant, null sans tenant. Lu par les filtres à l'exécution de chaque requête (EF Core paramètre les
    /// membres du contexte), jamais figé dans le modèle.
    /// </summary>
    internal TenantId? CurrentTenant => _tenantContext.CurrentTenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationsDbContext).Assembly);
        modelBuilder.ApplyOutbox();
        ApplyTenantQueryFilters(modelBuilder);

        // Racines à tenant facultatif : hors de ITenantOwned, donc filtrées ici. Comparer deux TenantId? donne la
        // sémantique SQL attendue : sans tenant courant, « tenant_id IS NULL » (la plateforme seule).
        modelBuilder.Entity<Notification>().HasQueryFilter(notification => notification.TenantId == CurrentTenant);
        modelBuilder.Entity<OtpChallenge>().HasQueryFilter(challenge => challenge.TenantId == CurrentTenant);
        modelBuilder.Entity<NotificationTemplate>()
            .HasQueryFilter(template => template.TenantId == null || template.TenantId == CurrentTenant);
    }
}
