namespace Notifications.Domain;

public readonly record struct TemplateId
{
    private TemplateId(Guid value) => Value = value;

    public Guid Value { get; }

    public static TemplateId New() => new(Guid.CreateVersion7());

    public static TemplateId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new TemplateId(value);
    }

    public override string ToString() => Value.ToString();
}
