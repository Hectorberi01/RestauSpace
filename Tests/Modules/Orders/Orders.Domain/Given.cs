namespace Orders.Domain;

/// <summary>Builders partagés des tests Orders.</summary>
internal static class Given
{
    public static readonly TenantId Tenant = TenantId.New();
    public static readonly LocationId Location = LocationId.New();
    public static readonly LocationReference LocationRef = new(Tenant, Location, IsOperational: true);

    public static readonly TaxSnapshot Vat20 = TaxSnapshot.Create("VAT20", "TVA 20 %", 0.20m);
    public static readonly TaxSnapshot Vat10 = TaxSnapshot.Create("VAT10", "TVA 10 %", 0.10m);
    public static readonly TaxSnapshot Vat18 = TaxSnapshot.Create("VAT18", "TVA 18 %", 0.18m);
    public static readonly TaxSnapshot NoTax = TaxSnapshot.Create("EXEMPT", "Exonere", 0m);

    public static Money Eur(decimal amount) => Money.Of(amount, Currency.EUR);

    public static Money Xof(decimal amount) => Money.Of(amount, Currency.XOF);

    public static Quantity Qty(int value) => Quantity.Create(value);

    public static Order DraftOrder(
        Currency? currency = null,
        OrderType type = OrderType.Takeaway,
        IClock? clock = null,
        TableSessionId? tableSessionId = null,
        CustomerId? customerId = null,
        PriceTaxMode mode = PriceTaxMode.TaxExclusive)
    {
        var order = Order.Create(
            Tenant, LocationRef, OrderNumber.Create("A-0042"), type, currency ?? Currency.EUR, mode, clock ?? new FakeClock(),
            customerId is { } customer ? Customer(customer) : null,
            tableSessionId is { } session ? OpenSession(session) : null);
        return order;
    }

    /// <summary>Un client connu, non anonymisé, du tenant de test.</summary>
    public static CustomerReference Customer(CustomerId? id = null, TenantId? tenant = null, bool anonymized = false) =>
        new(tenant ?? Tenant, id ?? CustomerId.New(), anonymized);

    /// <summary>Une session de table ouverte du tenant et de l'établissement de test, sauf indication contraire.</summary>
    public static TableSessionReference OpenSession(
        TableSessionId? id = null, TenantId? tenant = null, LocationId? location = null, bool open = true) =>
        new(tenant ?? Tenant, id ?? TableSessionId.New(), location ?? Location, open);

    /// <summary>La commande de départ des tests de création ; chaque test remplace la seule référence qu'il vise.</summary>
    public static Order Create(
        TenantId? tenant = null,
        LocationReference? location = null,
        OrderType type = OrderType.DineIn,
        CustomerReference? customer = null,
        TableSessionReference? tableSession = null,
        IClock? clock = null) =>
        Order.Create(
            tenant ?? Tenant, location ?? LocationRef, OrderNumber.Create("A-0042"), type, Currency.EUR,
            PriceTaxMode.TaxInclusive, clock ?? new FakeClock(), customer, tableSession);

    public static Order InclusiveOrder(Currency? currency = null) => DraftOrder(currency, mode: PriceTaxMode.TaxInclusive);

    /// <summary>Le règlement que Payments signalerait : exactement le total, sauf si <paramref name="paid"/> est fourni.</summary>
    public static OrderSettlement Settlement(Order order, Money? paid = null) =>
        OrderSettlement.Create(order.TenantId, order.Id, paid ?? order.Total);

    public static ProductSnapshot Product(
        Money unitPrice,
        TaxSnapshot? tax = null,
        string name = "Burger",
        TenantId? tenant = null,
        params OrderItemModifier[] modifiers) =>
        ProductSnapshot.Create(tenant ?? Tenant, ProductId.New(), name, unitPrice, tax ?? Vat20, modifiers);

    /// <summary>Un produit vendu dans l'une de ses variantes ; le prix unitaire inclut déjà la variante.</summary>
    public static ProductSnapshot VariantProduct(Money unitPrice, VariantId variantId, string variantName, string name = "Pizza") =>
        ProductSnapshot.Create(Tenant, ProductId.New(), name, unitPrice, Vat20, null, variantId, variantName);

    public static OrderItemModifier Modifier(string name, Money priceDelta) =>
        OrderItemModifier.Create(ModifierId.New(), name, priceDelta);

    public static OrderDiscount Discount(Money amount, string reference = "WELCOME10", DiscountSource source = DiscountSource.Coupon) =>
        OrderDiscount.Create(amount, source, reference, "Test discount");

    /// <summary>Une commande d'une ligne de 10.00 EUR à 20 % (total 12.00), amenée jusqu'au statut demandé.</summary>
    public static Order OrderIn(OrderStatus status, IClock? clock = null)
    {
        clock ??= new FakeClock();
        var order = DraftOrder(clock: clock);
        order.AddItem(Product(Eur(10.00m)), Qty(1));
        if (status == OrderStatus.Cancelled)
        {
            order.Place(clock);
            order.Cancel("Customer changed their mind", clock);
            return order;
        }

        Action[] steps =
        [
            () => order.Place(clock),
            () => order.Accept(clock),
            () => order.StartPreparation(clock),
            () => order.MarkReady(clock),
            () => order.Complete(Settlement(order), clock),
        ];
        foreach (var step in steps.Take((int)status - (int)OrderStatus.Draft))
        {
            step();
        }

        Assert.Equal(status, order.Status);
        return order;
    }
}
