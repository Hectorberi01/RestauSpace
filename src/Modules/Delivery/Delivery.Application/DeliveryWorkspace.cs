using Delivery.Application.Abstractions;
using Delivery.Domain;

namespace Delivery.Application;

/// <summary>
/// Étapes communes aux cas d'usage : le tenant vient du contexte de la requête (jamais de la requête), une écriture
/// exige un tenant qui les accepte, chaque fiche ou réglage est chargé dans ce tenant seulement (404 sinon).
/// Aucune règle métier ici : elles sont dans le domaine.
/// </summary>
public sealed class DeliveryWorkspace(
    ITenantContext tenantContext,
    IDeliveryTenants tenants,
    IDeliveryLocations locations,
    IDeliverySettingsRepository settings,
    IFulfillmentRepository fulfillments,
    ICourierRepository couriers,
    ICourierRemittanceRepository remittances,
    IDeliveryUnitOfWork unitOfWork,
    IClock clock)
{
    public const int MaxTake = 100;

    public IDeliverySettingsRepository Settings => settings;

    public IFulfillmentRepository Fulfillments => fulfillments;

    public ICourierRepository Couriers => couriers;

    public ICourierRemittanceRepository Remittances => remittances;

    public TenantId CurrentTenant => tenantContext.RequireTenant();

    /// <summary>Refuse l'écriture pour un tenant suspendu, fermé ou inconnu.</summary>
    public async Task RequireWritableTenantAsync(CancellationToken cancellationToken)
    {
        if (!await tenants.AcceptsBusinessWritesAsync(CurrentTenant, cancellationToken))
        {
            throw new DomainException(DeliveryApplicationErrors.TenantNotWritable);
        }
    }

    public async Task<LocationReference> LoadLocationAsync(Guid locationId, CancellationToken cancellationToken) =>
        (locationId == Guid.Empty ? null : await locations.FindAsync(CurrentTenant, LocationId.From(locationId), cancellationToken))
        ?? throw new NotFoundException(DeliveryApplicationErrors.LocationNotFound);

    /// <summary>Les réglages de l'établissement, créés avec les valeurs par défaut s'ils n'existent pas encore (ajoutés, pas enregistrés).</summary>
    public async Task<DeliverySettings> LoadOrCreateSettingsAsync(Guid locationId, CancellationToken cancellationToken)
    {
        var location = await LoadLocationAsync(locationId, cancellationToken);
        var existing = await settings.FindAsync(CurrentTenant, location.LocationId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var created = DeliverySettings.Create(CurrentTenant, location, await tenants.CurrencyOfAsync(CurrentTenant, cancellationToken), clock);
        settings.Add(created);
        return created;
    }

    public async Task<Currency> CurrencyAsync(CancellationToken cancellationToken) =>
        await tenants.CurrencyOfAsync(CurrentTenant, cancellationToken);

    public async Task<Fulfillment> LoadFulfillmentAsync(Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await fulfillments.FindAsync(CurrentTenant, FulfillmentId.From(id), cancellationToken))
        ?? throw new NotFoundException(DeliveryApplicationErrors.FulfillmentNotFound);

    public async Task<Courier> LoadCourierAsync(Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await couriers.FindAsync(CurrentTenant, CourierId.From(id), cancellationToken))
        ?? throw new NotFoundException(DeliveryApplicationErrors.CourierNotFound);

    public async Task<CourierRemittance> LoadRemittanceAsync(Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await remittances.FindAsync(CurrentTenant, CourierRemittanceId.From(id), cancellationToken))
        ?? throw new NotFoundException(DeliveryApplicationErrors.RemittanceNotFound);

    public Task SaveAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync(cancellationToken);

    public static void EnsurePage(int skip, int take) =>
        DomainException.ThrowIf(skip < 0 || take is < 1 or > MaxTake, DeliveryApplicationErrors.PageInvalid);
}
