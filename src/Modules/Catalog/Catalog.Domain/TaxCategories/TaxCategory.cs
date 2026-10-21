namespace Catalog.Domain;

/// <summary>
/// Taxe appliquée aux produits qui la référencent : un code stable, un nom d'affichage et un taux.
/// Les commandes copient code, nom et taux dans chaque ligne à son ajout : rien ici ne réécrit jamais une vente passée.
/// </summary>
public sealed class TaxCategory : AggregateRoot<TaxCategoryId>, ITenantOwned
{
    private TaxCategory(TaxCategoryId id, TenantId tenantId, TaxCategoryCode code, TaxCategoryName name, TaxRate rate) : base(id)
    {
        TenantId = tenantId;
        Code = code;
        Name = name;
        Rate = rate;
        Status = TaxCategoryStatus.Active;
    }

    // Réservé à l'ORM.
    private TaxCategory()
    {
        Code = null!;
        Name = null!;
        Rate = null!;
    }

    public TenantId TenantId { get; private set; }

    /// <summary>Fixé à la création : il identifie la catégorie dans les snapshots de commande et les exports.</summary>
    public TaxCategoryCode Code { get; private set; }

    public TaxCategoryName Name { get; private set; }

    public TaxRate Rate { get; private set; }

    public TaxCategoryStatus Status { get; private set; }

    public bool IsArchived => Status == TaxCategoryStatus.Archived;

    /// <param name="isCodeUniqueInTenant">Réponse de <see cref="ITaxCategoryCodeUniquenessChecker"/>.</param>
    public static TaxCategory Create(
        TenantId tenantId,
        TaxCategoryCode code,
        TaxCategoryName name,
        TaxRate rate,
        bool isCodeUniqueInTenant,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(rate);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        DomainException.ThrowIf(!isCodeUniqueInTenant, TaxCategoryErrors.CodeNotUnique);

        var category = new TaxCategory(TaxCategoryId.New(), tenantId, code, name, rate);
        category.Raise(new TaxCategoryCreated(tenantId, category.Id, code.Value, name.Value, rate.Value, clock.UtcNow));
        return category;
    }

    public void Rename(TaxCategoryName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        EnsureNotArchived();

        if (name == Name)
        {
            return;
        }

        Name = name;
    }

    /// <summary>Ventes futures uniquement : les lignes déjà dans une commande gardent le taux qu'elles ont figé.</summary>
    public void ChangeRate(TaxRate rate, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(rate);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();

        if (rate == Rate)
        {
            return;
        }

        var previous = Rate;
        Rate = rate;
        Raise(new TaxCategoryRateChanged(TenantId, Id, Code.Value, previous.Value, rate.Value, clock.UtcNow));
    }

    /// <summary>
    /// Active -> Archived (terminal). Les produits qui utilisent déjà la catégorie la gardent et restent vendables
    /// avec son dernier taux ; elle ne peut plus être affectée à un nouveau produit.
    /// </summary>
    public void Archive(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();

        Status = TaxCategoryStatus.Archived;
        Raise(new TaxCategoryArchived(TenantId, Id, Code.Value, clock.UtcNow));
    }

    public TaxCategoryReference ToReference() => new(Id, TenantId, Code.Value, Name.Value, Rate.Value, IsArchived);

    private void EnsureNotArchived() =>
        DomainException.ThrowIf(IsArchived, TaxCategoryErrors.Archived);
}
