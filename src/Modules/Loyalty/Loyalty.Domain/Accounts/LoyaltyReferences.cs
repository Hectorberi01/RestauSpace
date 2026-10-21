namespace Loyalty.Domain;

/// <summary>
/// Le client pour lequel un compte est ouvert, tel que chargé depuis le module Customers sous isolation par tenant.
/// Construit par la couche Application (agrégat d'un autre module) : ses ids sont vérifiés ici et ne peuvent plus être
/// remplacés ensuite.
/// </summary>
public sealed record CustomerReference(TenantId TenantId, CustomerId CustomerId, bool IsAnonymized)
{
    public TenantId TenantId { get; } = Guard.NotEmpty(TenantId);

    public CustomerId CustomerId { get; } = Guard.NotEmpty(CustomerId);
}

/// <summary>
/// Ce qu'un compte doit savoir d'une récompense pour l'échanger. Obtenable uniquement à partir d'une vraie récompense
/// via <see cref="Reward.ToReference"/> : le constructeur est internal et rien ne peut être modifié avec <c>with</c>,
/// donc un appelant ne peut pas forger un coût, une valeur de remise ni un indicateur « actif ».
/// </summary>
public sealed record RewardReference
{
    internal RewardReference(TenantId tenantId, RewardId rewardId, Points pointCost, Money discountValue, bool isActive)
    {
        TenantId = tenantId;
        RewardId = rewardId;
        PointCost = pointCost;
        DiscountValue = discountValue;
        IsActive = isActive;
    }

    public TenantId TenantId { get; }

    public RewardId RewardId { get; }

    public Points PointCost { get; }

    public Money DiscountValue { get; }

    public bool IsActive { get; }

    /// <summary>
    /// Référence à porter sur la remise de commande créée à partir de cette récompense : l'id de la récompense en
    /// chaîne (36 caractères, dans la limite des 64 caractères qu'accepte une référence de remise de commande).
    /// </summary>
    public string DiscountReference => RewardId.ToString();
}
