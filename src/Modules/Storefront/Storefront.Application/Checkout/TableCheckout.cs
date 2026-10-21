using Storefront.Application.Abstractions;
using Storefront.Application.Queries;
using Storefront.Domain;

namespace Storefront.Application.Checkout;

public static class TableCheckoutErrors
{
    // Même réponse pour un jeton inconnu, retiré, d'un autre restaurant ou d'un autre établissement.
    public static readonly DomainError TableNotFound = new("storefront.table.not_found", "This table QR code is not (or no longer) valid.");

    public static readonly DomainError DineInOnly = new("storefront.table.dine_in_only", "An order placed from a table is eaten on site.");

    public static readonly DomainError OutOfService = new("storefront.table.out_of_service", "This table is not in service.");

    public static readonly DomainError NotScheduled = new("storefront.table.not_scheduled", "An order placed from a table is served now.");
}

internal static class TableCheckout
{
    public static async Task<StorefrontTable?> ResolveAsync(
        IStorefrontTables tables, TenantId tenant, LocationId location, PlaceGuestOrderCommand command, CancellationToken cancellationToken)
    {
        if (command.TableToken is null)
        {
            return null;
        }

        var table = await tables.FindByQrTokenAsync(tenant, command.TableToken, cancellationToken);
        if (table is null || table.LocationId != location)
        {
            throw new NotFoundException(TableCheckoutErrors.TableNotFound);
        }

        DomainException.ThrowIf(command.Mode != ServiceMode.DineIn, TableCheckoutErrors.DineInOnly);
        DomainException.ThrowIf(command.ScheduledForUtc is not null, TableCheckoutErrors.NotScheduled);
        DomainException.ThrowIf(!table.IsInService, TableCheckoutErrors.OutOfService);
        return table;
    }
}

public sealed record StorefrontTableDto(Guid LocationId, string Label, string AreaName);

/// <summary>La table d'un QR code, pour le site : son établissement et son nom (404 si le jeton ne vaut rien ici).</summary>
public sealed record GetStorefrontTableQuery(string? Token) : IQuery<StorefrontTableDto>
{
    public override string ToString() => "GetStorefrontTableQuery (***)";
}

public sealed class GetStorefrontTableHandler(StorefrontWorkspace workspace, IStorefrontClient client, IStorefrontTables tables)
    : IQueryHandler<GetStorefrontTableQuery, StorefrontTableDto>
{
    public async Task<StorefrontTableDto> HandleAsync(GetStorefrontTableQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var app = await StorefrontReader.AppAsync(workspace, client, cancellationToken);
        var table = string.IsNullOrWhiteSpace(query.Token) ? null : await tables.FindByQrTokenAsync(workspace.CurrentTenant, query.Token, cancellationToken);
        return table is { IsInService: true } && StorefrontReader.Serves(app, client, table.LocationId)
            ? new StorefrontTableDto(table.LocationId.Value, table.Label, table.AreaName)
            : throw new NotFoundException(TableCheckoutErrors.TableNotFound);
    }
}
