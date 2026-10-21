using Locations.Application.Queries;
using Printing.Application.Abstractions;
using Printing.Application.Commands;
using SaasRestaurant.BuildingBlocks.Application;
using Storefront.Application.Abstractions;

namespace SaasRestaurant.Host;

// Les ports déclarés par Printing, et l'impression du ticket client demandée par Storefront. Aucun module n'en
// référence un autre : seul le host les relie. Fail-closed : un tenant autre que celui du contexte n'a rien.

/// <summary>Printing -> Tenants : écritures métier acceptées (inconnu = non).</summary>
internal sealed class PrintingTenantsFromTenants(CachedTenants tenants) : IPrintingTenants
{
    public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        tenants.AcceptsBusinessWritesAsync(tenantId, cancellationToken);
}

/// <summary>Printing -> Locations : l'établissement d'une imprimante.</summary>
internal sealed class PrintingLocationsFromLocations(ITenantContext tenantContext, IQueryHandler<GetLocationByIdQuery, LocationDto> locations)
    : IPrintingLocations
{
    public async Task<PrintingLocation?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var location = await locations.HandleAsync(new GetLocationByIdQuery(locationId.Value), cancellationToken);
            return new PrintingLocation(locationId, location.Name);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}

/// <summary>
/// Storefront -> Printing : le ticket client d'une commande passée à une borne. Une imprimante absente ou en erreur ne
/// fait jamais échouer la commande : le ticket n'est simplement pas imprimé (faux), et la borne affiche le numéro.
/// </summary>
internal sealed class StorefrontTicketsFromPrinting(
    ITenantContext tenantContext, ICommandHandler<PrintCustomerTicketCommand, Guid?> print, ILogger<StorefrontTicketsFromPrinting> logger)
    : IStorefrontTickets
{
    public async Task<bool> PrintAsync(TenantId tenantId, LocationId locationId, string reference, KioskTicket ticket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return false;
        }

        try
        {
            var input = new CustomerTicketInput(
                ticket.RestaurantName,
                ticket.LocationName,
                ticket.OrderNumber,
                ticket.ServiceMode,
                ticket.Lines.Select(line => new CustomerTicketLine(line.Quantity, line.Name, line.Details, line.Amount)).ToList(),
                ticket.Total,
                ticket.PaymentText,
                ticket.PrintedAt);
            return await print.HandleAsync(new PrintCustomerTicketCommand(locationId.Value, reference, input), cancellationToken) is not null;
        }
        catch (Exception exception) when (exception is DomainException or ConcurrencyException or NotFoundException or ForbiddenException)
        {
            logger.LogWarning("Customer ticket not queued for location {LocationId}: {ErrorCode}.", locationId.Value, Code(exception));
            return false;
        }
    }

    private static string Code(Exception exception) => exception switch
    {
        DomainException domain => domain.Error.Code,
        ConcurrencyException concurrency => concurrency.Error.Code,
        NotFoundException notFound => notFound.Error.Code,
        ForbiddenException forbidden => forbidden.Error.Code,
        _ => exception.GetType().Name,
    };
}
