namespace Kitchen.Domain;

/// <summary>
/// Une ligne de commande à préparer, copiée depuis la commande quand elle arrive en cuisine : quoi cuisiner (nom,
/// variante, options choisies, note libre), combien et où. Les bornes sont celles d'une ligne Orders.
/// </summary>
public sealed class KitchenOrderLine : ValueObject
{
    public const int MaxNameLength = 200;
    public const int MaxVariantNameLength = 120;
    public const int MaxModifierNameLength = 120;
    public const int MaxModifiers = 50;
    public const int MaxNoteLength = 200;

    private readonly string[] _modifiers;

    private KitchenOrderLine(
        OrderItemId orderItemId,
        string name,
        int quantity,
        KitchenStationReference station,
        string? variantName,
        string[] modifiers,
        string? note,
        KitchenCourse course)
    {
        OrderItemId = orderItemId;
        Name = name;
        Quantity = quantity;
        Station = station;
        VariantName = variantName;
        _modifiers = modifiers;
        Note = note;
        Course = course;
    }

    public OrderItemId OrderItemId { get; }
    public string Name { get; }
    public int Quantity { get; }
    public KitchenStationReference Station { get; }
    public string? VariantName { get; }
    /// <summary>Noms des options choisies, dans l'ordre de la ligne de commande.</summary>
    public IReadOnlyList<string> Modifiers => Array.AsReadOnly(_modifiers);

    /// <summary>Texte libre du client ou du serveur ("sans oignon", "allergie arachide").</summary>
    public string? Note { get; }

    public KitchenCourse Course { get; }

    /// <param name="variantName">Facultatif (vide = aucune).</param>
    /// <param name="modifiers">Noms des options, éventuellement vide ; copié.</param>
    /// <param name="note">Facultative (vide = aucune).</param>
    /// <param name="course">Service du plat ; <see cref="KitchenCourse.Direct"/> par défaut.</param>
    public static KitchenOrderLine Create(
        OrderItemId orderItemId,
        string? name,
        int quantity,
        KitchenStationReference station,
        string? variantName,
        IEnumerable<string> modifiers,
        string? note,
        KitchenCourse course = KitchenCourse.Direct)
    {
        ArgumentNullException.ThrowIfNull(station);
        ArgumentNullException.ThrowIfNull(modifiers);
        Guard.NotEmpty(orderItemId);
        DomainException.ThrowIf(!Enum.IsDefined(course), KitchenTicketErrors.CourseUnknown);
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(name), KitchenTicketErrors.LineNameEmpty);

        var trimmed = name!.Trim();
        DomainException.ThrowIf(trimmed.Length > MaxNameLength, KitchenTicketErrors.LineNameTooLong);
        DomainException.ThrowIf(quantity <= 0, KitchenTicketErrors.LineQuantityNotPositive);

        var normalizedVariant = string.IsNullOrWhiteSpace(variantName) ? null : variantName.Trim();
        DomainException.ThrowIf(normalizedVariant?.Length > MaxVariantNameLength, KitchenTicketErrors.LineVariantNameTooLong);

        var modifierNames = new List<string>();
        foreach (var modifier in modifiers)
        {
            ArgumentNullException.ThrowIfNull(modifier, nameof(modifiers));
            DomainException.ThrowIf(string.IsNullOrWhiteSpace(modifier), KitchenTicketErrors.LineModifierNameEmpty);

            var modifierName = modifier.Trim();
            DomainException.ThrowIf(modifierName.Length > MaxModifierNameLength, KitchenTicketErrors.LineModifierNameTooLong);
            DomainException.ThrowIf(modifierNames.Count >= MaxModifiers, KitchenTicketErrors.LineTooManyModifiers);
            modifierNames.Add(modifierName);
        }

        var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        DomainException.ThrowIf(normalizedNote?.Length > MaxNoteLength, KitchenTicketErrors.LineNoteTooLong);

        return new KitchenOrderLine(orderItemId, trimmed, quantity, station, normalizedVariant, [.. modifierNames], normalizedNote, course);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return OrderItemId;
        yield return Name;
        yield return Quantity;
        yield return Station;
        yield return VariantName;
        yield return Note;
        yield return Course;
        yield return _modifiers.Length;
        foreach (var modifier in _modifiers)
        {
            yield return modifier;
        }
    }
}
