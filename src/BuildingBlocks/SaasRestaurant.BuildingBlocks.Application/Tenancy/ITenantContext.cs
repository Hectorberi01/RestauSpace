namespace SaasRestaurant.BuildingBlocks.Application;

/// <summary>
/// Le tenant pour lequel agit la requête ou le job courant. En HTTP, il vient de l'access token validé, jamais d'un
/// en-tête, d'une route ou d'un body ; un job d'arrière-plan en reçoit un explicite (<see cref="FixedTenantContext"/>).
/// </summary>
public interface ITenantContext
{
    /// <summary>Null si rien n'agit pour un tenant : route anonyme, route plateforme, aucun restaurant choisi.</summary>
    TenantId? CurrentTenantId { get; }
}

public static class TenantContextExtensions
{
    /// <summary>Le tenant courant, ou 403 <c>common.tenant.required</c> quand aucun restaurant n'est sélectionné.</summary>
    public static TenantId RequireTenant(this ITenantContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.CurrentTenantId ?? throw new ForbiddenException(TenancyErrors.TenantRequired);
    }
}

/// <summary>Tenant fourni explicitement : jobs de fond, consommateurs d'outbox, tests. Null = aucun tenant.</summary>
public sealed class FixedTenantContext(TenantId? tenantId) : ITenantContext
{
    public TenantId? CurrentTenantId => tenantId;
}

/// <summary>
/// Tenant d'un traitement d'arrière-plan (consommateur d'outbox, job) qui agit pour un tenant précis, connu par le
/// message qu'il traite. Un par scope DI. N'a AUCUN effet pendant une requête HTTP : le tenant y vient toujours du jeton.
/// </summary>
public sealed class BackgroundTenant
{
    public TenantId? Current { get; private set; }

    /// <summary>Fixe le tenant du scope ; une seule fois, pour qu'un traitement ne puisse pas changer de tenant en route.</summary>
    public void ActFor(TenantId tenantId)
    {
        Guard.NotEmpty(tenantId);
        if (Current is { } current && current != tenantId)
        {
            throw new InvalidOperationException("This background scope already acts for another tenant.");
        }

        Current = tenantId;
    }
}

public static class TenancyErrors
{
    public static readonly DomainError TenantRequired =
        new("common.tenant.required", "Select a restaurant before using this feature.");
}

/// <summary>Claim de l'access token qui porte le tenant sélectionné. Écrit par Identity, lu par le host.</summary>
public static class TenantClaims
{
    public const string TenantId = "tid";
}

/// <summary>Claims d'une session de caisse partagée (ouverte par PIN) : la caisse et son établissement. Écrits par Identity.</summary>
public static class TillClaims
{
    public const string TillId = "till";
    public const string LocationId = "till_loc";
}
