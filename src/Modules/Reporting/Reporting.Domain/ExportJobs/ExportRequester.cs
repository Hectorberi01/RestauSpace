namespace Reporting.Domain;

public enum ExportRequesterKind
{
    User = 1,
    ApiCredential = 2,
}

/// <summary>
/// Qui demande un export (ou planifie un rapport) : un membre du personnel, ou une clé d'API du restaurant. Une
/// référence opaque : un id, jamais un nom ni une clé.
/// </summary>
public sealed record ExportRequester
{
    private ExportRequester(ExportRequesterKind kind, Guid id)
    {
        Kind = kind;
        Id = id;
    }

    public ExportRequesterKind Kind { get; }

    public Guid Id { get; }

    /// <summary>L'utilisateur qui a fait la demande ; null pour une clé d'API.</summary>
    public UserId? User => Kind == ExportRequesterKind.User ? UserId.From(Id) : null;

    public static ExportRequester ForUser(UserId userId)
    {
        Guard.NotEmpty(userId);
        return new ExportRequester(ExportRequesterKind.User, userId.Value);
    }

    public static ExportRequester ForApiCredential(Guid credentialId)
    {
        DomainException.ThrowIf(credentialId == Guid.Empty, CommonErrors.EmptyId);
        return new ExportRequester(ExportRequesterKind.ApiCredential, credentialId);
    }

    public static ExportRequester From(ExportRequesterKind kind, Guid id) => kind switch
    {
        ExportRequesterKind.User => ForUser(UserId.From(id)),
        ExportRequesterKind.ApiCredential => ForApiCredential(id),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
