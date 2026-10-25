using Identity.Application.Abstractions;
using Identity.Application.Commands;
using Identity.Application.Queries;
using Locations.Application.Queries;
using SaasRestaurant.BuildingBlocks.Application;
using Staff.Application.Abstractions;
using Staff.Application.Queries;
using Staff.Domain;
using Tenants.Application.Queries;
using Tenants.Domain.Tenants;

namespace SaasRestaurant.Host;

// Les ports déclarés par Staff et Identity, servis par les cas d'usage des autres modules. Le host est le seul
// endroit qui connaît plusieurs modules : aucun module n'en référence un autre.

/// <summary>Staff -> Tenants : le tenant existe-t-il, et accepte-t-il les écritures métier ?</summary>
internal sealed class StaffTenantsFromTenants(CachedTenants tenants) : IStaffTenants
{
    public async Task<TenantAccess> GetAccessAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        (await tenants.FindAsync(tenantId, cancellationToken)) switch
        {
            null => TenantAccess.Unknown,
            { CanAcceptBusinessWrites: true } => TenantAccess.Writable,
            _ => TenantAccess.ReadOnly,
        };

    public async Task<string?> GetNameAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        (await tenants.FindAsync(tenantId, cancellationToken))?.Name;
}

/// <summary>Staff -> Locations : un établissement du tenant de la requête, comme référence d'affectation.</summary>
internal sealed class LocationDirectoryFromLocations(
    ITenantContext tenantContext, IQueryHandler<GetLocationByIdQuery, LocationDto> locations) : ILocationDirectory
{
    public async Task<LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default)
    {
        // La query de Locations répond pour le tenant de la requête : jamais pour un autre.
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var location = await locations.HandleAsync(new GetLocationByIdQuery(locationId.Value), cancellationToken);
            return new LocationReference(tenantId, locationId, location.IsOperational);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}

/// <summary>Staff -> Identity : le compte d'un email, créé en attente d'invitation s'il n'existe pas.</summary>
internal sealed class UserAccountsFromIdentity(
    ICommandHandler<EnsureInvitedUserCommand, Guid> ensure,
    IQueryHandler<GetUserByIdQuery, UserDto> user) : IUserAccounts
{
    public async Task<UserId> FindOrInviteAsync(string? email, CancellationToken cancellationToken = default) =>
        UserId.From(await ensure.HandleAsync(new EnsureInvitedUserCommand(email), cancellationToken));

    public async Task<IReadOnlyDictionary<UserId, string>> EmailsAsync(IReadOnlyCollection<UserId> users, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(users);
        var emails = new Dictionary<UserId, string>();
        foreach (var id in users)
        {
            try
            {
                var account = await user.HandleAsync(new GetUserByIdQuery(id.Value), cancellationToken);
                if (account.ErasedAtUtc is null)
                {
                    emails[id] = account.Email;
                }
            }
            catch (NotFoundException)
            {
            }
        }

        return emails;
    }

    public async Task<IReadOnlyDictionary<UserId, string>> DisplayNamesAsync(IReadOnlyCollection<UserId> users, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(users);
        var names = new Dictionary<UserId, string>();
        foreach (var id in users)
        {
            try
            {
                var account = await user.HandleAsync(new GetUserByIdQuery(id.Value), cancellationToken);
                if (account.ErasedAtUtc is null)
                {
                    names[id] = ShortName(account.FirstName, account.LastName, account.Email);
                }
            }
            catch (NotFoundException)
            {
            }
        }

        return names;
    }

    // « Awa K. » ; sans prénom, la partie de l'email avant « @ ».
    private static string ShortName(string? firstName, string? lastName, string email)
    {
        var first = firstName?.Trim();
        if (string.IsNullOrEmpty(first))
        {
            var at = email.IndexOf('@', StringComparison.Ordinal);
            return at > 0 ? email[..at] : email;
        }

        var last = lastName?.Trim();
        return string.IsNullOrEmpty(last) ? first : $"{first} {char.ToUpperInvariant(last[0])}.";
    }
}

/// <summary>Staff -> Identity : la session d'un membre reconnu par son PIN sur une caisse partagée.</summary>
internal sealed class OperatorSessionsFromIdentity(ICommandHandler<StartOperatorSessionCommand, OperatorSessionDto?> start) : IOperatorSessions
{
    public async Task<OperatorTokens?> StartAsync(
        UserId userId, TenantId tenantId, TillId tillId, LocationId locationId, string device, CancellationToken cancellationToken = default)
    {
        var session = await start.HandleAsync(
            new StartOperatorSessionCommand(userId.Value, tenantId.Value, tillId.Value, locationId.Value, device), cancellationToken);
        return session is null
            ? null
            : new OperatorTokens(session.UserId, session.SessionId, session.AccessToken, session.AccessTokenExpiresAtUtc);
    }
}

