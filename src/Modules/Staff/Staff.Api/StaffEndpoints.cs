using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using SaasRestaurant.BuildingBlocks.Api;
using Staff.Application;
using Staff.Application.Commands;
using Staff.Application.Queries;

namespace Staff.Api;

/// <summary>
/// Trois groupes de routes, chacun protégé par l'hôte : <c>/staff</c> (restaurant sélectionné dans le token, plus la
/// permission propre à chaque route), <c>/me/memberships</c> (utilisateur connecté, sans restaurant) et la route
/// d'onboarding de la plateforme. Aucune route ne prend de tenant, sauf celle de la plateforme.
/// </summary>
public sealed record StaffRouteGroups(RouteGroupBuilder Staff, RouteGroupBuilder MyMemberships, RouteGroupBuilder Platform, RouteGroupBuilder Till);

public static partial class StaffEndpoints
{
    private const string GetMemberRoute = "GetStaffMember";

    public static StaffRouteGroups MapStaffEndpoints(this IEndpointRouteBuilder routes) =>
        new(MapStaff(routes), MapMyMemberships(routes), MapPlatform(routes), MapTill(routes));

    private static RouteGroupBuilder MapStaff(IEndpointRouteBuilder routes)
    {
        var staff = routes.MapGroup("/staff").WithTags("Staff");

        // Tout membre du restaurant : qu'ai-je le droit de faire ici ? Session de caisse : liste blanche seulement.
        staff.MapGet("/me/permissions", async (
            ClaimsPrincipal caller, HttpContext http, IQueryHandler<GetMyPermissionsQuery, MyPermissionsDto> handler, CancellationToken ct) =>
        {
            var mine = await handler.HandleAsync(new GetMyPermissionsQuery(UserOf(caller)), ct);
            if (!TillSessions.IsTillSession(caller))
            {
                return TypedResults.Ok(mine);
            }

            var allowed = http.RequestServices.GetService(typeof(TillSessionPermissions)) as TillSessionPermissions
                ?? new TillSessionPermissions([]);
            var location = TillSessions.LocationOf(caller);
            var here = mine.Locations?.FirstOrDefault(entry => entry.LocationId == location);
            return TypedResults.Ok(mine with
            {
                Permissions = allowed.Filter(here?.Permissions ?? mine.Permissions),
                Locations = here is null ? [] : [here with { Permissions = allowed.Filter(here.Permissions) }],
            });
        });

        // PIN de caisse du membre connecté.
        staff.MapPut("/me/pin", (
            PinRequest request, ClaimsPrincipal caller, ICommandHandler<SetMyPinCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetMyPinCommand(UserOf(caller), request.Pin), ct)));

