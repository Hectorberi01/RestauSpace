using Staff.Application.Abstractions;
using Staff.Domain;

namespace Staff.Application.Commands;

// --- PIN de caisse ---

/// <summary>Le membre connecté choisit son PIN de caisse dans le restaurant sélectionné.</summary>
public sealed record SetMyPinCommand(Guid UserId, string? Pin) : ICommand;

public sealed class SetMyPinHandler(StaffWorkspace workspace, IPinHasher pins) : ICommandHandler<SetMyPinCommand>
{
    public async Task HandleAsync(SetMyPinCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await workspace.RequireWritableTenantAsync(cancellationToken);
        var member = await workspace.LoadSelfAsync(tenantId, command.UserId, cancellationToken);
        member.SetPin(pins.Hash(StaffPin.Validate(command.Pin)));
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record ClearMyPinCommand(Guid UserId) : ICommand;

public sealed class ClearMyPinHandler(StaffWorkspace workspace) : ICommandHandler<ClearMyPinCommand>
{
    public async Task HandleAsync(ClearMyPinCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await workspace.RequireWritableTenantAsync(cancellationToken);
        var member = await workspace.LoadSelfAsync(tenantId, command.UserId, cancellationToken);
        member.ClearPin();
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Le gérant efface le PIN d'un membre (oublié, ou connu d'un autre) : le membre en choisira un nouveau.</summary>
public sealed record ResetMemberPinCommand(Guid StaffMemberId) : ICommand;

public sealed class ResetMemberPinHandler(StaffWorkspace workspace) : ICommandHandler<ResetMemberPinCommand>
{
    public async Task HandleAsync(ResetMemberPinCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await workspace.RequireWritableTenantAsync(cancellationToken);
        var member = await workspace.LoadMemberAsync(tenantId, command.StaffMemberId, cancellationToken);
        member.ClearPin();
        await workspace.SaveAsync(cancellationToken);
    }
}

// --- Caisses partagées (console) ---

public sealed record TillDto(Guid Id, Guid LocationId, string Name, int AutoLockMinutes, DateTimeOffset CreatedAtUtc, DateTimeOffset? LastUsedAtUtc, bool IsActive)
{
    public static TillDto From(Till till) =>
        new(till.Id.Value, till.LocationId.Value, till.Name, till.AutoLockMinutes, till.CreatedAtUtc, till.LastUsedAtUtc, till.IsActive);
}

/// <param name="Key">À garder sur l'appareil : elle n'est montrée qu'une fois et n'est jamais stockée en clair.</param>
public sealed record RegisteredTillDto(Guid Id, string Key);

public sealed record RegisterTillCommand(Guid LocationId, string? Name, int AutoLockMinutes) : ICommand<RegisteredTillDto>;

public sealed class RegisterTillHandler(StaffWorkspace workspace, ILocationDirectory locations, ITillRepository tills, ITillSecrets secrets, IClock clock)
    : ICommandHandler<RegisterTillCommand, RegisteredTillDto>
{
    public async Task<RegisteredTillDto> HandleAsync(RegisterTillCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await workspace.RequireWritableTenantAsync(cancellationToken);
        var location = (command.LocationId == Guid.Empty
                ? null
                : await locations.FindAsync(tenantId, LocationId.From(command.LocationId), cancellationToken))
            ?? throw new NotFoundException(StaffApplicationErrors.LocationNotFound);

        var (secret, hash) = secrets.Generate();
        var till = Till.Register(location, command.Name, command.AutoLockMinutes, hash, clock);
        tills.Add(till);
        await workspace.SaveAsync(cancellationToken);
        return new RegisteredTillDto(till.Id.Value, TillKey.Format(till.Id, secret));
    }
}

public sealed record ChangeTillCommand(Guid TillId, string? Name, int AutoLockMinutes) : ICommand;

public sealed class ChangeTillHandler(StaffWorkspace workspace, ITillRepository tills) : ICommandHandler<ChangeTillCommand>
{
    public async Task HandleAsync(ChangeTillCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await workspace.RequireWritableTenantAsync(cancellationToken);
        var till = await tills.LoadAsync(tenantId, command.TillId, cancellationToken);
        till.Change(command.Name, command.AutoLockMinutes);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record RevokeTillCommand(Guid TillId) : ICommand;

public sealed class RevokeTillHandler(StaffWorkspace workspace, ITillRepository tills, IClock clock) : ICommandHandler<RevokeTillCommand>
{
    public async Task HandleAsync(RevokeTillCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await workspace.RequireWritableTenantAsync(cancellationToken);
        var till = await tills.LoadAsync(tenantId, command.TillId, cancellationToken);
        till.Revoke(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record ListTillsQuery : IQuery<IReadOnlyList<TillDto>>;

public sealed class ListTillsHandler(ITenantContext tenantContext, ITillRepository tills) : IQueryHandler<ListTillsQuery, IReadOnlyList<TillDto>>
{
    public async Task<IReadOnlyList<TillDto>> HandleAsync(ListTillsQuery query, CancellationToken cancellationToken = default) =>
        (await tills.ListAsync(tenantContext.RequireTenant(), cancellationToken))
            .OrderByDescending(till => till.IsActive)
            .ThenBy(till => till.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(TillDto.From)
            .ToList();
}

// --- Sur la caisse partagée (authentifiée par sa clé) ---

public sealed record TillOperatorDto(Guid StaffMemberId, string Name, bool HasPin, bool PinLocked, DateTimeOffset? ClockedInSinceUtc);

public sealed record TillStateDto(Guid TillId, string Name, Guid LocationId, int AutoLockMinutes, IReadOnlyList<TillOperatorDto> Operators);

/// <summary>Ce que montre l'écran verrouillé : la caisse et les membres qui travaillent dans son établissement.</summary>
public sealed record GetTillStateQuery : IQuery<TillStateDto>;

public sealed class GetTillStateHandler(TillDesk desk, IStaffMemberRepository members, ITimeEntryRepository entries, IUserAccounts users, IClock clock)
    : IQueryHandler<GetTillStateQuery, TillStateDto>
{
    public async Task<TillStateDto> HandleAsync(GetTillStateQuery query, CancellationToken cancellationToken = default)
    {
        var till = await desk.RequireTillAsync(cancellationToken);
        var here = (await members.ListActiveAsync(till.TenantId, cancellationToken))
            .Where(member => member.EffectiveRoleAt(till.LocationId) is not null)
            .ToList();
        var names = await users.DisplayNamesAsync(here.Select(member => member.UserId).Distinct().ToList(), cancellationToken);
        var open = (await entries.ListOpenAsync(till.TenantId, here.Select(member => member.Id).ToList(), cancellationToken))
            .ToDictionary(entry => entry.StaffMemberId);
        var now = clock.UtcNow;
        var operators = here
            .Where(member => names.ContainsKey(member.UserId))
            .Select(member => new TillOperatorDto(
                member.Id.Value,
                names[member.UserId],
                member.HasPin,
                member.PinLockedUntilUtc > now,
                open.TryGetValue(member.Id, out var entry) && !entry.IsStale(now) ? entry.ClockedInAtUtc : null))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new TillStateDto(till.Id.Value, till.Name, till.LocationId.Value, till.AutoLockMinutes, operators);
    }
}

public sealed record TillSessionDto(
    Guid TenantId,
    Guid LocationId,
    Guid StaffMemberId,
    string Name,
    Guid UserId,
    Guid SessionId,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc);

/// <summary>Un membre se fait reconnaître par son PIN : il reçoit une session de caisse à lui, sans refresh token.</summary>
public sealed record UnlockTillCommand(Guid StaffMemberId, string? Pin) : ICommand<TillSessionDto>;

public sealed class UnlockTillHandler(TillDesk desk, IOperatorSessions sessions, IUserAccounts users)
    : ICommandHandler<UnlockTillCommand, TillSessionDto>
{
    public async Task<TillSessionDto> HandleAsync(UnlockTillCommand command, CancellationToken cancellationToken = default)
    {
        var till = await desk.RequireTillAsync(cancellationToken);
        var member = await desk.VerifyPinAsync(till, command.StaffMemberId, command.Pin, cancellationToken);
        await desk.EnsureNotFrozenAsync(till, member, cancellationToken);
        var tokens = await sessions.StartAsync(member.UserId, till.TenantId, till.Id, till.LocationId, $"Caisse partagée · {till.Name}", cancellationToken)
            ?? throw new ForbiddenException(StaffApplicationErrors.SessionRefused);
        var names = await users.DisplayNamesAsync([member.UserId], cancellationToken);
        return new TillSessionDto(
            till.TenantId.Value,
            till.LocationId.Value,
            member.Id.Value,
            names.GetValueOrDefault(member.UserId) ?? string.Empty,
            tokens.UserId,
            tokens.SessionId,
            tokens.AccessToken,
            tokens.AccessTokenExpiresAtUtc);
    }
}

public sealed record ClockResultDto(Guid StaffMemberId, bool ClockedIn, DateTimeOffset AtUtc, TimeSpan? Worked);

/// <summary>Arrivée ou départ, par PIN sur la caisse : le pointage suit l'état actuel du membre.</summary>
public sealed record ClockOnTillCommand(Guid StaffMemberId, string? Pin) : ICommand<ClockResultDto>;

public sealed class ClockOnTillHandler(TillDesk desk, StaffWorkspace workspace, ITimeEntryRepository entries, IClock clock)
    : ICommandHandler<ClockOnTillCommand, ClockResultDto>
{
    public async Task<ClockResultDto> HandleAsync(ClockOnTillCommand command, CancellationToken cancellationToken = default)
    {
        var till = await desk.RequireTillAsync(cancellationToken);
        await workspace.EnsureWritableAsync(till.TenantId, cancellationToken);
        var member = await desk.VerifyPinAsync(till, command.StaffMemberId, command.Pin, cancellationToken);

        var open = await entries.FindOpenAsync(till.TenantId, member.Id, cancellationToken);
        if (open is not null)
        {
            var stale = open.IsStale(clock.UtcNow);
            open.ClockOut(clock);
            if (!stale)
            {
                await workspace.SaveAsync(cancellationToken);
                return new ClockResultDto(member.Id.Value, false, open.ClockedOutAtUtc!.Value, open.WorkedUntil(clock.UtcNow));
            }

            // Départ oublié la veille : la présence est fermée d'office et ce pointage est une arrivée.
            await workspace.SaveAsync(cancellationToken);
        }

        var entry = TimeEntry.ClockIn(member, till.LocationId, clock);
        entries.Add(entry);
        await workspace.SaveAsync(cancellationToken);
        return new ClockResultDto(member.Id.Value, true, entry.ClockedInAtUtc, null);
    }
}

/// <summary>
/// La caisse de la requête (authentifiée par sa clé) et la vérification d'un PIN : le membre doit travailler dans
/// l'établissement de la caisse ; un essai manqué est enregistré avant d'être refusé.
/// </summary>
public sealed class TillDesk(
    ITillClient client, ITillRepository tills, IStaffMemberRepository members, IPinHasher pins, IStaffUnitOfWork unitOfWork, StaffFreeze freeze, IClock clock)
{
    public async Task<Till> RequireTillAsync(CancellationToken cancellationToken)
    {
        var till = client.TillId is { } id ? await tills.FindAsync(id, cancellationToken) : null;
        return till is { IsActive: true } ? till : throw new UnauthenticatedException(StaffApplicationErrors.TillKeyInvalid);
    }

    public async Task<StaffMember> VerifyPinAsync(Till till, Guid memberId, string? pin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(till);
        var member = (memberId == Guid.Empty ? null : await members.FindAsync(till.TenantId, StaffMemberId.From(memberId), cancellationToken))
            ?? throw new NotFoundException(StaffApplicationErrors.MemberNotFound);
        DomainException.ThrowIf(member.EffectiveRoleAt(till.LocationId) is null, StaffApplicationErrors.NotAtThisLocation);

        var matches = member.PinHash is { } hash && StaffPin.IsWellFormed(pin) && pins.Verify(pin!, hash);
        var accepted = member.TryPin(matches, clock);
        till.MarkUsed(clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return accepted ? member : throw new UnauthenticatedException(StaffApplicationErrors.PinWrong);
    }

    /// <summary>Un membre gelé (au-delà de la limite du plan) n'ouvre pas la caisse : 403 <c>common.plan.over_limit</c>.</summary>
    public async Task EnsureNotFrozenAsync(Till till, StaffMember member, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(till);
        ArgumentNullException.ThrowIfNull(member);
        if (await freeze.IsFrozenAsync(till.TenantId, member.Id, cancellationToken))
        {
            throw PlanFeatures.OverLimit("staff");
        }
    }
}

/// <summary>Clé d'une caisse partagée : « {id de la caisse}.{secret} ».</summary>
public static class TillKey
{
    public static string Format(TillId id, string secret) => $"{id.Value:N}.{secret}";

    public static bool TryParse(string? key, out TillId id, out string secret)
    {
        id = default;
        secret = string.Empty;
        var dot = key?.IndexOf('.', StringComparison.Ordinal) ?? -1;
        if (dot <= 0 || !Guid.TryParseExact(key![..dot], "N", out var value) || value == Guid.Empty || dot == key.Length - 1)
        {
            return false;
        }

        id = TillId.From(value);
        secret = key[(dot + 1)..];
        return true;
    }
}

internal static class TillRepositoryExtensions
{
    public static async Task<Till> LoadAsync(this ITillRepository tills, TenantId tenantId, Guid id, CancellationToken cancellationToken)
    {
        var till = id == Guid.Empty ? null : await tills.FindAsync(TillId.From(id), cancellationToken);
        return till is not null && till.TenantId == tenantId ? till : throw new NotFoundException(StaffApplicationErrors.TillNotFound);
    }
}

public sealed record TillPrincipal(Guid TenantId, Guid TillId);

/// <summary>Pour l'authentification : la caisse de cette clé, si elle existe, n'est pas révoquée et que le secret correspond.</summary>
public sealed record AuthenticateTillQuery(string? Key) : IQuery<TillPrincipal?>;

public sealed class AuthenticateTillHandler(ITillRepository tills, ITillSecrets secrets) : IQueryHandler<AuthenticateTillQuery, TillPrincipal?>
{
    public async Task<TillPrincipal?> HandleAsync(AuthenticateTillQuery query, CancellationToken cancellationToken = default)
    {
        if (!TillKey.TryParse(query.Key, out var id, out var secret))
        {
            return null;
        }

        var till = await tills.FindAsync(id, cancellationToken);
        return till is { IsActive: true } && secrets.Matches(secret, till.SecretHash) ? new TillPrincipal(till.TenantId.Value, till.Id.Value) : null;
    }
}
