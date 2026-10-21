namespace Delivery.Domain;

/// <summary>
/// Zone de livraison d'un établissement (un quartier, une commune) : frais facturés, montant minimum de commande et
/// délai estimé. Ses règles sont vérifiées par <see cref="DeliverySettings"/>, qui seule la modifie.
/// </summary>
public sealed class DeliveryZone : Entity<DeliveryZoneId>
{
    public const int NameMaxLength = 80;
    public const int MinMinutes = 5;
    public const int MaxMinutes = 240;

    internal DeliveryZone(DeliveryZoneId id, string name, Money fee, Money minimumOrder, int estimatedMinutes) : base(id)
    {
        Name = name;
        Fee = fee;
        MinimumOrder = minimumOrder;
        EstimatedMinutes = estimatedMinutes;
        IsActive = true;
    }

    // Réservé à l'ORM.
    private DeliveryZone()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public Money Fee { get; private set; } = null!;

    public Money MinimumOrder { get; private set; } = null!;

    public int EstimatedMinutes { get; private set; }

    public bool IsActive { get; private set; }

    internal void Change(string name, Money fee, Money minimumOrder, int estimatedMinutes)
    {
        Name = name;
        Fee = fee;
        MinimumOrder = minimumOrder;
        EstimatedMinutes = estimatedMinutes;
    }

    internal void SetActive(bool active) => IsActive = active;
}
