namespace Catalog.Domain;

/// <summary>Une option d'un groupe de modificateurs. <see cref="PriceDelta"/> est un supplément (jamais négatif).</summary>
public sealed class Modifier : Entity<ModifierId>
{
    private Modifier(ModifierId id, CatalogName name, Money priceDelta) : base(id)
    {
        Name = name;
        PriceDelta = priceDelta;
        IsAvailable = true;
    }

    // Réservé à l'ORM.
    private Modifier()
    {
        Name = null!;
        PriceDelta = null!;
    }

    public CatalogName Name { get; private set; }

    public Money PriceDelta { get; private set; }

    public bool IsAvailable { get; private set; }

    internal static Modifier Create(CatalogName name, Money priceDelta) => new(ModifierId.New(), name, priceDelta);

    internal void Rename(CatalogName name) => Name = name;

    internal void ChangePrice(Money priceDelta) => PriceDelta = priceDelta;

    internal void SetAvailability(bool isAvailable) => IsAvailable = isAvailable;
}
