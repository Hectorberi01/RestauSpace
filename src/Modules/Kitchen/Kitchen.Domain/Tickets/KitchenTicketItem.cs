namespace Kitchen.Domain;

/// <summary>Une ligne à préparer à un poste. Son statut ne fait qu'avancer : Queued -> Preparing -> Ready.</summary>
public sealed class KitchenTicketItem : Entity<KitchenTicketItemId>
{
    // Pas readonly : l'ORM le pose au chargement (stocké en une seule valeur jsonb). Jamais réassigné par le domaine.
    private List<string> _modifiers = [];

    private KitchenTicketItem(KitchenTicketItemId id, KitchenOrderLine line, bool held) : base(id)
    {
        Course = line.Course;
        IsHeld = held;
        OrderItemId = line.OrderItemId;
        Name = line.Name;
        VariantName = line.VariantName;
        _modifiers.AddRange(line.Modifiers);
        Note = line.Note;
        Quantity = line.Quantity;
        StationId = line.Station.Id;
        Status = KitchenTicketItemStatus.Queued;
    }

    // Réservé à l'ORM.
    private KitchenTicketItem() => Name = null!;

    public OrderItemId OrderItemId { get; private set; }

    public string Name { get; private set; }

    public string? VariantName { get; private set; }

    /// <summary>Noms des options choisies, dans l'ordre de la ligne de commande.</summary>
    public IReadOnlyList<string> Modifiers => _modifiers.AsReadOnly();

    /// <summary>Texte libre du client ou du serveur, tel qu'écrit sur la ligne de commande.</summary>
    public string? Note { get; private set; }

    public int Quantity { get; private set; }

    public StationId StationId { get; private set; }

    public KitchenTicketItemStatus Status { get; private set; }

    public bool IsReady => Status == KitchenTicketItemStatus.Ready;

    public KitchenCourse Course { get; private set; } = KitchenCourse.Direct;

    /// <summary>Le plat attend que la salle envoie son service ; il ne peut pas encore être préparé.</summary>
    public bool IsHeld { get; private set; }

    internal static KitchenTicketItem Create(KitchenOrderLine line, bool held) => new(KitchenTicketItemId.New(), line, held);

    internal void Fire() => IsHeld = false;

    internal void Start()
    {
        DomainException.ThrowIf(IsHeld, KitchenTicketErrors.ItemHeld);
        DomainException.ThrowIf(Status != KitchenTicketItemStatus.Queued, KitchenTicketErrors.ItemNotQueued);
        Status = KitchenTicketItemStatus.Preparing;
    }

    internal void MarkReady()
    {
        DomainException.ThrowIf(Status != KitchenTicketItemStatus.Preparing, KitchenTicketErrors.ItemNotPreparing);
        Status = KitchenTicketItemStatus.Ready;
    }
}