        staff.MapDelete("/me/pin", (
            ClaimsPrincipal caller, ICommandHandler<ClearMyPinCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ClearMyPinCommand(UserOf(caller)), ct)));

        // Membres.
        staff.MapGet("/members", async (
            int? page, int? pageSize, IQueryHandler<ListStaffQuery, PagedResult<StaffMemberDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListStaffQuery(page ?? 1, pageSize ?? ListStaffQuery.DefaultPageSize), ct)))
            .RequirePermission(StaffPermissions.ManageStaff);

        staff.MapGet("/members/{id:guid}", async (
            Guid id, IQueryHandler<GetStaffMemberQuery, StaffMemberDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetStaffMemberQuery(id), ct)))
            .WithName(GetMemberRoute)
            .RequirePermission(StaffPermissions.ManageStaff);

        staff.MapPost("/members/invitations", async (
            InviteStaffRequest request, ICommandHandler<InviteStaffCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new InviteStaffCommand(request.Email, request.TenantWideRoleId), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetMemberRoute, new { id });
        }).RequirePermission(StaffPermissions.ManageStaff);

        staff.MapPost("/members/{id:guid}/invitation", (
            Guid id, ICommandHandler<ResendStaffInvitationCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ResendStaffInvitationCommand(id), ct)))
            .RequirePermission(StaffPermissions.ManageStaff);

        staff.MapPost("/members/{id:guid}/locations", (
            Guid id, AssignToLocationRequest request, ICommandHandler<AssignStaffToLocationCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new AssignStaffToLocationCommand(id, request.LocationId, request.RoleId), ct)))
            .RequirePermission(StaffPermissions.ManageStaff);

        staff.MapDelete("/members/{id:guid}/locations/{locationId:guid}", (
            Guid id, Guid locationId, ICommandHandler<RemoveStaffFromLocationCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RemoveStaffFromLocationCommand(id, locationId), ct)))
            .RequirePermission(StaffPermissions.ManageStaff);

        staff.MapPut("/members/{id:guid}/tenant-role", (
            Guid id, TenantWideRoleRequest request, ICommandHandler<AssignTenantWideRoleCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new AssignTenantWideRoleCommand(id, request.RoleId), ct)))
            .RequirePermission(StaffPermissions.ManageStaff);

        staff.MapDelete("/members/{id:guid}/tenant-role", (
            Guid id, ICommandHandler<RemoveTenantWideRoleCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RemoveTenantWideRoleCommand(id), ct)))
            .RequirePermission(StaffPermissions.ManageStaff);

        staff.MapPost("/members/{id:guid}/suspend", (
            Guid id, ICommandHandler<SuspendStaffCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SuspendStaffCommand(id), ct)))
            .RequirePermission(StaffPermissions.ManageStaff);

        staff.MapPost("/members/{id:guid}/reinstate", (
            Guid id, ICommandHandler<ReinstateStaffCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ReinstateStaffCommand(id), ct)))
            .RequirePermission(StaffPermissions.ManageStaff);

        staff.MapDelete("/members/{id:guid}", (
            Guid id, ICommandHandler<RemoveStaffCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RemoveStaffCommand(id), ct)))
            .RequirePermission(StaffPermissions.ManageStaff);

        staff.MapDelete("/members/{id:guid}/pin", (
            Guid id, ICommandHandler<ResetMemberPinCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ResetMemberPinCommand(id), ct)))
            .RequirePermission(StaffPermissions.ManageStaff);

        // Caisses partagées et pointage.
        staff.MapGet("/tills", async (IQueryHandler<ListTillsQuery, IReadOnlyList<TillDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListTillsQuery(), ct)))
            .RequirePermission(StaffPermissions.ManageStaff);

        staff.MapPost("/tills", async (
            TillRequest request, ICommandHandler<RegisterTillCommand, RegisteredTillDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new RegisterTillCommand(request.LocationId, request.Name, request.AutoLockMinutes), ct)))
            .RequirePermission(StaffPermissions.ManageStaff);

        staff.MapPut("/tills/{id:guid}", (
            Guid id, TillChangeRequest request, ICommandHandler<ChangeTillCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeTillCommand(id, request.Name, request.AutoLockMinutes), ct)))
            .RequirePermission(StaffPermissions.ManageStaff);

        staff.MapDelete("/tills/{id:guid}", (
            Guid id, ICommandHandler<RevokeTillCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RevokeTillCommand(id), ct)))
            .RequirePermission(StaffPermissions.ManageStaff);

        staff.MapGet("/time-entries", async (
            DateTimeOffset from, DateTimeOffset to, Guid? locationId, Guid? staffMemberId,
            IQueryHandler<ListTimeEntriesQuery, IReadOnlyList<TimeEntryDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListTimeEntriesQuery(from, to, locationId, staffMemberId), ct)))
            .RequirePermission(StaffPermissions.ManageStaff);

        // Rôles.
        staff.MapGet("/roles", async (
            IQueryHandler<ListRolesQuery, IReadOnlyList<RoleDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListRolesQuery(), ct)))
            .RequirePermission(StaffPermissions.ManageStaff);

        staff.MapGet("/permissions", async (
            IQueryHandler<GetPermissionCatalogQuery, IReadOnlyList<string>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetPermissionCatalogQuery(), ct)))
            .RequirePermission(StaffPermissions.ManageRoles);

        staff.MapPost("/roles", async (
            RoleNameRequest request, ICommandHandler<CreateRoleCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new CreateRoleCommand(request.Name), ct);
            return TypedResults.Created($"/staff/roles/{id}", new CreatedResponse(id));
        }).RequirePermission(StaffPermissions.ManageRoles);

        staff.MapPut("/roles/{id:guid}/name", (
            Guid id, RoleNameRequest request, ICommandHandler<RenameRoleCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RenameRoleCommand(id, request.Name), ct)))
            .RequirePermission(StaffPermissions.ManageRoles);

        staff.MapDelete("/roles/{id:guid}", (
            Guid id, ICommandHandler<DeleteRoleCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new DeleteRoleCommand(id), ct)))
            .RequirePermission(StaffPermissions.ManageRoles);

        staff.MapPost("/roles/{id:guid}/permissions", (
            Guid id, PermissionRequest request, ICommandHandler<GrantPermissionCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new GrantPermissionCommand(id, request.Permission), ct)))
            .RequirePermission(StaffPermissions.ManageRoles);

        staff.MapDelete("/roles/{id:guid}/permissions/{permission}", (
            Guid id, string permission, ICommandHandler<RevokePermissionCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RevokePermissionCommand(id, permission), ct)))
            .RequirePermission(StaffPermissions.ManageRoles);

        MapSchedule(staff);
        return staff;
    }

    // L'utilisateur est toujours celui de l'access token.
    private static RouteGroupBuilder MapMyMemberships(IEndpointRouteBuilder routes)
    {
        var memberships = routes.MapGroup("/me/memberships").WithTags("Me");

        memberships.MapGet("/", async (
            ClaimsPrincipal caller, IQueryHandler<ListMyMembershipsQuery, IReadOnlyList<MembershipDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListMyMembershipsQuery(UserOf(caller)), ct)));

        memberships.MapPost("/{tenantId:guid}/accept", (
            Guid tenantId, ClaimsPrincipal caller, ICommandHandler<AcceptStaffInvitationCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new AcceptStaffInvitationCommand(UserOf(caller), tenantId), ct)));

        return memberships;
    }

    private static RouteGroupBuilder MapPlatform(IEndpointRouteBuilder routes)
    {
        var platform = routes.MapGroup("/admin/tenants/{tenantId:guid}/owners").WithTags("Tenants");

        platform.MapPost("/", async (
            Guid tenantId, OnboardOwnerRequest request, ICommandHandler<OnboardTenantOwnerCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new OnboardTenantOwnerCommand(tenantId, request.Email), ct);
            return TypedResults.Created((string?)null, new CreatedResponse(id));
        });

        return platform;
    }

    // La caisse partagée, authentifiée par sa clé : écran verrouillé, déverrouillage et pointage par PIN.
    private static RouteGroupBuilder MapTill(IEndpointRouteBuilder routes)
    {
        var till = routes.MapGroup("/till")
            .WithTags("Till")
            .RequireAuthorization(TillKeyAuthentication.Policy)
            .RequireRateLimiting(TillKeyAuthentication.RateLimitPolicy);

        till.MapGet("/", async (IQueryHandler<GetTillStateQuery, TillStateDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetTillStateQuery(), ct)));

        till.MapPost("/unlock", async (
            TillPinRequest request, HttpContext http, ICommandHandler<UnlockTillCommand, TillSessionDto> handler, CancellationToken ct) =>
        {
            // Des jetons : jamais en cache.
            http.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(await handler.HandleAsync(new UnlockTillCommand(request.StaffMemberId, request.Pin), ct));
        });

        till.MapPost("/clock", async (
            TillPinRequest request, ICommandHandler<ClockOnTillCommand, ClockResultDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ClockOnTillCommand(request.StaffMemberId, request.Pin), ct)));

        return till;
    }

    private static Guid UserOf(ClaimsPrincipal caller) =>
        Guid.TryParse(caller.FindFirst("sub")?.Value, out var id) && id != Guid.Empty
            ? id
            : throw new UnauthenticatedException(StaffApplicationErrors.InvalidAccessToken);

    private static async Task<NoContent> Run(Task command)
    {
        await command;
        return TypedResults.NoContent();
    }
}
