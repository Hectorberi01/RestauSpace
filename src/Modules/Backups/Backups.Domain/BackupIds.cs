namespace Backups.Domain;

public readonly record struct BackupId
{
    private BackupId(Guid value) => Value = value;

    public Guid Value { get; }

    public static BackupId New() => new(Guid.CreateVersion7());

    public static BackupId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new BackupId(value);
    }

    public override string ToString() => Value.ToString();
}

public readonly record struct RestoreId
{
    private RestoreId(Guid value) => Value = value;

    public Guid Value { get; }

    public static RestoreId New() => new(Guid.CreateVersion7());

    public static RestoreId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new RestoreId(value);
    }

    public override string ToString() => Value.ToString();
}
