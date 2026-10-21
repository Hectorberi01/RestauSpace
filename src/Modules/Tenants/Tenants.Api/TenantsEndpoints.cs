using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SaasRestaurant.BuildingBlocks.Api;
using Tenants.Application;
using Tenants.Application.Commands;
using Tenants.Application.Queries;

namespace Tenants.Api;

public static class TenantsEndpoints
{
    private const string GetTenantRoute = "GetTenant";

    public static RouteGroupBuilder MapTenantsEndpoints(this IEndpointRouteBuilder routes)
    {
        var tenants = routes.MapGroup("/admin/tenants").WithTags("Tenants");

        tenants.MapPost("/", async ( CreateTenantRequest request, ICommandHandler<CreateTenantCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(
                new CreateTenantCommand(
                    request.Name, 
                    request.DefaultCurrencyCode, 
                    request.TimeZoneId, 
                    request.PricesIncludeTax),
                    ct);
            return TypedResults.CreatedAtRoute(new TenantCreatedResponse(id), GetTenantRoute, new { id });
        });


        tenants.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetTenantByIdQuery, TenantDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetTenantByIdQuery(id), ct)))
            .WithName(GetTenantRoute);

        // Cycle de vie.
        tenants.MapPost("/{id:guid}/activate", async (
            Guid id, ICommandHandler<ActivateTenantCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new ActivateTenantCommand(id), ct);
            return TypedResults.NoContent();
        });

        tenants.MapPost("/{id:guid}/suspend", async (
            Guid id, SuspendTenantRequest request, ICommandHandler<SuspendTenantCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new SuspendTenantCommand(id, request.Reason), ct);
            return TypedResults.NoContent();
        });

        tenants.MapPost("/{id:guid}/reactivate", async (
            Guid id, ICommandHandler<ReactivateTenantCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new ReactivateTenantCommand(id), ct);
            return TypedResults.NoContent();
        });

        tenants.MapPost("/{id:guid}/close", async (
            Guid id, ICommandHandler<CloseTenantCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new CloseTenantCommand(id), ct);
            return TypedResults.NoContent();
        });

        // Paramètres : chacun est remplacé en bloc.
        tenants.MapPut("/{id:guid}/name", async (
            Guid id, RenameTenantRequest request, ICommandHandler<RenameTenantCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new RenameTenantCommand(id, request.Name), ct);
            return TypedResults.NoContent();
        });

        tenants.MapPut("/{id:guid}/default-currency", async (
            Guid id,
            ChangeTenantDefaultCurrencyRequest request,
            ICommandHandler<ChangeTenantDefaultCurrencyCommand> handler,
            CancellationToken ct) =>
        {
            await handler.HandleAsync(new ChangeTenantDefaultCurrencyCommand(id, request.CurrencyCode), ct);
            return TypedResults.NoContent();
        });

        tenants.MapPut("/{id:guid}/time-zone", async (
            Guid id,
            ChangeTenantTimeZoneRequest request,
            ICommandHandler<ChangeTenantTimeZoneCommand> handler,
            CancellationToken ct) =>
        {
            await handler.HandleAsync(new ChangeTenantTimeZoneCommand(id, request.TimeZoneId), ct);
            return TypedResults.NoContent();
        });

        tenants.MapPut("/{id:guid}/price-tax-mode", async (
            Guid id,
            ChangeTenantPriceTaxModeRequest request,
            ICommandHandler<ChangeTenantPriceTaxModeCommand> handler,
            CancellationToken ct) =>
        {
            await handler.HandleAsync(new ChangeTenantPriceTaxModeCommand(id, request.PricesIncludeTax), ct);
            return TypedResults.NoContent();
        });

        tenants.MapPut("/{id:guid}/branding", async (
            Guid id,
            UpdateTenantBrandingRequest request,
            ICommandHandler<UpdateTenantBrandingCommand> handler,
            CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new UpdateTenantBrandingCommand(id, request.DisplayName, request.LogoUrl, request.PrimaryColor), ct);
            return TypedResults.NoContent();
        });

        return tenants;
    }

    /// <summary>
    /// Le restaurant sélectionné pour ses membres : <c>GET /tenant</c> pour tous, et ses paramètres pour
    /// <see cref="TenantsPermissions.Configure"/>. Le tenant vient UNIQUEMENT du contexte authentifié, jamais de la requête.
    /// </summary>
    public static RouteGroupBuilder MapCurrentTenantEndpoints(this IEndpointRouteBuilder routes)
    {
        var current = routes.MapGroup("/tenant").WithTags("Tenants");

        current.MapGet("/", async (IQueryHandler<GetCurrentTenantQuery, TenantDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetCurrentTenantQuery(), ct)));

        current.MapPut("/name", async (
            RenameTenantRequest request, ITenantContext tenant, ICommandHandler<RenameTenantCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new RenameTenantCommand(tenant.RequireTenant().Value, request.Name), ct);
            return TypedResults.NoContent();
        }).RequirePermission(TenantsPermissions.Configure);

        current.MapPut("/branding", async (
            UpdateTenantBrandingRequest request, ITenantContext tenant, ICommandHandler<UpdateTenantBrandingCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new UpdateTenantBrandingCommand(tenant.RequireTenant().Value, request.DisplayName, request.LogoUrl, request.PrimaryColor), ct);
            return TypedResults.NoContent();
        }).RequirePermission(TenantsPermissions.Configure);

        current.MapPut("/public-profile", async (
            UpdateTenantPublicProfileRequest request, ITenantContext tenant, ICommandHandler<UpdateTenantPublicProfileCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new UpdateTenantPublicProfileCommand(
                    tenant.RequireTenant().Value, request.Tagline, request.Description, request.CoverImageUrl, request.Links), ct);
            return TypedResults.NoContent();
        }).RequirePermission(TenantsPermissions.Configure);

        current.MapPut("/time-zone", async (
            ChangeTenantTimeZoneRequest request, ITenantContext tenant, ICommandHandler<ChangeTenantTimeZoneCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new ChangeTenantTimeZoneCommand(tenant.RequireTenant().Value, request.TimeZoneId), ct);
            return TypedResults.NoContent();
        }).RequirePermission(TenantsPermissions.Configure);

        // Devise et mode de prix : refusés dès qu'un prix existe au catalogue (règle du domaine).
        current.MapPut("/default-currency", async (
            ChangeTenantDefaultCurrencyRequest request,
            ITenantContext tenant,
            ICommandHandler<ChangeTenantDefaultCurrencyCommand> handler,
            CancellationToken ct) =>
        {
            await handler.HandleAsync(new ChangeTenantDefaultCurrencyCommand(tenant.RequireTenant().Value, request.CurrencyCode), ct);
            return TypedResults.NoContent();
        }).RequirePermission(TenantsPermissions.Configure);

        current.MapPut("/price-tax-mode", async (
            ChangeTenantPriceTaxModeRequest request,
            ITenantContext tenant,
            ICommandHandler<ChangeTenantPriceTaxModeCommand> handler,
            CancellationToken ct) =>
        {
            await handler.HandleAsync(new ChangeTenantPriceTaxModeCommand(tenant.RequireTenant().Value, request.PricesIncludeTax), ct);
            return TypedResults.NoContent();
        }).RequirePermission(TenantsPermissions.Configure);

        return current;
    }
}
