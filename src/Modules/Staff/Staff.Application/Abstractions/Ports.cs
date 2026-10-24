using Staff.Domain;

namespace Staff.Application.Abstractions;

/// <summary>
/// Profils des membres du personnel. Chaque méthode prend le tenant explicitement : Staff est aussi lu EN DEHORS
/// d'une requête tenant (choix d'un restaurant, onboarding, "mes appartenances"), il ne se fie donc jamais au seul
/// tenant de la requête.
/// </summary>
public interface IStaffMemberRepository
{
    Task<StaffMember?> FindAsync(TenantId tenantId, StaffMemberId id, CancellationToken cancellationToken = default);

    Task<StaffMember?> FindByUserAsync(TenantId tenantId, UserId userId, CancellationToken cancellationToken = default);

    /// <summary>Page en lecture seule, du plus ancien au plus récent.</summary>
    Task<StaffPage> ListAsync(TenantId tenantId, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>Profils d'un utilisateur dans tous les tenants (lecture seule) : ce qu'affiche "mes restaurants".</summary>
    Task<IReadOnlyList<StaffMember>> ListByUserAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>Un membre invité, actif ou suspendu détient ce rôle, sur tout le tenant ou dans un établissement.</summary>
    Task<bool> IsRoleHeldAsync(TenantId tenantId, RoleId roleId, CancellationToken cancellationToken = default);

    /// <summary>Membres ACTIFS ayant <paramref name="roleId"/> sur tout le tenant, hors <paramref name="excluding"/>.</summary>
    Task<int> CountActiveTenantWideHoldersAsync(
        TenantId tenantId, RoleId roleId, StaffMemberId excluding, CancellationToken cancellationToken = default);

    /// <summary>Membres du personnel qui occupent une place : invités, actifs ou suspendus (pas ceux qui sont partis).</summary>
    Task<int> CountCurrentAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>Membres actifs du tenant (lecture seule).</summary>
    Task<IReadOnlyList<StaffMember>> ListActiveAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    void Add(StaffMember member);
}

/// <summary>
/// Le nombre maximal de membres du personnel (invités, actifs ou suspendus) prévu par le plan du restaurant, fourni par
/// l'hôte depuis Billing. Null : aucun plafond. Vérifié à l'invitation par le restaurant ; l'intégration du propriétaire
/// par la plateforme n'est jamais bloquée.
/// </summary>
public interface IStaffPlanLimits
{
    Task<long?> MaxStaffMembersAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

public sealed record StaffPage(IReadOnlyList<StaffMember> Items, int TotalCount);

public interface IRoleRepository
{
    Task<Role?> FindAsync(TenantId tenantId, RoleId id, CancellationToken cancellationToken = default);

    Task<Role?> FindSystemRoleAsync(TenantId tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, par nom.</summary>
    Task<IReadOnlyList<Role>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>Les rôles système (Owner, Manager) de TOUS les restaurants, suivis pour modification : maintenance de la plateforme.</summary>
    Task<IReadOnlyList<Role>> ListSystemRolesAsync(CancellationToken cancellationToken = default);

    void Add(Role role);

    void Remove(Role role);

    /// <summary>
    /// Réécrit la ligne du rôle à la sauvegarde pour que sa version protège une décision prise à partir de lui : deux
    /// requêtes retirant en même temps les deux derniers propriétaires touchent toutes deux le rôle Owner, et la
    /// seconde échoue sur un conflit de concurrence.
    /// </summary>
    void GuardDecisionsOn(Role role);
}

public enum TenantAccess
{
    Unknown = 0,
    ReadOnly = 1,
    Writable = 2,
}

/// <summary>Ce que Staff doit savoir d'un tenant, servi par le host depuis le module Tenants.</summary>
public interface IStaffTenants
{
    Task<TenantAccess> GetAccessAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>Nom affiché du restaurant, pour la liste « mes restaurants ». Null s'il n'existe pas.</summary>
    Task<string?> GetNameAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Un établissement du tenant courant sous forme de <see cref="LocationReference"/>, servi par le host depuis le
/// module Locations. Null s'il n'existe pas dans ce tenant.
/// </summary>
public interface ILocationDirectory
{
    Task<LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);
}

/// <summary>Les personnes sont des comptes du module Identity, servis par le host.</summary>
public interface IUserAccounts
{
    /// <summary>
    /// Le compte de cet email ; s'il n'y en a pas, un compte en attente est créé. L'email d'invitation part après
    /// l'enregistrement du membre (événement StaffInvited). Email invalide = l'erreur de domaine d'Identity.
    /// </summary>
    Task<UserId> FindOrInviteAsync(string? email, CancellationToken cancellationToken = default);

