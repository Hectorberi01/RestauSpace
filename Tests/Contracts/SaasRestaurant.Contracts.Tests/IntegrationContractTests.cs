using System.Reflection;
using System.Runtime.CompilerServices;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace SaasRestaurant.Contracts;

public sealed class IntegrationContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Contracts
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var contract in typeof(IIntegrationEvent).Assembly.GetTypes()
                .Where(type => type.GetCustomAttribute<IntegrationEventAttribute>() is not null)
                .Select(type => type.FullName!)
                .Order(StringComparer.Ordinal))
            {
                data.Add(contract);
            }

            return data;
        }
    }

    private static Type ContractType(string name) => typeof(IIntegrationEvent).Assembly.GetType(name, throwOnError: true)!;

    private static IReadOnlyList<Type> DomainEventsOf(IntegrationEventAttribute contract)
    {
        var module = char.ToUpperInvariant(contract.Module[0]) + contract.Module[1..];
        return Assembly.Load(new AssemblyName($"{module}.Domain")).GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IDomainEvent).IsAssignableFrom(type))
            .Where(type => contract.Matches(contract.Module, type.Name))
            .ToList();
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void Every_contract_reads_fields_its_domain_events_carry_with_a_compatible_type(string name)
    {
        var contract = ContractType(name);
        var attribute = IntegrationEventReader.ContractOf(contract);
        var events = DomainEventsOf(attribute);

        Assert.NotEmpty(events);
        foreach (var domainEvent in events)
        {
            foreach (var parameter in contract.GetConstructors().Single().GetParameters())
            {
                var property = domainEvent.GetProperty(parameter.Name!);
                Assert.True(property is not null, $"{domainEvent.Name} has no {parameter.Name} for {contract.Name}.");
                Assert.True(
                    Compatible(parameter.ParameterType, property!.PropertyType),
                    $"{domainEvent.Name}.{parameter.Name} ({property.PropertyType.Name}) cannot be read as {parameter.ParameterType.Name}.");
                Assert.False(
                    Nullable.GetUnderlyingType(property.PropertyType) is not null && Nullable.GetUnderlyingType(parameter.ParameterType) is null
                        && parameter.ParameterType.IsValueType,
                    $"{domainEvent.Name}.{parameter.Name} is optional but {contract.Name} requires it.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void Every_contract_reads_the_outbox_json_of_its_domain_events(string name)
    {
        var contract = ContractType(name);
        var attribute = IntegrationEventReader.ContractOf(contract);
        var read = typeof(IntegrationEventReader).GetMethod(nameof(IntegrationEventReader.Read))!.MakeGenericMethod(contract);

        foreach (var domainEvent in DomainEventsOf(attribute))
        {
            var sample = (IDomainEvent)Sample(domainEvent);
            var message = OutboxMessage.From(sample);

            var value = read.Invoke(null, [attribute.Module, domainEvent.Name, message.SchemaVersion, message.Content]);

            Assert.IsAssignableFrom<IIntegrationEvent>(value);
        }
    }

    [Fact]
    public void A_newer_version_of_an_event_is_refused_rather_than_misread()
    {
        var refused = Assert.Throws<IntegrationContractException>(() => IntegrationEventReader.Read<Customers.CustomerAnonymized>(
            "customers", "CustomerAnonymized", 2, $$"""{"TenantId":"{{Guid.NewGuid()}}","CustomerId":"{{Guid.NewGuid()}}"}"""));

        Assert.Contains("version 2", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_required_field_is_refused()
    {
        Assert.Throws<IntegrationContractException>(() => IntegrationEventReader.Read<Customers.CustomerAnonymized>(
            "customers", "CustomerAnonymized", 1, $$"""{"TenantId":"{{Guid.NewGuid()}}"}"""));
    }

    private static bool Compatible(Type contract, Type domain)
    {
        contract = Nullable.GetUnderlyingType(contract) ?? contract;
        domain = Nullable.GetUnderlyingType(domain) ?? domain;
        if (contract == domain)
        {
            return true;
        }

        if (contract == typeof(MoneyAmount))
        {
            return domain.Name == "Money";
        }

        if (contract == typeof(string) && domain.IsEnum)
        {
            return true;
        }

        var value = domain.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
        if (value is not null && Compatible(contract, value.PropertyType))
        {
            return true;
        }

        return contract == typeof(decimal) && (domain == typeof(int) || domain == typeof(long));
    }

    private static object Sample(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            return Sample(underlying);
        }

        if (type == typeof(string))
        {
            return "sample";
        }

        if (type == typeof(Guid))
        {
            return Guid.NewGuid();
        }

        if (type == typeof(int) || type == typeof(long) || type == typeof(decimal) || type == typeof(short))
        {
            return Convert.ChangeType(1, type, System.Globalization.CultureInfo.InvariantCulture);
        }

        if (type == typeof(bool))
        {
            return true;
        }

        if (type == typeof(DateTimeOffset))
        {
            return Now;
        }

        if (type == typeof(TimeSpan))
        {
            return TimeSpan.FromHours(1);
        }

        if (type == typeof(Uri))
        {
            return new Uri("https://example.com/");
        }

        if (type.IsEnum)
        {
            return Enum.GetValues(type).GetValue(0)!;
        }

        if (type == typeof(Money))
        {
            return Money.Of(1m, Currency.FromCode("EUR"));
        }

        if (type == typeof(Period))
        {
            return Period.Create(Now, Now.AddHours(1));
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() is var definition
            && (definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>) || definition == typeof(IEnumerable<>)))
        {
            return Array.CreateInstance(type.GetGenericArguments()[0], 0);
        }

        if (typeof(IDomainEvent).IsAssignableFrom(type))
        {
            var constructor = type.GetConstructors().OrderByDescending(candidate => candidate.GetParameters().Length).First();
            return constructor.Invoke(constructor.GetParameters().Select(parameter => Sample(parameter.ParameterType)).ToArray());
        }

        foreach (var name in new[] { "New", "From", "Create", "Of" })
        {
            foreach (var factory in type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name == name && method.ReturnType == type)
                .OrderBy(method => method.GetParameters().Length))
            {
                try
                {
                    return factory.Invoke(null, factory.GetParameters().Select(parameter => Sample(parameter.ParameterType)).ToArray())!;
                }
                catch (TargetInvocationException)
                {
                }
            }
        }

        return RuntimeHelpers.GetUninitializedObject(type);
    }
}
