namespace Audit.Domain;

public enum ActorType
{
    User = 1,
    ApiCredential = 2,
    System = 3,
}

/// <summary>
/// Référence opaque à l'auteur de l'action. Locale au module Audit pour que des acteurs non humains
/// (tâche de fond, identifiant d'intégration) puissent être enregistrés sans dépendre d'un autre module.
/// </summary>
public sealed class ActorId : ValueObject
{
    public const int MaxValueLength = 100;

    private ActorId(ActorType type, string value)
    {
        Type = type;
        Value = value;
    }

    public ActorType Type { get; }

    public string Value { get; }

    public static ActorId ForUser(UserId userId)
    {
        Guard.NotEmpty(userId);
        return new ActorId(ActorType.User, userId.Value.ToString());
    }

    public static ActorId ForSystem(string? componentName) => Create(ActorType.System, componentName);

    public static ActorId Create(ActorType type, string? value)
    {
        DomainException.ThrowIf(!Enum.IsDefined(type), AuditErrors.ActorTypeInvalid);
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), AuditErrors.ActorValueInvalid);

        var trimmed = value!.Trim();
        DomainException.ThrowIf(
            trimmed.Length > MaxValueLength || trimmed.Any(AuditText.IsLineBreakingOrControl),
            AuditErrors.ActorValueInvalid);

        if (type != ActorType.User)
        {
            return new ActorId(type, trimmed);
        }

        // Même règle et même texte canonique que ForUser : un acteur utilisateur est toujours un UserId non vide.
        DomainException.ThrowIf(!Guid.TryParse(trimmed, out var userId), AuditErrors.ActorValueInvalid);
        return ForUser(UserId.From(userId));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Type;
        yield return Value;
    }

    public override string ToString() => $"{Type}:{Value}";
}
