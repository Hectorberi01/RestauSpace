using Billing.Application;
using Billing.Application.Commands;
using Billing.Application.Queries;
using Billing.Domain;
using Identity.Api;
using Identity.Application.Commands;
using Identity.Domain.Users;
using Microsoft.AspNetCore.Http.HttpResults;
using SaasRestaurant.BuildingBlocks.Application;
using Staff.Application.Commands;
using Tenants.Application.Commands;
using Tenants.Domain.Tenants;

namespace SaasRestaurant.Host;

/// <summary>Un pays où un restaurant peut s'inscrire seul : sa devise et son fuseau horaire par défaut.</summary>
public sealed record SignUpCountry(string Code, string Name, string CurrencyCode, string TimeZoneId);

/// <summary>Un plan proposé à l'inscription (plan de base actif) : ce que le site public peut afficher, rien de plus.</summary>
public sealed record SignUpPlan(
    Guid Id, string Name, decimal Price, string CurrencyCode, BillingInterval BillingInterval, IReadOnlyList<EntitlementDto> Entitlements);

public sealed record SignUpOptionsResponse(IReadOnlyList<SignUpCountry> Countries, IReadOnlyList<SignUpPlan> Plans, int TrialDays);

/// <param name="Website">Champ piège invisible : rempli, la demande est ignorée sans rien révéler.</param>
public sealed record SignUpRequest(
    string? Email,
    string? Password,
    string? FirstName,
    string? LastName,
    string? RestaurantName,
    string? Country,
    Guid PlanId,
    bool AcceptTerms,
    string? Website);

public sealed record SignUpResponse(int TrialDays);

public static class SignUpErrors
{
    public static readonly DomainError TermsNotAccepted = new("signup.terms_not_accepted", "The terms of service must be accepted.");

    public static readonly DomainError CountryUnsupported = new("signup.country_unsupported", "Self-service sign-up is not available in this country.");

    public static readonly DomainError PlanUnavailable = new("signup.plan_unavailable", "This plan cannot be chosen in this country.");
}

