namespace SaasRestaurant.Contracts;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class IntegrationEventAttribute(string module, string type, int version = 1) : Attribute
{
    public string Module { get; } = module;

    public string Type { get; } = type;

    public int Version { get; } = version;

    public bool MatchesPrefix => Type.EndsWith('*');

    public bool Matches(string module, string type) =>
        string.Equals(Module, module, StringComparison.Ordinal)
        && (MatchesPrefix ? type.StartsWith(Type[..^1], StringComparison.Ordinal) : string.Equals(Type, type, StringComparison.Ordinal));
}

public interface IIntegrationEvent
{
    Guid TenantId { get; }
}

public interface ILocatedIntegrationEvent : IIntegrationEvent
{
    Guid LocationId { get; }
}
