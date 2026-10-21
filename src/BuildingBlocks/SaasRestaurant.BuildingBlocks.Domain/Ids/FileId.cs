namespace SaasRestaurant.BuildingBlocks.Domain;

public readonly record struct FileId
{
    private FileId(Guid value) => Value = value;

    public Guid Value { get; }

    public static FileId New() => new(Guid.CreateVersion7());

    public static FileId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new FileId(value);
    }

    public override string ToString() => Value.ToString();
}
