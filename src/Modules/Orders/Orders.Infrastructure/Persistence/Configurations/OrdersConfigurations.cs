using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Orders.Domain;

namespace Orders.Infrastructure.Persistence.Configurations;

internal static class OrdersConverters
{
    public static readonly ValueConverter<TenantId, Guid> TenantKey = new(id => id.Value, value => TenantId.From(value));
    public static readonly ValueConverter<LocationId, Guid> LocationKey = new(id => id.Value, value => LocationId.From(value));
    public static readonly ValueConverter<OrderId, Guid> OrderKey = new(id => id.Value, value => OrderId.From(value));
    public static readonly ValueConverter<OrderItemId, Guid> ItemKey = new(id => id.Value, value => OrderItemId.From(value));
    public static readonly ValueConverter<ProductId, Guid> ProductKey = new(id => id.Value, value => ProductId.From(value));
    public static readonly ValueConverter<VariantId, Guid> VariantKey = new(id => id.Value, value => VariantId.From(value));
    public static readonly ValueConverter<CustomerId, Guid> CustomerKey = new(id => id.Value, value => CustomerId.From(value));
    public static readonly ValueConverter<TableSessionId, Guid> SessionKey = new(id => id.Value, value => TableSessionId.From(value));
    public static readonly ValueConverter<Currency, string> CurrencyCode = new(currency => currency.Code, value => Currency.FromCode(value));

    public static readonly ValueConverter<List<OrderItemModifier>, string> Modifiers =
        new(modifiers => OrdersJson.WriteModifiers(modifiers), json => OrdersJson.ReadModifiers(json));

    public static readonly ValueConverter<List<OrderItemComponent>, string> Components =
        new(components => OrdersJson.WriteComponents(components), json => OrdersJson.ReadComponents(json));

    public static readonly ValueConverter<List<OrderDiscount>, string> Discounts =
        new(discounts => OrdersJson.WriteDiscounts(discounts), json => OrdersJson.ReadDiscounts(json));

    /// <summary>Une liste jsonb est modifiée sur place par le domaine : on la compare élément par élément, dans l'ordre.</summary>
    public static ValueComparer<List<T>> ListComparer<T>()
        where T : ValueObject => new(
        (left, right) => left!.SequenceEqual(right!),
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
        list => list.ToList());

    /// <summary>
    /// Montant + devise en deux colonnes. 19 chiffres dont 3 décimales : couvre <see cref="Money.MaxAbsoluteAmount"/>
    /// et les devises à 3 décimales.
    /// </summary>
    public static void MapMoney(ComplexPropertyBuilder<Money> money, string column)
    {
        money.Property(m => m.Amount).HasColumnName(column).HasPrecision(19, 3);
        money.Property(m => m.Currency)
            .HasColumnName(column + "_currency")
            .HasConversion(CurrencyCode)
            .HasMaxLength(3)
            .IsFixedLength();
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public const string OwnerProperty = "OrderId";
    public const string LocationNumberIndex = "ix_orders_tenant_id_location_id_number";
    public const string PlatformReferenceIndex = "ix_orders_tenant_id_platform_external_reference";

    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).HasColumnName("id").HasConversion(OrdersConverters.OrderKey).ValueGeneratedNever();
        builder.Property(order => order.TenantId).HasColumnName("tenant_id").HasConversion(OrdersConverters.TenantKey);
        builder.Property(order => order.LocationId).HasColumnName("location_id").HasConversion(OrdersConverters.LocationKey);

        builder.Property(order => order.Number)
            .HasColumnName("number")
            .HasConversion(number => number.Value, value => OrderNumber.Create(value))
            .HasMaxLength(OrderNumber.MaxLength);
        // Numéro unique par établissement : garde-fou final derrière le compteur.
        builder.HasIndex(order => new { order.TenantId, order.LocationId, order.Number }).IsUnique().HasDatabaseName(LocationNumberIndex);
        // Listes : les plus récentes d'abord, par tenant.
        builder.HasIndex(order => new { order.TenantId, order.CreatedAtUtc }).HasDatabaseName("ix_orders_tenant_id_created_at_utc");
        builder.HasIndex(order => new { order.TenantId, order.Channel, order.CreatedAtUtc }).HasDatabaseName("ix_orders_tenant_id_channel_created_at_utc");
        // Libération d'une table : commandes ouvertes d'une session.
        builder.HasIndex(order => new { order.TenantId, order.TableSessionId })
            .HasFilter("table_session_id IS NOT NULL")
            .HasDatabaseName("ix_orders_tenant_id_table_session_id");

