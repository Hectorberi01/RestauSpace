using Identity.Application.Abstractions;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace Identity.Infrastructure;

/// <summary>Vrais ports, vrai PostgreSQL (Docker), migrations appliquées. Seules l'horloge et la boîte mail sont simulées.</summary>
public sealed class IdentityDatabase : IAsyncLifetime, IIdentityNotifier
{
    public const string Issuer = "identity-tests";
    public const string Audience = "saasrestaurant";
    public static readonly string SigningKey = Convert.ToBase64String(new byte[32].Select((_, index) => (byte)(index + 1)).ToArray());

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private ServiceProvider _services = null!;

    public FakeClock Clock { get; } = new();

    public List<(string Kind, string Email, string Token)> Sent { get; } = [];

    public string LastTokenSentTo(string email) => Sent.Last(sent => sent.Email == email).Token;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddIdentityInfrastructure(
                _container.GetConnectionString(),
                options =>
                {
                    options.Issuer = Issuer;
                    options.Audience = Audience;
                    options.SigningKey = SigningKey;
                },
                twoFactor => twoFactor.SecretKey = Convert.ToBase64String(Enumerable.Range(0, 32).Select(index => (byte)(index * 3)).ToArray()))
            .AddSingleton<IClock>(Clock)
            .AddSingleton<IIdentityNotifier>(this)
            .BuildServiceProvider(validateScopes: true);

        await using var scope = NewScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    }

    /// <summary>Une seconde composition indépendante sur la même base, pour les tests qui ont besoin de services en plus.</summary>
    public ServiceProvider NewProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddIdentityInfrastructure(_container.GetConnectionString(), options =>
            {
                options.Issuer = Issuer;
                options.Audience = Audience;
                options.SigningKey = SigningKey;
            })
            .AddSingleton<IClock>(Clock);
        configure(services);
        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>Un scope = une requête.</summary>
    public AsyncServiceScope NewScope() => _services.CreateAsyncScope();

    public T Get<T>() where T : notnull => _services.GetRequiredService<T>();

    public Task SendEmailVerificationAsync(Email email, string token, CancellationToken cancellationToken = default) =>
        Record("verification", email, token);

    public Task SendInvitationAsync(Email email, string token, CancellationToken cancellationToken = default) =>
        Record("invitation", email, token);

    public Task SendPasswordResetAsync(Email email, string token, CancellationToken cancellationToken = default) =>
        Record("reset", email, token);

    private Task Record(string kind, Email email, string token)
    {
        lock (Sent)
        {
            Sent.Add((kind, email.Value, token));
        }

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _container.DisposeAsync();
    }
}
