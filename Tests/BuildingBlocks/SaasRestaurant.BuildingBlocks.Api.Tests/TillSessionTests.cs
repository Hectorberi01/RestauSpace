using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace SaasRestaurant.BuildingBlocks.Api;

/// <summary>Session de caisse (PIN) : liste blanche de permissions, établissement de la caisse, routes d'écriture fermées.</summary>
public sealed class TillSessionTests
{
    private const string Sell = "orders.take";
    private const string ManageStaff = "staff.manage";

    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid TillLocation = Guid.NewGuid();

    /// <summary>Le rôle du membre a TOUT (propriétaire) : seule la liste blanche limite la caisse.</summary>
    private sealed class OwnerEverywhere : IPermissionChecker
    {
        public List<Guid> LocationsAsked { get; } = [];

        public Task<bool> HasTenantPermissionAsync(UserId userId, TenantId tenantId, string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> HasLocationPermissionAsync(
            UserId userId, TenantId tenantId, LocationId locationId, string permission, CancellationToken cancellationToken = default)
        {
            LocationsAsked.Add(locationId.Value);
            return Task.FromResult(true);
        }
    }

    private readonly OwnerEverywhere _checker = new();

    private IAuthorizationService Authorization()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddPermissionAuthorization();
        services.AddTillSessionPermissions([Sell]);
        services.AddSingleton<IPermissionChecker>(_checker);
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal Principal(bool till)
    {
        List<Claim> claims = [new("sub", User.ToString()), new(TenantClaims.TenantId, Tenant.ToString())];
        if (till)
        {
            claims.Add(new Claim(TillClaims.TillId, Guid.NewGuid().ToString()));
            claims.Add(new Claim(TillClaims.LocationId, TillLocation.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private async Task<bool> AllowedAsync(ClaimsPrincipal user, string permission, HttpContext? http = null) =>
        (await Authorization().AuthorizeAsync(user, http ?? new DefaultHttpContext(), new PermissionRequirement(permission))).Succeeded;

    [Fact]
    public async Task A_till_session_keeps_an_operating_permission_of_its_role_at_the_till_location()
    {
        Assert.True(await AllowedAsync(Principal(till: true), Sell));
        Assert.Equal(TillLocation, Assert.Single(_checker.LocationsAsked));
    }

    [Fact]
    public async Task A_till_session_loses_an_administration_permission_even_for_an_owner()
    {
        Assert.False(await AllowedAsync(Principal(till: true), ManageStaff));
        Assert.True(await AllowedAsync(Principal(till: false), ManageStaff));
    }

    [Fact]
    public async Task A_till_session_cannot_act_for_another_location()
    {
        var http = new DefaultHttpContext();
        http.Request.RouteValues["locationId"] = Guid.NewGuid().ToString();

        Assert.False(await AllowedAsync(Principal(till: true), Sell, http));

        http.Request.RouteValues["locationId"] = TillLocation.ToString();
        Assert.True(await AllowedAsync(Principal(till: true), Sell, http));
    }

    [Fact]
    public async Task Without_a_registered_list_a_till_session_has_no_permission()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddPermissionAuthorization();
        services.AddSingleton<IPermissionChecker>(_checker);
        var authorization = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(Principal(till: true), new DefaultHttpContext(), new PermissionRequirement(Sell));

        Assert.False(result.Succeeded);
    }

    private static HttpContext Request(string method, bool till, params object[] metadata)
    {
        var http = new DefaultHttpContext { User = Principal(till) };
        http.Request.Method = method;
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "test"));
        return http;
    }

    [Fact]
    public void Routes_without_permission_are_read_only_for_a_till_session()
    {
        var permission = new AuthorizationPolicyBuilder().AddRequirements(new PermissionRequirement(Sell)).Build();

        Assert.False(TillSessions.Refuses(Request("GET", till: true)));
        Assert.True(TillSessions.Refuses(Request("PUT", till: true)));
        Assert.True(TillSessions.Refuses(Request("POST", till: true)));
        Assert.False(TillSessions.Refuses(Request("POST", till: true, permission)));
        Assert.False(TillSessions.Refuses(Request("POST", till: false)));
    }

    [Fact]
    public void A_route_can_be_explicitly_opened_or_closed_to_a_till_session()
    {
        Assert.False(TillSessions.Refuses(Request("DELETE", till: true, new TillSessionAccess(true))));
        Assert.True(TillSessions.Refuses(Request("GET", till: true, new TillSessionAccess(false))));
        Assert.False(TillSessions.Refuses(Request("GET", till: false, new TillSessionAccess(false))));
    }
}