internal sealed class StaffInvitationFlow(IServiceScopeFactory scopes, ILogger<StaffInvitationFlow> logger) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        // Un membre invité par le restaurant, ou le propriétaire intégré par la plateforme : un compte sans mot de passe
        // reçoit son lien d'invitation (un compte déjà actif n'en reçoit pas, voir SendUserInvitationCommand).
        Guid userId;
        switch (message.Module, message.Type)
        {
            case ("staff", "StaffInvited"):
                userId = message.Read<SaasRestaurant.Contracts.Staff.StaffInvited>().UserId;
                break;
            case ("staff", "StaffInvitationResent"):
                userId = message.Read<SaasRestaurant.Contracts.Staff.StaffInvitationResent>().UserId;
                break;
            case ("staff", "StaffOwnerCreated"):
                userId = message.Read<SaasRestaurant.Contracts.Staff.StaffOwnerCreated>().UserId;
                break;
            default:
                return;
        }

        await using var scope = scopes.CreateAsyncScope();
        if (await scope.ServiceProvider.GetRequiredService<ICommandHandler<SendUserInvitationCommand, bool>>()
            .HandleAsync(new SendUserInvitationCommand(userId), cancellationToken))
        {
            logger.LogInformation("Invitation sent to user {UserId} after staff message {MessageId}.", userId, message.Id);
        }
    }
}

/// <summary>
/// Identity -> Staff + Tenants : un utilisateur peut agir pour un tenant tant que son profil de membre du personnel
/// est ACTIF et que le tenant n'est pas fermé (un tenant suspendu reste lisible). Un restaurant fermé ou introuvable ne
/// retient pas non plus son seul propriétaire. Un membre gelé (au-delà de la limite du plan) ne le sélectionne pas.
/// </summary>
internal sealed class TenantMembershipsFromStaff(
    IQueryHandler<IsActiveStaffMemberQuery, bool> staff,
    IQueryHandler<ListSoleOwnershipsQuery, IReadOnlyList<Guid>> soleOwnerships,
    IQueryHandler<GetTenantByIdQuery, TenantDto> tenants,
    IServiceScopeFactory scopes) : ITenantMemberships
{
    /// <summary>
    /// Le tenant demandé n'est pas encore celui du jeton : lu dans un scope à part, hors du contexte HTTP (non transmis),
    /// qui agit pour CE tenant (<see cref="BackgroundTenant"/>). La limite du plan est donc la sienne.
    /// </summary>
    public async Task<bool> IsFrozenAsync(UserId userId, TenantId tenantId, CancellationToken cancellationToken = default)
    {
        Task<bool> check;
        using (ExecutionContext.SuppressFlow())
        {
            check = Task.Run(
                async () =>
                {
                    await using var scope = scopes.CreateAsyncScope();
                    scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(tenantId);
                    return await scope.ServiceProvider.GetRequiredService<IQueryHandler<IsStaffMemberFrozenQuery, bool>>()
                        .HandleAsync(new IsStaffMemberFrozenQuery(userId.Value, tenantId.Value), cancellationToken);
                },
                cancellationToken);
        }

        return await check;
    }

    public async Task<IReadOnlyList<TenantId>> SoleOwnerTenantsAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var result = new List<TenantId>();
        foreach (var tenantId in await soleOwnerships.HandleAsync(new ListSoleOwnershipsQuery(userId.Value), cancellationToken))
        {
            if (await IsOpenAsync(tenantId, cancellationToken))
            {
                result.Add(TenantId.From(tenantId));
            }
        }

        return result;
    }

    public async Task<bool> CanActForTenantAsync(UserId userId, TenantId tenantId, CancellationToken cancellationToken = default)
    {
        if (!await staff.HandleAsync(new IsActiveStaffMemberQuery(userId.Value, tenantId.Value), cancellationToken))
        {
            return false;
        }

        return await IsOpenAsync(tenantId.Value, cancellationToken);
    }

    private async Task<bool> IsOpenAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            var tenant = await tenants.HandleAsync(new GetTenantByIdQuery(tenantId), cancellationToken);
            return tenant.Status != TenantStatus.Closed;
        }
        catch (NotFoundException)
        {
            return false;
        }
    }
}
