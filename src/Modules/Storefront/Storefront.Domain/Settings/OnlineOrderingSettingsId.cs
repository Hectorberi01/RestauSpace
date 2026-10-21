namespace Storefront.Domain;

public readonly record struct OnlineOrderingSettingsId
{
    private OnlineOrderingSettingsId(Guid value) => Value = value;

    public Guid Value { get; }

    public static OnlineOrderingSettingsId New() => new(Guid.CreateVersion7());

    public static OnlineOrderingSettingsId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new OnlineOrderingSettingsId(value);
    }

    public override string ToString() => Value.ToString();
}