/// <summary>
/// Inscription en libre-service d'un restaurant : compte du propriétaire (en attente de confirmation de son email),
/// restaurant dans la devise et le fuseau de son pays, propriétaire actif, essai du plan choisi. Tout ce qui peut être
/// refusé l'est AVANT la première écriture ; la connexion n'est possible qu'une fois l'email confirmé.
/// </summary>
internal sealed class SignUpFlow(
    IQueryHandler<ListPlansQuery, IReadOnlyList<PlanDto>> plans,
    ICommandHandler<RegisterUserCommand, Guid> register,
    ICommandHandler<UpdateMyProfileCommand> profile,
    ICommandHandler<CreateTenantCommand, Guid> createTenant,
    ICommandHandler<CloseTenantCommand> closeTenant,
    ICommandHandler<OnboardTenantOwnerCommand, Guid> onboardOwner,
    ICommandHandler<AdminStartSubscriptionCommand, Guid> startSubscription,
    ILogger<SignUpFlow> logger)
{
    public static IReadOnlyList<SignUpCountry> Countries { get; } =
    [
        new("FR", "France", "EUR", "Europe/Paris"),
        new("BJ", "Bénin", "XOF", "Africa/Porto-Novo"),
        new("BF", "Burkina Faso", "XOF", "Africa/Ouagadougou"),
        new("CI", "Côte d’Ivoire", "XOF", "Africa/Abidjan"),
        new("GW", "Guinée-Bissau", "XOF", "Africa/Bissau"),
        new("ML", "Mali", "XOF", "Africa/Bamako"),
        new("NE", "Niger", "XOF", "Africa/Niamey"),
        new("SN", "Sénégal", "XOF", "Africa/Dakar"),
        new("TG", "Togo", "XOF", "Africa/Lome"),
    ];

    public static SignUpCountry? FindCountry(string? code) =>
        Countries.FirstOrDefault(country => string.Equals(country.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Les plans de base actifs et payants des devises des pays ouverts à l'inscription (essai sur le plan choisi).</summary>
    public async Task<IReadOnlyList<SignUpPlan>> PlansAsync(CancellationToken cancellationToken)
    {
        var currencies = Countries.Select(country => country.CurrencyCode).ToHashSet(StringComparer.Ordinal);
        return (await plans.HandleAsync(new ListPlansQuery(ActiveOnly: true), cancellationToken))
            .Where(plan => plan.Kind == PlanKind.Base && plan.Status == PlanStatus.Active && plan.Price > 0m && currencies.Contains(plan.CurrencyCode))
            .OrderBy(plan => plan.CurrencyCode, StringComparer.Ordinal)
            .ThenBy(plan => plan.BillingInterval)
            .ThenBy(plan => plan.Price)
            .Select(plan => new SignUpPlan(plan.Id, plan.Name, plan.Price, plan.CurrencyCode, plan.BillingInterval, plan.Entitlements))
            .ToList();
    }

    public async Task HandleAsync(SignUpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        DomainException.ThrowIf(!request.AcceptTerms, SignUpErrors.TermsNotAccepted);
        var country = FindCountry(request.Country) ?? throw new DomainException(SignUpErrors.CountryUnsupported);
        var name = TenantName.Create(request.RestaurantName);
        DomainException.ThrowIf(
            request.FirstName?.Trim().Length > User.NameMaxLength || request.LastName?.Trim().Length > User.NameMaxLength,
            UserErrors.NameTooLong);
        DomainException.ThrowIf(
            !(await PlansAsync(cancellationToken)).Any(plan => plan.Id == request.PlanId && plan.CurrencyCode == country.CurrencyCode),
            SignUpErrors.PlanUnavailable);

        // Email, mot de passe et unicité du compte sont contrôlés ici, avant toute autre écriture.
        var userId = await register.HandleAsync(new RegisterUserCommand(request.Email, request.Password), cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.FirstName) || !string.IsNullOrWhiteSpace(request.LastName))
        {
            await profile.HandleAsync(new UpdateMyProfileCommand(userId, request.FirstName, request.LastName, null, null), cancellationToken);
        }

        var tenantId = await createTenant.HandleAsync(
            new CreateTenantCommand(name.Value, country.CurrencyCode, country.TimeZoneId), cancellationToken);
        try
        {
            await onboardOwner.HandleAsync(new OnboardTenantOwnerCommand(tenantId, request.Email), cancellationToken);
            await startSubscription.HandleAsync(new AdminStartSubscriptionCommand(tenantId, request.PlanId), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Jamais l'email ni le nom : l'id du restaurant suffit pour retrouver l'inscription.
            logger.LogError("Sign-up of tenant {TenantId} failed after its creation ({Error}); the tenant is closed.", tenantId, exception.GetType().Name);
            try
            {
                await closeTenant.HandleAsync(new CloseTenantCommand(tenantId), CancellationToken.None);
            }
            catch (Exception closing) when (closing is not OperationCanceledException)
            {
                logger.LogError("Tenant {TenantId} could not be closed after a failed sign-up ({Error}).", tenantId, closing.GetType().Name);
            }

            throw;
        }
    }
}

/// <summary>
/// <c>GET /public/sign-up</c> : pays, plans et durée d'essai proposés à l'inscription. <c>POST /public/sign-up</c> :
/// l'inscription elle-même, anonyme et limitée en débit comme les autres routes de compte ; 202 sans contenu sensible,
/// le lien de confirmation part par email.
/// </summary>
internal static class SignUpEndpoints
{
    public static RouteGroupBuilder MapSignUpEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/public/sign-up").WithTags("Sign-up").AllowAnonymous();

        group.MapGet("/", async (SignUpFlow flow, BillingSettings settings, CancellationToken ct) =>
            TypedResults.Ok(new SignUpOptionsResponse(SignUpFlow.Countries, await flow.PlansAsync(ct), (int)settings.TrialLength.TotalDays)))
            .RequireRateLimiting(PublicMenuEndpoints.MediaRateLimitPolicy);

        group.MapPost("/", async (SignUpRequest request, SignUpFlow flow, BillingSettings settings, CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(request.Website))
            {
                await flow.HandleAsync(request, ct);
            }

            return TypedResults.Accepted((string?)null, new SignUpResponse((int)settings.TrialLength.TotalDays));
        })
        .RequireRateLimiting(IdentityRateLimiting.AuthPolicy);

        return group;
    }
}
