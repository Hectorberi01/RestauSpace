using Staff.Application.Abstractions;
using Staff.Domain;

namespace Staff.Application.Queries;

public sealed record ListStaffQuery(int Page = 1, int PageSize = ListStaffQuery.DefaultPageSize) : IQuery<PagedResult<StaffMemberDto>>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxPage = 100_000;
}

public sealed class ListStaffHandler(ITenantContext tenantContext, IStaffMemberRepository members, IUserAccounts users)
    : IQueryHandler<ListStaffQuery, PagedResult<StaffMemberDto>>
{
    public async Task<PagedResult<StaffMemberDto>> HandleAsync(ListStaffQuery query, CancellationToken cancellationToken = default)
    {
        if (query.Page is < 1 or > ListStaffQuery.MaxPage || query.PageSize is < 1 or > ListStaffQuery.MaxPageSize)
        {
            throw new DomainException(StaffApplicationErrors.PagingInvalid);
        }

        var page = await members.ListAsync(
            tenantContext.RequireTenant(), (query.Page - 1) * query.PageSize, query.PageSize, cancellationToken);
        var emails = await users.EmailsAsync(page.Items.Select(member => member.UserId).Distinct().ToList(), cancellationToken);
        return PagedResult<StaffMemberDto>.Counted(
            page.Items.Select(member => StaffMemberDto.From(member, emails.GetValueOrDefault(member.UserId))).ToList(),
            query.Page,
            query.PageSize,
            page.TotalCount);
    }
}

public sealed record GetStaffMemberQuery(Guid StaffMemberId) : IQuery<StaffMemberDto>;

public sealed class GetStaffMemberHandler(ITenantContext tenantContext, IStaffMemberRepository members, IUserAccounts users)
    : IQueryHandler<GetStaffMemberQuery, StaffMemberDto>
{
    public async Task<StaffMemberDto> HandleAsync(GetStaffMemberQuery query, CancellationToken cancellationToken = default)
    {
        var member = query.StaffMemberId == Guid.Empty
            ? null
            : await members.FindAsync(tenantContext.RequireTenant(), StaffMemberId.From(query.StaffMemberId), cancellationToken);
        var found = member ?? throw new NotFoundException(StaffApplicationErrors.MemberNotFound);
        var emails = await users.EmailsAsync([found.UserId], cancellationToken);
        return StaffMemberDto.From(found, emails.GetValueOrDefault(found.UserId));
    }
}

public sealed record ListRolesQuery : IQuery<IReadOnlyList<RoleDto>>;

public sealed class ListRolesHandler(ITenantContext tenantContext, IRoleRepository roles)
    : IQueryHandler<ListRolesQuery, IReadOnlyList<RoleDto>>
{
    public async Task<IReadOnlyList<RoleDto>> HandleAsync(ListRolesQuery query, CancellationToken cancellationToken = default) =>
        (await roles.ListAsync(tenantContext.RequireTenant(), cancellationToken)).Select(RoleDto.From).ToList();
}

/// <summary>Toutes les permissions qu'un rôle peut recevoir sur cette plateforme.</summary>
public sealed record GetPermissionCatalogQuery : IQuery<IReadOnlyList<string>>;