        // Numériques : les valeurs des enums sont explicites et stables.
        builder.Property(order => order.Type).HasColumnName("type");
        builder.Property(order => order.Status).HasColumnName("status");
        builder.Property(order => order.PriceTaxMode).HasColumnName("price_tax_mode");
        builder.Property(order => order.Currency)
            .HasColumnName("currency")
            .HasConversion(OrdersConverters.CurrencyCode)
            .HasMaxLength(3)
            .IsFixedLength();
        builder.Property(order => order.CustomerId).HasColumnName("customer_id").HasConversion(OrdersConverters.CustomerKey);
        builder.Property(order => order.TableSessionId).HasColumnName("table_session_id").HasConversion(OrdersConverters.SessionKey);
        builder.Property(order => order.Channel).HasColumnName("channel").HasDefaultValue(OrderChannel.Counter).HasSentinel(OrderChannel.Counter);
        builder.Property(order => order.SourceAppId).HasColumnName("source_app_id");
        builder.Property(order => order.Platform).HasColumnName("platform").HasMaxLength(OrderOrigin.MaxPlatformLength);
        builder.Property(order => order.ExternalReference).HasColumnName("external_reference").HasMaxLength(OrderOrigin.MaxExternalReferenceLength);
        // Une commande de plateforme n'est saisie qu'une fois (double saisie, intégrateur qui rejoue).
        builder.HasIndex(order => new { order.TenantId, order.Platform, order.ExternalReference })
            .IsUnique()
            .HasFilter("external_reference IS NOT NULL")
            .HasDatabaseName(PlatformReferenceIndex);
        builder.Property(order => order.ScheduledForUtc).HasColumnName("scheduled_for_utc");

        // Totaux stockés : vérité historique de la commande, toujours recalculés par l'agrégat.
        builder.ComplexProperty(order => order.SubTotal, money => OrdersConverters.MapMoney(money, "sub_total"));
        builder.ComplexProperty(order => order.DiscountTotal, money => OrdersConverters.MapMoney(money, "discount_total"));
        builder.ComplexProperty(order => order.TaxTotal, money => OrdersConverters.MapMoney(money, "tax_total"));
        builder.ComplexProperty(order => order.Total, money => OrdersConverters.MapMoney(money, "total"));

        builder.Property<decimal?>("_deliveryFeeAmount").HasColumnName("delivery_fee_amount").HasPrecision(19, 3);
        builder.Property(order => order.DeliveryFeeReference)
            .HasColumnName("delivery_fee_reference")
            .HasMaxLength(Order.MaxDeliveryFeeReferenceLength);

        builder.Property<List<OrderDiscount>>("_discounts")
            .HasColumnName("discounts")
            .HasColumnType("jsonb")
            .HasConversion(OrdersConverters.Discounts, OrdersConverters.ListComparer<OrderDiscount>());

