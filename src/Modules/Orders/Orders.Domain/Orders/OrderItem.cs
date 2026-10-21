namespace Orders.Domain;

/// <summary>
/// Un produit commandé, figé tel qu'il était à l'ajout. Seul <see cref="Order"/> peut le modifier ; la part de
/// remise et la taxe de la ligne sont attribuées par la commande à chaque recalcul.
/// </summary>
public sealed class OrderItem : Entity<OrderItemId>
{
    public const int MaxModifiers = 50;
    public const int MaxNoteLength = 200;

    // Pas readonly : l'ORM le pose au chargement (stocké en une seule valeur jsonb). Jamais réassigné par le domaine.
    private List<OrderItemModifier> _modifiers = [];

    // Pas readonly : l'ORM le pose au chargement (jsonb). Fixé à la création ; vide pour un produit ordinaire.
    private List<OrderItemComponent> _components = [];

    private OrderItem(OrderItemId id, ProductSnapshot product, Quantity quantity, string? note)
        : base(id)
    {
        ProductId = product.ProductId;
        ProductName = product.Name;
        VariantId = product.VariantId;
        VariantName = product.VariantName;
        Note = note;
        UnitPrice = product.UnitPrice;
        Tax = product.Tax;
        Quantity = quantity;
        DiscountAmount = Money.Zero(product.UnitPrice.Currency);
        NetAmount = Money.Zero(product.UnitPrice.Currency);
        TaxAmount = Money.Zero(product.UnitPrice.Currency);
    }

    // Réservé à l'ORM.
    private OrderItem()
    {
        ProductName = null!;
        UnitPrice = null!;
        Tax = null!;
        Quantity = null!;
        DiscountAmount = null!;
        NetAmount = null!;
        TaxAmount = null!;
    }

    public ProductId ProductId { get; private set; }

    public string ProductName { get; private set; }

    /// <summary>La variante vendue, si le produit en a ; son nom est présent si et seulement si elle l'est.</summary>
    public VariantId? VariantId { get; private set; }

    public string? VariantName { get; private set; }

    /// <summary>Texte libre pour la cuisine (« sans oignon », « bien cuit »). Ne change rien au prix.</summary>
    public string? Note { get; private set; }

    /// <summary>Prix d'une unité avant modificateurs (variante incluse), dans le mode de taxe de la commande.</summary>
    public Money UnitPrice { get; private set; }

    public Quantity Quantity { get; private set; }

    public TaxSnapshot Tax { get; private set; }

    public IReadOnlyCollection<OrderItemModifier> Modifiers => _modifiers.AsReadOnly();

    /// <summary>Produits choisis dans une formule, avec leurs suppléments et options ; vide pour un produit ordinaire.</summary>
    public IReadOnlyCollection<OrderItemComponent> Components => _components.AsReadOnly();

    /// <summary>Prix unitaire, options de la ligne, suppléments et options des composants d'une formule compris.</summary>
    public Money UnitPriceWithModifiers =>
        _components.Aggregate(_modifiers.Aggregate(UnitPrice, (price, modifier) => price + modifier.PriceDelta), (price, component) => price + component.PriceDelta);

    /// <summary>(UnitPrice + somme des deltas de modificateurs) x Quantity. Avant remise, au mode de taxe de la commande.</summary>
    public Money LineTotal => Money.Of(UnitPriceWithModifiers.Amount * Quantity.Value, UnitPrice.Currency);

    /// <summary>Part de la remise de la commande attribuée à cette ligne.</summary>
    public Money DiscountAmount { get; private set; }

    /// <summary>
    /// LineTotal - DiscountAmount : le montant sur lequel la taxe est calculée. Commande hors taxe : la base nette à
    /// laquelle la taxe s'ajoute ; commande TTC : le montant brut qui contient la taxe.
    /// </summary>
    public Money TaxableAmount => LineTotal - DiscountAmount;

    /// <summary>Montant remisé de la ligne hors taxe, dans les deux modes.</summary>
    public Money NetAmount { get; private set; }

    public Money TaxAmount { get; private set; }

    /// <summary>Montant remisé de la ligne TTC, dans les deux modes : NetAmount + TaxAmount, exactement.</summary>
    public Money TotalWithTax => NetAmount + TaxAmount;

    internal static OrderItem Create(ProductSnapshot product, Quantity quantity, Currency orderCurrency, string? note)
    {
        DomainException.ThrowIf(product.UnitPrice.Currency != orderCurrency, OrderErrors.CurrencyMismatch);

        var item = new OrderItem(OrderItemId.New(), product, quantity, NormalizeNote(note));
        item._components = [.. product.Components];
        EnsureUnitPriceInRange(item.UnitPriceWithModifiers);
        foreach (var modifier in product.Modifiers)
        {
            item.AddModifier(modifier);
        }

        return item;
    }