public sealed class GetPermissionCatalogHandler(KnownPermissions permissions)
    : IQueryHandler<GetPermissionCatalogQuery, IReadOnlyList<string>>
{
    public Task<IReadOnlyList<string>> HandleAsync(GetPermissionCatalogQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(permissions.Catalog.Codes.Select(code => code.Value).ToList());
}

/// <summary>Restaurants de l'utilisateur connecté, quel que soit le sélectionné : ce qu'il peut sélectionner ou accepter.</summary>
public sealed record ListMyMembershipsQuery(Guid UserId) : IQuery<IReadOnlyList<MembershipDto>>;

public sealed class ListMyMembershipsHandler(IStaffMemberRepository members, IStaffTenants tenants)
    : IQueryHandler<ListMyMembershipsQuery, IReadOnlyList<MembershipDto>>
{
    public async Task<IReadOnlyList<MembershipDto>> HandleAsync(ListMyMembershipsQuery query, CancellationToken cancellationToken = default)
    {
        if (query.UserId == Guid.Empty)
        {
            return [];
        }

        var profiles = await members.ListByUserAsync(UserId.From(query.UserId), cancellationToken);
        var memberships = new List<MembershipDto>();
        foreach (var member in profiles.Where(member => member.Status != StaffStatus.Left))
        {
            // Un restaurant introuvable (supprimé entre-temps) n'est pas proposé : il ne pourrait pas être sélectionné.
            var name = await tenants.GetNameAsync(member.TenantId, cancellationToken);
            if (name != null)
            {
                memberships.Add(new MembershipDto(member.TenantId.Value, name, member.Id.Value, member.Status));
            }
        }

        return memberships.OrderBy(membership => membership.TenantName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}

/// <summary>Permissions du membre connecté dans le restaurant sélectionné (niveau tenant), pour le front end.</summary>
public sealed record GetMyPermissionsQuery(Guid UserId) : IQuery<MyPermissionsDto>;

public sealed class GetMyPermissionsHandler(ITenantContext tenantContext, IStaffMemberRepository members, IRoleRepository roles)
    : IQueryHandler<GetMyPermissionsQuery, MyPermissionsDto>
{
    public async Task<MyPermissionsDto> HandleAsync(GetMyPermissionsQuery query, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.RequireTenant();
        var member = (query.UserId == Guid.Empty
                ? null
                : await members.FindByUserAsync(tenantId, UserId.From(query.UserId), cancellationToken))
            ?? throw new NotFoundException(StaffApplicationErrors.MemberNotFound);

        var role = member.EffectiveTenantRole is { } roleId ? await roles.FindAsync(tenantId, roleId, cancellationToken) : null;
        var granted = role?.Permissions.Select(permission => permission.Value).Order(StringComparer.Ordinal).ToList() ?? new List<string>();
        return new MyPermissionsDto(member.Id.Value, member.Status, member.TenantWideRoleId?.Value, granted, member.HasPin);
    }
}

/// <summary>Pour l'hôte : cet utilisateur peut-il agir pour ce tenant ? Un profil de personnel ACTIF est requis.</summary>
public sealed record IsActiveStaffMemberQuery(Guid UserId, Guid TenantId) : IQuery<bool>;

public sealed class IsActiveStaffMemberHandler(IStaffMemberRepository members) : IQueryHandler<IsActiveStaffMemberQuery, bool>
{
    public async Task<bool> HandleAsync(IsActiveStaffMemberQuery query, CancellationToken cancellationToken = default)
    {
        if (query.UserId == Guid.Empty || query.TenantId == Guid.Empty)
        {
            return false;
        }

        var member = await members.FindByUserAsync(TenantId.From(query.TenantId), UserId.From(query.UserId), cancellationToken);
        return member?.Status == StaffStatus.Active;
    }
}

/// <summary>
/// Pour l'hôte : les restaurants dont cet utilisateur est le SEUL propriétaire actif (rôle Owner sur tout le tenant).
/// Lu avant l'effacement d'un compte en libre-service.
/// </summary>
public sealed record ListSoleOwnershipsQuery(Guid UserId) : IQuery<IReadOnlyList<Guid>>;

public sealed class ListSoleOwnershipsHandler(IStaffMemberRepository members, IRoleRepository roles)
    : IQueryHandler<ListSoleOwnershipsQuery, IReadOnlyList<Guid>>
{
    public async Task<IReadOnlyList<Guid>> HandleAsync(ListSoleOwnershipsQuery query, CancellationToken cancellationToken = default)
    {
        if (query.UserId == Guid.Empty)
        {
            return [];
        }

        var result = new List<Guid>();
        foreach (var member in await members.ListByUserAsync(UserId.From(query.UserId), cancellationToken))
        {
            if (member.Status != StaffStatus.Active || member.EffectiveTenantRole is not { } roleId)
            {
                continue;
            }

            var role = await roles.FindAsync(member.TenantId, roleId, cancellationToken);
            if (role is not null && SystemRoles.IsOwnerRole(role)
                && await members.CountActiveTenantWideHoldersAsync(member.TenantId, roleId, member.Id, cancellationToken) == 0)
            {
                result.Add(member.TenantId.Value);
            }
        }

        return result;
    }
}