        builder.Property(order => order.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(order => order.PlacedAtUtc).HasColumnName("placed_at_utc");
        builder.Property(order => order.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(order => order.CancelledAtUtc).HasColumnName("cancelled_at_utc");
        builder.Property(order => order.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(Order.MaxCancellationReasonLength);

        // Concurrence optimiste sur la colonne système xmin de PostgreSQL.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasMany(order => order.Items)
            .WithOne()
            .HasForeignKey(OwnerProperty)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(order => order.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(order => order.DomainEvents);
        builder.Ignore(order => order.Discounts);
        builder.Ignore(order => order.NetTotal);
        builder.Ignore(order => order.DeliveryFee);
        builder.Ignore(order => order.IsFinalized);
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public const string PositionProperty = "Position";

    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items");

        builder.HasKey(item => item.Id);
        // Jamais généré : une ligne atteinte via sa commande avec un id déjà défini est une NOUVELLE ligne.
        builder.Property(item => item.Id).HasColumnName("id").HasConversion(OrdersConverters.ItemKey).ValueGeneratedNever();
        builder.Property<OrderId>(OrderConfiguration.OwnerProperty).HasColumnName("order_id").HasConversion(OrdersConverters.OrderKey);
        // Ordre d'ajout (celui de DescribeLines), posé par l'unit of work.
        builder.Property<int>(PositionProperty).HasColumnName("position");

        builder.Property(item => item.ProductId).HasColumnName("product_id").HasConversion(OrdersConverters.ProductKey);
        builder.Property(item => item.ProductName).HasColumnName("product_name").HasMaxLength(ProductSnapshot.MaxNameLength);
        builder.Property(item => item.VariantId).HasColumnName("variant_id").HasConversion(OrdersConverters.VariantKey);
        builder.Property(item => item.VariantName).HasColumnName("variant_name").HasMaxLength(ProductSnapshot.MaxVariantNameLength);
        builder.Property(item => item.Note).HasColumnName("note").HasMaxLength(OrderItem.MaxNoteLength);
        builder.Property(item => item.Quantity)
            .HasColumnName("quantity")
            .HasConversion(quantity => quantity.Value, value => Quantity.Create(value));

        builder.ComplexProperty(item => item.UnitPrice, money => OrdersConverters.MapMoney(money, "unit_price"));
        builder.ComplexProperty(item => item.DiscountAmount, money => OrdersConverters.MapMoney(money, "discount_amount"));
        builder.ComplexProperty(item => item.NetAmount, money => OrdersConverters.MapMoney(money, "net_amount"));
        builder.ComplexProperty(item => item.TaxAmount, money => OrdersConverters.MapMoney(money, "tax_amount"));

        // Taxe figée à l'ajout de la ligne.
        builder.ComplexProperty(item => item.Tax, tax =>
        {
            tax.Property(t => t.CategoryCode).HasColumnName("tax_category_code").HasMaxLength(TaxSnapshot.MaxCategoryCodeLength);
            tax.Property(t => t.CategoryName).HasColumnName("tax_category_name").HasMaxLength(TaxSnapshot.MaxCategoryNameLength);
            tax.Property(t => t.Rate).HasColumnName("tax_rate").HasPrecision(7, 6);
        });

        builder.Property<List<OrderItemModifier>>("_modifiers")
            .HasColumnName("modifiers")
            .HasColumnType("jsonb")
            .HasConversion(OrdersConverters.Modifiers, OrdersConverters.ListComparer<OrderItemModifier>());

        // Formule : les produits choisis, figés à l'ajout de la ligne.
        builder.Property<List<OrderItemComponent>>("_components")
            .HasColumnName("components")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .HasConversion(OrdersConverters.Components, OrdersConverters.ListComparer<OrderItemComponent>());

        builder.Ignore(item => item.Modifiers);
        builder.Ignore(item => item.Components);
        builder.Ignore(item => item.UnitPriceWithModifiers);
        builder.Ignore(item => item.LineTotal);
        builder.Ignore(item => item.TaxableAmount);
        builder.Ignore(item => item.TotalWithTax);
    }
}

internal sealed class OrderNumberCounterConfiguration : IEntityTypeConfiguration<OrderNumberCounter>
{
    public void Configure(EntityTypeBuilder<OrderNumberCounter> builder)
    {
        builder.ToTable(OrderNumberCounter.Table);
        builder.HasKey(counter => new { counter.TenantId, counter.LocationId, counter.Day });
        builder.Property(counter => counter.TenantId).HasColumnName("tenant_id").HasConversion(OrdersConverters.TenantKey);
        builder.Property(counter => counter.LocationId).HasColumnName("location_id").HasConversion(OrdersConverters.LocationKey);
        builder.Property(counter => counter.Day).HasColumnName("day");
        builder.Property(counter => counter.LastValue).HasColumnName("last_value");
    }
}

internal sealed class OrderPaymentSettlementConfiguration : IEntityTypeConfiguration<OrderPaymentSettlement>
{
    public const string OrderIndex = "ix_order_payment_settlements_order";

    public void Configure(EntityTypeBuilder<OrderPaymentSettlement> builder)
    {
        builder.ToTable(OrderPaymentSettlement.Table, table =>
            table.HasCheckConstraint("ck_order_payment_settlements_amount", "settled_amount >= 0"));
        builder.HasKey(settlement => new { settlement.TenantId, settlement.PaymentId });
        builder.Property(settlement => settlement.TenantId).HasColumnName("tenant_id").HasConversion(OrdersConverters.TenantKey);
        builder.Property(settlement => settlement.PaymentId).HasColumnName("payment_id");
        builder.Property(settlement => settlement.OrderId).HasColumnName("order_id").HasConversion(OrdersConverters.OrderKey);
        builder.Property(settlement => settlement.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3);
        builder.Property(settlement => settlement.SettledAmount).HasColumnName("settled_amount").HasPrecision(19, 3);
        builder.Property(settlement => settlement.Version).HasColumnName("version");
        builder.HasIndex(settlement => new { settlement.TenantId, settlement.OrderId }).HasDatabaseName(OrderIndex);
    }
}
