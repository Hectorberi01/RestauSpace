using System.Reflection;

namespace SaasRestaurant.ArchitectureTests;

/// <summary>Règles DDD du prompt vérifiables mécaniquement sur chaque assembly Domain.</summary>
public sealed class DomainModelTests
{
    /// <summary>Agrégats de la plateforme, pas d'un tenant. En ajouter un ici est une décision d'architecture.</summary>
    private static readonly string[] PlatformAggregates =
    [
        "Identity.Domain.Users.User",
        "Tenants.Domain.Tenants.Tenant",
        "Tenants.Domain.Prospects.DemoRequest",
        "Billing.Domain.Plan",
        "Payments.Domain.PaymentProvider",
        "Notifications.Domain.NotificationProvider",
    ];

    private static readonly Type[] MutableCollections =
        [typeof(List<>), typeof(IList<>), typeof(ICollection<>), typeof(HashSet<>), typeof(ISet<>), typeof(Dictionary<,>), typeof(IDictionary<,>)];

    private static IEnumerable<Type> ConcreteTypes(string module) =>
        Solution.Load(module, "Domain").GetTypes().Where(type => type is { IsClass: true, IsAbstract: false });

    private static bool IsEntity(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Entity<>))
            {
                return true;
            }
        }

        return false;
    }

    [Theory]
    [MemberData(nameof(Solution.AllModules), MemberType = typeof(Solution))]
    public void Every_aggregate_carries_its_tenant(string module)
    {
        // ITenantOwned, ou un TenantId nullable pour les agrégats partagés entre un tenant et la plateforme (Notifications).
        var withoutTenant = ConcreteTypes(module)
            .Where(type => typeof(IAggregateRoot).IsAssignableFrom(type))
            .Where(type => !typeof(ITenantOwned).IsAssignableFrom(type))
            .Where(type => type.GetProperty("TenantId") is null)
            .Select(type => type.FullName ?? type.Name)
            .Where(name => !PlatformAggregates.Contains(name))
            .ToList();

        Assert.Empty(withoutTenant);
    }

    [Theory]
    [MemberData(nameof(Solution.AllModules), MemberType = typeof(Solution))]
    public void Entities_have_no_public_setter(string module)
    {
        var settable = ConcreteTypes(module)
            .Where(IsEntity)
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.SetMethod is { IsPublic: true })
                .Select(property => $"{type.Name}.{property.Name}"))
            .ToList();

        Assert.Empty(settable);
    }

    [Theory]
    [MemberData(nameof(Solution.AllModules), MemberType = typeof(Solution))]
    public void Entities_expose_no_mutable_collection(string module)
    {
        var mutable = ConcreteTypes(module)
            .Where(IsEntity)
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.PropertyType.IsGenericType
                    && MutableCollections.Contains(property.PropertyType.GetGenericTypeDefinition()))
                .Select(property => $"{type.Name}.{property.Name}"))
            .ToList();

        Assert.Empty(mutable);
    }

    [Theory]
    [MemberData(nameof(Solution.AllModules), MemberType = typeof(Solution))]
    public void Domain_events_are_immutable_records(string module)
    {
        var mutableEvents = ConcreteTypes(module)
            .Where(type => typeof(IDomainEvent).IsAssignableFrom(type))
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.SetMethod is { IsPublic: true } setter
                    && !setter.ReturnParameter.GetRequiredCustomModifiers().Any(modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit"))
                .Select(property => $"{type.Name}.{property.Name}"))
            .ToList();

        Assert.Empty(mutableEvents);
    }
}
