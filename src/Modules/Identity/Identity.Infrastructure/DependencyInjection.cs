using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Commands;
using Identity.Domain.Users;
using Identity.Infrastructure.Notifications;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Identity.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Enregistre tous les ports du module Identity sauf <see cref="IIdentityNotifier"/> (l'hôte choisit comment
    /// partent les emails) et <c>IClock</c>.
    /// </summary>
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        string connectionString,
        Action<AccessTokenOptions> configureAccessTokens,
        Action<TwoFactorOptions>? configureTwoFactor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(configureAccessTokens);

        var accessTokens = new AccessTokenOptions();
        configureAccessTokens(accessTokens);
        accessTokens.Validate();

        services.AddDbContext<IdentityDbContext>(options => options.UseIdentityNpgsql(connectionString));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IIdentityUnitOfWork, IdentityUnitOfWork>();
        services.AddScoped<IUserEmailUniquenessChecker, UserEmailUniquenessChecker>();
        services.AddScoped<IUserTokens, UserTokens>();
        services.AddScoped<IPasswordAttempts, PasswordAttempts>();
        services.AddScoped<ITwoFactorAttempts, TwoFactorAttempts>();
        services.AddScoped<ISessionStatus, SessionStatus>();
        services.AddScoped<PasswordVerifier>();
        services.AddScoped<TwoFactorVerifier>();
        services.AddScoped<SessionOpener>();
        services.AddScoped<IUserTraces, UserTraces>();
        services.AddScoped<IUserAvatars, UserAvatars>();
        services.AddScoped<IOutboxConsumer, SecurityNotificationConsumer>();
        services.AddOutboxDispatcher<IdentityDbContext>();
        services.AddSingleton<IdentityMaintenance>();
        services.AddHostedService(provider => provider.GetRequiredService<IdentityMaintenance>());
        services.AddSingleton<EmailQueue>();
        services.TryAddSingleton<ICompromisedPasswords, NoCompromisedPasswordCheck>();
        services.TryAddScoped<ITenantMemberships, NoTenantMemberships>();

        var twoFactor = new TwoFactorOptions();
        configureTwoFactor?.Invoke(twoFactor);
        services.AddSingleton<ITwoFactorSecrets>(new TwoFactorSecrets(twoFactor.KeyBytes()));
        services.AddSingleton<ITwoFactorChallenges>(TwoFactorChallenges.FromSigningKey(accessTokens.SigningKeyBytes()));

        services.AddSingleton(accessTokens);
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IRefreshTokens, RefreshTokens>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.TryAddSingleton(new IdentitySettings());
        return services;
    }

    /// <summary>De vrais emails, via un serveur SMTP.</summary>
    public static IServiceCollection AddIdentitySmtpEmails(
        this IServiceCollection services, Action<IdentityEmailOptions> configureEmails, Action<SmtpOptions> configureSmtp)
    {
        var smtp = new SmtpOptions();
        configureSmtp(smtp);
        smtp.Validate();

        services.AddSingleton(smtp);
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        return services.AddIdentityEmails(configureEmails);
    }

    /// <summary>DÉVELOPPEMENT UNIQUEMENT : les emails complets, liens compris, sont écrits dans le log.</summary>
    public static IServiceCollection AddIdentityLogEmails(
        this IServiceCollection services, Action<IdentityEmailOptions> configureEmails)
    {
        services.AddSingleton<IEmailSender, LogEmailSender>();
        return services.AddIdentityEmails(configureEmails);
    }

    /// <summary>
    /// Qui peut agir pour quel tenant, réponse fournie par l'hôte à partir des modules Staff et Tenants. Sans cela,
    /// personne ne peut sélectionner de restaurant.
    /// </summary>
    public static IServiceCollection AddIdentityTenantMemberships<TMemberships>(this IServiceCollection services)
        where TMemberships : class, ITenantMemberships
    {
        services.RemoveAll<ITenantMemberships>();
        services.AddScoped<ITenantMemberships, TMemberships>();
        return services;
    }

    /// <summary>Rejette les mots de passe issus de fuites connues (appels sortants vers api.pwnedpasswords.com).</summary>
    public static IServiceCollection AddIdentityCompromisedPasswordCheck(
        this IServiceCollection services, HttpMessageHandler? handler = null)
    {
        services.RemoveAll<ICompromisedPasswords>();
        services.AddSingleton<ICompromisedPasswords>(provider => new PwnedPasswordsClient(
            new HttpClient(handler ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) })
            {
                BaseAddress = PwnedPasswordsClient.DefaultBaseAddress,
                Timeout = TimeSpan.FromSeconds(2),
            },
            provider.GetRequiredService<ILogger<PwnedPasswordsClient>>()));
        return services;
    }

    private static IServiceCollection AddIdentityEmails(
        this IServiceCollection services, Action<IdentityEmailOptions> configureEmails)
    {
        var emails = new IdentityEmailOptions();
        configureEmails(emails);
        emails.Validate();

        services.AddSingleton(emails);
        services.AddSingleton<IIdentityNotifier, EmailIdentityNotifier>();
        services.AddHostedService<EmailDispatchService>();
        return services;
    }

    internal static DbContextOptionsBuilder UseIdentityNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.Schema));
}
