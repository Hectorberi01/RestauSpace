using Customers.Application.Abstractions;
using Customers.Domain;

namespace Customers.Application;

/// <summary>
/// Étapes communes aux cas d'usage : le tenant vient du contexte (requête, ou message traité en arrière-plan), une
/// écriture exige un tenant qui les accepte, chaque agrégat est chargé dans ce tenant seulement (404 sinon), puis on
/// sauvegarde. Aucune règle métier ici : elles sont dans le domaine.
/// </summary>
public sealed class CustomersWorkspace(
    ITenantContext tenantContext,
    ICustomersTenants tenants,
    ICustomerRepository customers,
    IConsentRepository consents,
    ICustomerSettingsStore settings,
    ICustomersUnitOfWork unitOfWork,
    IPlanFeatureGate plan)
{
    public ICustomerRepository Customers => customers;

    public IConsentRepository Consents => consents;

    public ICustomerSettingsStore Settings => settings;

    public TenantId CurrentTenant => tenantContext.RequireTenant();

    public async Task RequireWritableTenantAsync(CancellationToken cancellationToken)
    {
        if (!await tenants.AcceptsBusinessWritesAsync(CurrentTenant, cancellationToken))
        {
            throw new DomainException(CustomersApplicationErrors.TenantNotWritable);
        }
    }

    /// <summary>Fonctionnalité du plan exigée (403 <c>common.plan.feature_required</c> sinon).</summary>
    public Task RequireFeatureAsync(string feature, CancellationToken cancellationToken) =>
        plan.RequireAsync(CurrentTenant, feature, cancellationToken);

    public async Task<Customer> LoadCustomerAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await customers.FindAsync(tenantId, CustomerId.From(id), cancellationToken))
        ?? throw new NotFoundException(CustomersApplicationErrors.CustomerNotFound);

    /// <summary>Charge le client du tenant courant (tenant qui accepte les écritures), applique <paramref name="change"/> et sauvegarde.</summary>
    public async Task EditCustomerAsync(Guid id, Action<Customer> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await RequireWritableTenantAsync(cancellationToken);
        change(await LoadCustomerAsync(CurrentTenant, id, cancellationToken));
        await SaveAsync(cancellationToken);
    }

    public Task SaveAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync(cancellationToken);

    /// <summary>Une saisie facultative : vide = absent ; sinon validée et normalisée par le noyau (erreur métier sinon).</summary>
    public static Email? OptionalEmail(string? value) => string.IsNullOrWhiteSpace(value) ? null : Email.Create(value);

    /// <inheritdoc cref="OptionalEmail"/>
    public static PhoneNumber? OptionalPhone(string? value) => string.IsNullOrWhiteSpace(value) ? null : PhoneNumber.Create(value);
}
