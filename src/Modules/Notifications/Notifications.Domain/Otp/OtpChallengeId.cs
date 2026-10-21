namespace Notifications.Domain;

public readonly record struct OtpChallengeId
{
    private OtpChallengeId(Guid value) => Value = value;

    public Guid Value { get; }

    public static OtpChallengeId New() => new(Guid.CreateVersion7());

    public static OtpChallengeId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new OtpChallengeId(value);
    }

    public override string ToString() => Value.ToString();
}