    /// <summary>Note sans espaces superflus, null si vide.</summary>
    internal static string? NormalizeNote(string? note)
    {
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        DomainException.ThrowIf(trimmed?.Length > MaxNoteLength, OrderErrors.ItemNoteTooLong);
        return trimmed;
    }

    internal void ChangeQuantity(Quantity quantity) => Quantity = quantity;

    internal void ChangeNote(string? normalizedNote) => Note = normalizedNote;

    internal void AddModifier(OrderItemModifier modifier)
    {
        DomainException.ThrowIf(modifier.PriceDelta.Currency != UnitPrice.Currency, OrderErrors.CurrencyMismatch);
        DomainException.ThrowIf(_modifiers.Any(existing => existing.ModifierId == modifier.ModifierId), OrderErrors.ItemModifierDuplicate);
        DomainException.ThrowIf(_modifiers.Count + (_components.FirstOrDefault()?.Modifiers.Count ?? 0) >= MaxModifiers, OrderErrors.ItemTooManyModifiers);
        EnsureUnitPriceInRange(UnitPriceWithModifiers + modifier.PriceDelta);
        _modifiers.Add(modifier);
    }

    internal void RemoveModifier(ModifierId modifierId)
    {
        var modifier = _modifiers.FirstOrDefault(existing => existing.ModifierId == modifierId);
        DomainException.ThrowIf(modifier is null, OrderErrors.ItemModifierNotFound);
        // Retirer un supplément peut laisser un delta négatif non couvert ; retirer une réduction, dépasser le plafond.
        EnsureUnitPriceInRange(UnitPriceWithModifiers - modifier!.PriceDelta);
        _modifiers.Remove(modifier);
    }

    /// <summary>
    /// Ce que la cuisine et le stock voient de la ligne : la ligne elle-même, ou pour une formule chacun de ses produits
    /// choisis (même quantité, id du composant), avec leurs options ; les options de la formule vont au premier.
    /// </summary>
    internal IEnumerable<OrderLineView> Describe()
    {
        if (_components.Count == 0)
        {
            yield return new OrderLineView(
                Id,
                ProductId,
                VariantId,
                ProductName,
                VariantName,
                Quantity.Value,
                Array.AsReadOnly(_modifiers.Select(modifier => new OrderLineModifierView(modifier.ModifierId, modifier.Name)).ToArray()),
                Note);
            yield break;
        }

        for (var index = 0; index < _components.Count; index++)
        {
            var component = _components[index];
            var modifiers = (index == 0 ? _modifiers.Concat(component.Modifiers) : component.Modifiers)
                .Select(modifier => new OrderLineModifierView(modifier.ModifierId, modifier.Name))
                .ToArray();
            yield return new OrderLineView(
                OrderItemId.From(component.Id),
                component.ProductId,
                component.VariantId,
                component.ProductName,
                component.VariantName,
                Quantity.Value,
                Array.AsReadOnly(modifiers),
                Note,
                ProductName);
        }
    }

    /// <summary>Ce qu'une mutation de la commande peut changer sur une ligne, pour annuler une mutation échouée.</summary>
    internal Memento Capture() => new(Quantity, [.. _modifiers], DiscountAmount, NetAmount, TaxAmount);

    internal void Restore(Memento memento)
    {
        Quantity = memento.Quantity;
        _modifiers.Clear();
        _modifiers.AddRange(memento.Modifiers);
        DiscountAmount = memento.DiscountAmount;
        NetAmount = memento.NetAmount;
        TaxAmount = memento.TaxAmount;
    }

    internal void ApplyPricing(Money discountShare, PriceTaxMode mode)
    {
        DiscountAmount = discountShare;
        if (mode == PriceTaxMode.TaxInclusive)
        {
            // Le net découle de la taxe arrondie, jamais arrondi seul : net + taxe redonne le brut à l'unité près.
            TaxAmount = Tax.TaxIncludedIn(TaxableAmount);
            NetAmount = TaxableAmount - TaxAmount;
        }
        else
        {
            TaxAmount = Tax.TaxOn(TaxableAmount);
            NetAmount = TaxableAmount;
        }
    }

    private static void EnsureUnitPriceInRange(Money unitPriceWithModifiers)
    {
        DomainException.ThrowIf(unitPriceWithModifiers.IsNegative, OrderErrors.ItemUnitPriceNegative);
        DomainException.ThrowIf(
            unitPriceWithModifiers.Amount > ProductSnapshot.MaxUnitPriceAmount,
            OrderErrors.ItemUnitPriceTooLarge);
    }

    internal sealed record Memento(
        Quantity Quantity,
        OrderItemModifier[] Modifiers,
        Money DiscountAmount,
        Money NetAmount,
        Money TaxAmount);
}
