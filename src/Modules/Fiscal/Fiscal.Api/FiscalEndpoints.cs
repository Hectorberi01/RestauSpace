using Fiscal.Application;
using Fiscal.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SaasRestaurant.BuildingBlocks.Api;

namespace Fiscal.Api;

public sealed record FiscalProfileRequest(string? Ifu, string? Rccm, TaxRegime Regime, AibRate Aib = AibRate.None);

public sealed record FiscalTokenRequest(string? Token);

public sealed record FiscalModeRequest(FiscalMode Mode);

/// <summary>
/// Facturation normalisée (e-MECeF) du restaurant sélectionné dans l'access token, sous <c>Tenancy.TenantMemberPolicy</c>
/// chaînée par l'hôte. Lecture <see cref="FiscalPermissions.View"/>, réglages <see cref="FiscalPermissions.Configure"/>.
/// Le jeton s'écrit, il ne se relit jamais. Les mentions fiscales d'une commande (reçu) sont lisibles par tout membre.
/// </summary>
public static class FiscalEndpoints
{
    private const string View = FiscalPermissions.View;
    private const string Configure = FiscalPermissions.Configure;

    public static RouteGroupBuilder MapFiscalEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var fiscal = routes.MapGroup("/fiscal").WithTags("Fiscal");

        fiscal.MapGet("/profile", async (IQueryHandler<GetFiscalProfileQuery, FiscalProfileDto?> handler, CancellationToken ct) =>
            await handler.HandleAsync(new GetFiscalProfileQuery(), ct) is { } profile ? Results.Ok(profile) : Results.NoContent())
            .RequirePermission(View);

        fiscal.MapPut("/profile", async (FiscalProfileRequest request, ICommandHandler<ConfigureFiscalProfileCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new ConfigureFiscalProfileCommand(request.Ifu, request.Rccm, request.Regime, request.Aib), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Configure);

        fiscal.MapPut("/profile/token", async (FiscalTokenRequest request, ICommandHandler<SetFiscalTokenCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new SetFiscalTokenCommand(request.Token), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Configure);

        fiscal.MapPut("/profile/mode", async (FiscalModeRequest request, ICommandHandler<SetFiscalModeCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new SetFiscalModeCommand(request.Mode), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Configure);

        fiscal.MapPost("/profile/test", async (ICommandHandler<TestFiscalConnectionCommand, FiscalConnectionDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new TestFiscalConnectionCommand(), ct)))
            .RequirePermission(Configure);

        fiscal.MapGet("/invoices", async (
            FiscalInvoiceStatus? status, DateTimeOffset? from, DateTimeOffset? to, int? take,
            IQueryHandler<ListFiscalInvoicesQuery, IReadOnlyList<FiscalInvoiceDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListFiscalInvoicesQuery(status, from, to, take ?? 100), ct)))
            .RequirePermission(View);

        fiscal.MapPost("/invoices/{id:guid}/retry", async (Guid id, ICommandHandler<RetryFiscalInvoiceCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new RetryFiscalInvoiceCommand(id), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Configure);

        fiscal.MapGet("/orders/{orderId:guid}", async (Guid orderId, IQueryHandler<GetOrderFiscalQuery, OrderFiscalDto?> handler, CancellationToken ct) =>
            await handler.HandleAsync(new GetOrderFiscalQuery(orderId), ct) is { } fiscalInfo ? Results.Ok(fiscalInfo) : Results.NoContent());

        return fiscal;
    }
}
