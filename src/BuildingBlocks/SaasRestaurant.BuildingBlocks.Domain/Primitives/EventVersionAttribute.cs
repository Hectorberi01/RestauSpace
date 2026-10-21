namespace SaasRestaurant.BuildingBlocks.Domain;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EventVersionAttribute(int version) : Attribute
{
    public int Version { get; } = version;
}
