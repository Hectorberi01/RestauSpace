namespace Customers.Domain;

public readonly record struct CustomerNoteId
{
    private CustomerNoteId(Guid value) => Value = value;

    public Guid Value { get; }

    public static CustomerNoteId New() => new(Guid.CreateVersion7());

    public static CustomerNoteId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new CustomerNoteId(value);
    }

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Note interne de l'équipe sur un client (préférences, incidents, habitudes), datée et signée. Donnée personnelle :
/// effacée avec la fiche à l'anonymisation, reprise par la fiche qui absorbe un doublon. Jamais modifiée : on la supprime
/// et on en écrit une autre.
/// </summary>
public sealed class CustomerNote : Entity<CustomerNoteId>, ITenantOwned
{
    public const int TextMaxLength = 1000;

    private CustomerNote(CustomerNoteId id, TenantId tenantId, CustomerId customerId, string text, UserId? authorId, DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        CustomerId = customerId;
        Text = text;
        AuthorId = authorId;
        CreatedAtUtc = createdAtUtc;
    }

    // Réservé à l'ORM.
    private CustomerNote()
    {
        Text = string.Empty;
    }

    public TenantId TenantId { get; private set; }

    public CustomerId CustomerId { get; private set; }

    public string Text { get; private set; }

    /// <summary>L'utilisateur qui l'a écrite ; null si inconnu (import).</summary>
    public UserId? AuthorId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static CustomerNote Write(CustomerReference customer, string? text, UserId? authorId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(customer.IsAnonymized, CustomerErrors.Anonymized);
        var trimmed = text?.Trim() ?? string.Empty;
        DomainException.ThrowIf(trimmed.Length is 0 or > TextMaxLength, CustomerNoteErrors.TextInvalid);
        return new CustomerNote(CustomerNoteId.New(), customer.TenantId, customer.CustomerId, trimmed, authorId, clock.UtcNow);
    }

    /// <summary>La fiche qui absorbe un doublon reprend ses notes.</summary>
    public void MoveTo(CustomerReference customer)
    {
        ArgumentNullException.ThrowIfNull(customer);
        this.EnsureSameTenant(customer.TenantId);
        DomainException.ThrowIf(customer.IsAnonymized, CustomerErrors.Anonymized);
        CustomerId = customer.CustomerId;
    }
}

public static class CustomerNoteErrors
{
    public static readonly DomainError TextInvalid = new("customers.note.text_invalid", "A note has 1 to 1000 characters.");
    public static readonly DomainError NotFound = new("customers.note.not_found", "The note does not exist.");
}
