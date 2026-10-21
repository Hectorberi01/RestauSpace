using Identity.Application.Abstractions;
using Identity.Application.Commands;
using Identity.Domain.Users;
using Locations.Application;
using Locations.Application.Commands;
using SaasRestaurant.BuildingBlocks.Application;
using Staff.Application.Commands;
using Staff.Application.Queries;
using Tenants.Application.Commands;

namespace SaasRestaurant.Host;

/// <summary>Comptes de test, en développement seulement (DevSeed:Enabled).</summary>
internal sealed record DevSeedSettings(
    string AdminEmail,
    string OwnerEmail,
    string Password,
    string RestaurantName,
    string CurrencyCode,
    string TimeZoneId,
    string CountryCode,
    string City);

/// <summary>
/// Crée, s'ils n'existent pas, un administrateur de la plateforme et un restaurant actif avec son propriétaire et un
/// établissement. Comptes déjà vérifiés. Rejouable : ce qui existe est laissé tel quel.
/// </summary>
internal static class DevelopmentSeed
{
    public static async Task SeedDevelopmentAsync(this WebApplication app)
    {
        var section = app.Configuration.GetSection("DevSeed");
        if (!app.Environment.IsDevelopment() || !section.GetValue("Enabled", false))
        {
            return;
        }

        var settings = new DevSeedSettings(
            section["AdminEmail"] ?? "admin@example.com",
            section["OwnerEmail"] ?? "restaurateur@example.com",
            section["Password"] ?? "RestauSpace2026!",
            section["RestaurantName"] ?? "Restaurant de test",
            section["CurrencyCode"] ?? "XOF",
            section["TimeZoneId"] ?? "Africa/Porto-Novo",
            section["CountryCode"] ?? "BJ",
            section["City"] ?? "Cotonou");
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SaasRestaurant.DevSeed");

        await EnsureUserAsync(app.Services, settings.AdminEmail, settings.Password);
        var (ownerId, ownerCreated) = await EnsureUserAsync(app.Services, settings.OwnerEmail, settings.Password);
        if (ownerCreated || !await HasRestaurantAsync(app.Services, ownerId))
        {
            await CreateRestaurantAsync(app.Services, settings);
            logger.LogInformation("Jeu de test : restaurant « {Restaurant} » créé.", settings.RestaurantName);
        }

        logger.LogInformation(
            "Jeu de test prêt : administrateur {Admin}, restaurateur {Owner} (mot de passe DevSeed:Password).",
            settings.AdminEmail,
            settings.OwnerEmail);
    }

    /// <summary>Le compte, créé et vérifié s'il le faut. Renvoie son id et s'il vient d'être créé.</summary>
    private static async Task<(Guid Id, bool Created)> EnsureUserAsync(IServiceProvider services, string email, string password)
    {
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var created = false;
        var user = await users.FindByEmailAsync(Email.Create(email));
        if (user is null)
        {
            await scope.ServiceProvider.GetRequiredService<ICommandHandler<RegisterUserCommand, Guid>>()
                .HandleAsync(new RegisterUserCommand(email, password));
            user = await users.FindByEmailAsync(Email.Create(email))
                ?? throw new InvalidOperationException("Seed account was not created.");
            created = true;
        }

        if (user.Status == UserStatus.Pending)
        {
            user.VerifyEmail(scope.ServiceProvider.GetRequiredService<IClock>());
            await scope.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync();
        }

        return (user.Id.Value, created);
    }

    private static async Task<bool> HasRestaurantAsync(IServiceProvider services, Guid userId)
    {
        await using var scope = services.CreateAsyncScope();
        var memberships = await scope.ServiceProvider.GetRequiredService<IQueryHandler<ListMyMembershipsQuery, IReadOnlyList<MembershipDto>>>()
            .HandleAsync(new ListMyMembershipsQuery(userId));
        return memberships.Count > 0;
    }

    private static async Task CreateRestaurantAsync(IServiceProvider services, DevSeedSettings settings)
    {
        Guid tenantId;
        await using (var scope = services.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider;
            tenantId = await provider.GetRequiredService<ICommandHandler<CreateTenantCommand, Guid>>()
                .HandleAsync(new CreateTenantCommand(settings.RestaurantName, settings.CurrencyCode, settings.TimeZoneId));
            await provider.GetRequiredService<ICommandHandler<ActivateTenantCommand>>().HandleAsync(new ActivateTenantCommand(tenantId));
            await provider.GetRequiredService<ICommandHandler<OnboardTenantOwnerCommand, Guid>>()
                .HandleAsync(new OnboardTenantOwnerCommand(tenantId, settings.OwnerEmail));
        }

        // L'établissement se crée pour le restaurant : son propre scope, avec le tenant fixé.
        await using (var scope = services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(tenantId));
            await scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateLocationCommand, Guid>>().HandleAsync(
                new CreateLocationCommand(
                    "Établissement principal",
                    new AddressInput("1 rue du Test", settings.City, null, settings.CountryCode),
                    Seats: 40));
        }
    }
}
