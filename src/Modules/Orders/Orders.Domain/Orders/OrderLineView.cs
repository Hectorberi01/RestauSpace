namespace Orders.Domain;

/// <summary>
/// Ce que les modules en aval peuvent savoir d'une ligne de commande (ticket cuisine, consommation de stock) :
/// identité et libellés, pas de prix. Obtenable uniquement via <see cref="Order.DescribeLines"/>, elle décrit donc
/// toujours une vraie ligne ; chaque texte respecte déjà les limites imposées par la commande.
/// </summary>
public sealed record OrderLineView
{
    internal OrderLineView(
        OrderItemId orderItemId,
        ProductId productId,
        VariantId? variantId,
        string productName,
        string? variantName,
        int quantity,
        IReadOnlyList<OrderLineModifierView> modifiers,
        string? note,
        string? formulaName = null)
    {
        FormulaName = formulaName;
        OrderItemId = orderItemId;
        ProductId = productId;
        VariantId = variantId;
        ProductName = productName;
        VariantName = variantName;
        Quantity = quantity;
        Modifiers = modifiers;
        Note = note;
    }

    public OrderItemId OrderItemId { get; }

    public ProductId ProductId { get; }

    public VariantId? VariantId { get; }

    public string ProductName { get; }

    public string? VariantName { get; }

    public int Quantity { get; }

    /// <summary>Options choisies, dans l'ordre d'ajout. Au plus <see cref="OrderItem.MaxModifiers"/>.</summary>
    public IReadOnlyList<OrderLineModifierView> Modifiers { get; }

    /// <summary>Texte libre pour la cuisine, si le client en a donné un.</summary>
    public string? Note { get; }

    /// <summary>Pour un produit choisi dans une formule : le nom de la formule (la ligne vendue).</summary>
    public string? FormulaName { get; }
}

public sealed record OrderLineModifierView
{
    internal OrderLineModifierView(ModifierId modifierId, string name)
    {
        ModifierId = modifierId;
        Name = name;
    }

    public ModifierId ModifierId { get; }

    public string Name { get; }
}