    /// <summary>
    /// L'email de chacun de ces comptes, pour que le gérant reconnaisse son personnel. Un compte inconnu ou effacé est
    /// absent du résultat.
    /// </summary>
    Task<IReadOnlyDictionary<UserId, string>> EmailsAsync(IReadOnlyCollection<UserId> users, CancellationToken cancellationToken = default);

    /// <summary>
    /// Nom court de chacun de ces comptes (« Awa K. »), affiché sur une caisse partagée ; à défaut de prénom, le début de
    /// l'email. Un compte inconnu ou effacé est absent du résultat.
    /// </summary>
    Task<IReadOnlyDictionary<UserId, string>> DisplayNamesAsync(IReadOnlyCollection<UserId> users, CancellationToken cancellationToken = default);
}

/// <summary>Caisses partagées. <see cref="FindAsync"/> ne filtre pas par tenant : une caisse s'authentifie par son id.</summary>
public interface ITillRepository
{
    Task<Till?> FindAsync(TillId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Till>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    void Add(Till till);
}

public interface ITimeEntryRepository
{
    Task<TimeEntry?> FindOpenAsync(TenantId tenantId, StaffMemberId memberId, CancellationToken cancellationToken = default);

    /// <summary>Présences ouvertes de ces membres (lecture seule).</summary>
    Task<IReadOnlyList<TimeEntry>> ListOpenAsync(TenantId tenantId, IReadOnlyCollection<StaffMemberId> members, CancellationToken cancellationToken = default);

    /// <summary>Présences commencées dans [<paramref name="fromUtc"/>, <paramref name="untilUtc"/>[ (lecture seule).</summary>
    Task<IReadOnlyList<TimeEntry>> ListAsync(
        TenantId tenantId, DateTimeOffset fromUtc, DateTimeOffset untilUtc, LocationId? locationId, StaffMemberId? memberId, int max,
        CancellationToken cancellationToken = default);

    void Add(TimeEntry entry);
}

/// <summary>Empreinte lente et salée des PIN (4 à 6 chiffres : jamais une empreinte rapide).</summary>
public interface IPinHasher
{
    string Hash(string pin);

    bool Verify(string pin, string hash);
}

/// <summary>Clés des caisses partagées : longues et aléatoires, gardées sous forme d'empreinte.</summary>
public interface ITillSecrets
{
    /// <returns>Le secret à remettre une fois à l'appareil, et son empreinte à garder.</returns>
    (string Secret, string Hash) Generate();

    bool Matches(string secret, string hash);
}

/// <summary>
/// Les jetons d'un membre identifié par PIN sur une caisse partagée, émis par Identity (servi par l'hôte) : une session
/// propre au membre, qui agit pour ce restaurant. Null si son compte ne peut pas ouvrir de session.
/// </summary>
public interface IOperatorSessions
{
    Task<OperatorTokens?> StartAsync(UserId userId, TenantId tenantId, string device, CancellationToken cancellationToken = default);
}

public sealed record OperatorTokens(
    Guid UserId,
    Guid SessionId,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc);

/// <summary>L'utilisateur qui fait la requête. Null hors requête HTTP (job, seed, maintenance) : aucun contrôle d'escalade.</summary>
public interface IStaffActor
{
    UserId? UserId { get; }
}

/// <summary>La caisse partagée de la requête, si la requête a été authentifiée par sa clé.</summary>
public interface ITillClient
{
    TillId? TillId { get; }
}
