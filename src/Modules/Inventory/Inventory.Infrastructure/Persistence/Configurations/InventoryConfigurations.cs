using Inventory.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Inventory.Infrastructure.Persistence.Configurations;

internal static class InventoryConverters
{
    public static readonly ValueConverter<TenantId, Guid> TenantKey = new(id => id.Value, value => TenantId.From(value));
    public static readonly ValueConverter<LocationId, Guid> LocationKey = new(id => id.Value, value => LocationId.From(value));
    public static readonly ValueConverter<IngredientId, Guid> IngredientKey = new(id => id.Value, value => IngredientId.From(value));
    public static readonly ValueConverter<InventoryItemId, Guid> ItemKey = new(id => id.Value, value => InventoryItemId.From(value));
    public static readonly ValueConverter<StockMovementId, Guid> MovementKey = new(id => id.Value, value => StockMovementId.From(value));
    public static readonly ValueConverter<RecipeId, Guid> RecipeKey = new(id => id.Value, value => RecipeId.From(value));
    public static readonly ValueConverter<ProductId, Guid> ProductKey = new(id => id.Value, value => ProductId.From(value));
    public static readonly ValueConverter<VariantId, Guid> VariantKey = new(id => id.Value, value => VariantId.From(value));
    public static readonly ValueConverter<OrderId, Guid> OrderKey = new(id => id.Value, value => OrderId.From(value));

    /// <summary>
    /// Quantités : jusqu'à <see cref="Quantity.MaxAmount"/> (10 chiffres) avec <see cref="Quantity.MaxDecimals"/>
    /// décimales ; le stock courant, somme signée du registre, a de la marge au-delà.
    /// </summary>
    public const int Precision = 22;

    public static void MapQuantity<TOwner>(OwnedNavigationBuilder<TOwner, Quantity> quantity, string column)
        where TOwner : class
    {
        quantity.Property(q => q.Amount).HasColumnName(column).HasPrecision(Precision, Quantity.MaxDecimals);
        quantity.Property(q => q.Unit).HasColumnName(column + "_unit");
    }
}

internal sealed class IngredientConfiguration : IEntityTypeConfiguration<Ingredient>
{
    public const string NormalizedNameProperty = "NormalizedName";
    public const string NameIndex = "ix_ingredients_tenant_id_normalized_name";

    public void Configure(EntityTypeBuilder<Ingredient> builder)
    {
        builder.ToTable("ingredients", table => table.HasCheckConstraint("ck_ingredients_base_unit", "base_unit BETWEEN 1 AND 5"));

        builder.HasKey(ingredient => ingredient.Id);
        builder.Property(ingredient => ingredient.Id).HasColumnName("id").HasConversion(InventoryConverters.IngredientKey).ValueGeneratedNever();
        builder.Property(ingredient => ingredient.TenantId).HasColumnName("tenant_id").HasConversion(InventoryConverters.TenantKey);
        builder.Property(ingredient => ingredient.Name).HasColumnName("name").HasMaxLength(Ingredient.NameMaxLength);
        // Noms uniques par tenant quelle que soit la casse : une copie en majuscules porte l'index unique.
        builder.Property<string>(NormalizedNameProperty)
            .HasColumnName("normalized_name")
            .HasMaxLength(Ingredient.NameMaxLength)
            .HasComputedColumnSql("upper(name)", stored: true);
        builder.HasIndex(nameof(Ingredient.TenantId), NormalizedNameProperty).IsUnique().HasDatabaseName(NameIndex);
        builder.Property(ingredient => ingredient.BaseUnit).HasColumnName("base_unit");
        builder.Property(ingredient => ingredient.IsActive).HasColumnName("is_active");

        // Concurrence optimiste sur la colonne système xmin de PostgreSQL.
        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(ingredient => ingredient.DomainEvents);
    }
}

internal sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public const string LocationIngredientIndex = "ix_inventory_items_tenant_id_location_id_ingredient_id";

    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("inventory_items", table =>
        {
            table.HasCheckConstraint("ck_inventory_items_unit", "unit BETWEEN 1 AND 5");
            table.HasCheckConstraint("ck_inventory_items_negative_stock_policy", "negative_stock_policy BETWEEN 1 AND 2");
            table.HasCheckConstraint("ck_inventory_items_reorder_threshold", "reorder_threshold >= 0");
        });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id").HasConversion(InventoryConverters.ItemKey).ValueGeneratedNever();
        builder.Property(item => item.TenantId).HasColumnName("tenant_id").HasConversion(InventoryConverters.TenantKey);
        // Références par id vers un autre module (établissement) et vers un autre agrégat (ingrédient), sans clé étrangère.
        builder.Property(item => item.LocationId).HasColumnName("location_id").HasConversion(InventoryConverters.LocationKey);
        builder.Property(item => item.IngredientId).HasColumnName("ingredient_id").HasConversion(InventoryConverters.IngredientKey);
        // Un article par ingrédient et par établissement : garde-fou final contre deux créations concurrentes.
        builder.HasIndex(item => new { item.TenantId, item.LocationId, item.IngredientId }).IsUnique().HasDatabaseName(LocationIngredientIndex);

        builder.Property(item => item.Unit).HasColumnName("unit");
        builder.Property(item => item.CurrentQuantity)
            .HasColumnName("current_quantity")
            .HasPrecision(InventoryConverters.Precision, Quantity.MaxDecimals);
        builder.OwnsOne(item => item.ReorderThreshold, threshold => InventoryConverters.MapQuantity(threshold, "reorder_threshold"));
        builder.Navigation(item => item.ReorderThreshold).IsRequired();
        builder.Property(item => item.NegativeStockPolicy).HasColumnName("negative_stock_policy");
        builder.Property(item => item.AverageUnitCost)
            .HasColumnName("average_unit_cost")
            .HasPrecision(InventoryConverters.Precision, InventoryItem.CostDecimals);

        // Concurrence optimiste sur xmin : deux mouvements simultanés ne peuvent pas partir du même stock.
        builder.Property<uint>("Version").IsRowVersion();

        // Les mouvements ajoutés par l'agrégat sont insérés avec lui ; l'historique n'est jamais rechargé.
        builder.HasMany(item => item.Movements)
            .WithOne()
            .HasForeignKey(movement => movement.InventoryItemId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(item => item.Movements).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(item => item.DomainEvents);
        builder.Ignore(item => item.IsLowStock);
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements", table =>
        {
            table.HasCheckConstraint("ck_stock_movements_type", "type BETWEEN 1 AND 6");
            // Seule la trace d'un inventaire qui a confirmé le stock a un delta nul.
            table.HasCheckConstraint("ck_stock_movements_delta", "(quantity_delta = 0) = (type = 6)");
        });

        builder.HasKey(movement => movement.Id);
        // Jamais généré : un mouvement atteint par son article avec un id déjà défini est un NOUVEAU mouvement.
        builder.Property(movement => movement.Id).HasColumnName("id").HasConversion(InventoryConverters.MovementKey).ValueGeneratedNever();
        builder.Property(movement => movement.TenantId).HasColumnName("tenant_id").HasConversion(InventoryConverters.TenantKey);
        builder.Property(movement => movement.InventoryItemId).HasColumnName("inventory_item_id").HasConversion(InventoryConverters.ItemKey);
        // Historique d'un article, du plus récent au plus ancien.
        builder.HasIndex(movement => new { movement.TenantId, movement.InventoryItemId, movement.OccurredAtUtc })
            .HasDatabaseName("ix_stock_movements_tenant_id_inventory_item_id_occurred_at_utc");
        // Retrouver les mouvements d'une commande ou d'un transfert.
        builder.HasIndex(movement => new { movement.TenantId, movement.Reference }).HasDatabaseName("ix_stock_movements_tenant_id_reference");

        builder.Property(movement => movement.Type).HasColumnName("type");
        builder.Property(movement => movement.QuantityDelta)
            .HasColumnName("quantity_delta")
            .HasPrecision(InventoryConverters.Precision, Quantity.MaxDecimals);
        builder.Property(movement => movement.Unit).HasColumnName("unit");
        builder.Property(movement => movement.OccurredAtUtc).HasColumnName("occurred_at_utc");
        builder.Property(movement => movement.Reference).HasColumnName("reference").HasMaxLength(StockMovement.ReferenceMaxLength);
        builder.Property(movement => movement.Reason).HasColumnName("reason").HasMaxLength(StockMovement.ReasonMaxLength);
        builder.Property(movement => movement.UnitCost).HasColumnName("unit_cost").HasPrecision(InventoryConverters.Precision, InventoryItem.CostDecimals);
    }
}

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("suppliers");
        builder.HasKey(supplier => supplier.Id);
        builder.Property(supplier => supplier.Id).HasColumnName("id").HasConversion(id => id.Value, value => SupplierId.From(value)).ValueGeneratedNever();
        builder.Property(supplier => supplier.TenantId).HasColumnName("tenant_id").HasConversion(InventoryConverters.TenantKey);
        builder.Property(supplier => supplier.Name).HasColumnName("name").HasMaxLength(Supplier.NameMaxLength);
        builder.Property(supplier => supplier.Phone).HasColumnName("phone").HasMaxLength(Supplier.ContactMaxLength);
        builder.Property(supplier => supplier.Email).HasColumnName("email").HasMaxLength(Supplier.ContactMaxLength);
        builder.Property(supplier => supplier.Notes).HasColumnName("notes").HasMaxLength(Supplier.NotesMaxLength);
        builder.Property(supplier => supplier.IsActive).HasColumnName("is_active");
        builder.Property(supplier => supplier.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.HasIndex(supplier => supplier.TenantId).HasDatabaseName("ix_suppliers_tenant_id");
        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(supplier => supplier.DomainEvents);
    }
}

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("purchase_orders", table => table.HasCheckConstraint("ck_purchase_orders_status", "status BETWEEN 1 AND 5"));
        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).HasColumnName("id").HasConversion(id => id.Value, value => PurchaseOrderId.From(value)).ValueGeneratedNever();
        builder.Property(order => order.TenantId).HasColumnName("tenant_id").HasConversion(InventoryConverters.TenantKey);
        builder.Property(order => order.LocationId).HasColumnName("location_id").HasConversion(InventoryConverters.LocationKey);
        builder.Property(order => order.SupplierId).HasColumnName("supplier_id").HasConversion(id => id.Value, value => SupplierId.From(value));
        builder.Property(order => order.Number).HasColumnName("number").HasMaxLength(20);
        builder.Property(order => order.Status).HasColumnName("status");
        builder.Property(order => order.Notes).HasColumnName("notes").HasMaxLength(PurchaseOrder.NotesMaxLength);
        builder.Property(order => order.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(order => order.SentAtUtc).HasColumnName("sent_at_utc");
        builder.Property(order => order.ReceivedAtUtc).HasColumnName("received_at_utc");
        // Lignes : valeurs sans identité propre (l'article de stock les distingue), gardées en jsonb avec le bon.
        builder.Property<List<PurchaseOrderLine>>("_lines")
            .HasColumnName("lines")
            .HasColumnType("jsonb")
            .HasConversion(PurchaseOrderLines.Converter, PurchaseOrderLines.Comparer);
        builder.Ignore(order => order.Lines);
        builder.Ignore(order => order.EstimatedTotal);
        builder.HasIndex(order => new { order.TenantId, order.LocationId, order.CreatedAtUtc }).HasDatabaseName("ix_purchase_orders_tenant_id_location_id_created_at_utc");
        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(order => order.DomainEvents);
    }
}

internal static class PurchaseOrderLines
{
    public static readonly ValueConverter<List<PurchaseOrderLine>, string> Converter = new(lines => Write(lines), json => Read(json));

    public static readonly Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<PurchaseOrderLine>> Comparer = new(
        (left, right) => left!.SequenceEqual(right!),
        lines => lines.Aggregate(0, (hash, line) => HashCode.Combine(hash, line.GetHashCode())),
        lines => lines.ToList());

    private static string Write(List<PurchaseOrderLine> lines) =>
        System.Text.Json.JsonSerializer.Serialize(lines.Select(line => new Row(line.ItemId.Value, line.IngredientId.Value, line.Unit, line.Ordered, line.UnitPrice, line.Received)).ToList());

    private static List<PurchaseOrderLine> Read(string json) =>
        (System.Text.Json.JsonSerializer.Deserialize<List<Row>>(json) ?? [])
            .Select(row => new PurchaseOrderLine(InventoryItemId.From(row.ItemId), IngredientId.From(row.IngredientId), row.Unit, row.Ordered, row.UnitPrice, row.Received))
            .ToList();

    private sealed record Row(Guid ItemId, Guid IngredientId, UnitOfMeasure Unit, decimal Ordered, decimal? UnitPrice, decimal Received);
}

internal sealed class RecipeConfiguration : IEntityTypeConfiguration<Recipe>
{
    public const string OwnerProperty = "RecipeId";
    public const string ProductIndex = "ix_recipes_tenant_id_product_id";
    public const string VariantIndex = "ix_recipes_tenant_id_product_id_variant_id";

    public void Configure(EntityTypeBuilder<Recipe> builder)
    {
        builder.ToTable("recipes");

        builder.HasKey(recipe => recipe.Id);
        builder.Property(recipe => recipe.Id).HasColumnName("id").HasConversion(InventoryConverters.RecipeKey).ValueGeneratedNever();
        builder.Property(recipe => recipe.TenantId).HasColumnName("tenant_id").HasConversion(InventoryConverters.TenantKey);
        // Références par id vers le module Catalog, sans clé étrangère.
        builder.Property(recipe => recipe.ProductId).HasColumnName("product_id").HasConversion(InventoryConverters.ProductKey);
        builder.Property(recipe => recipe.VariantId).HasColumnName("variant_id").HasConversion(InventoryConverters.VariantKey);
        // Une recette au niveau produit et au plus une par variante (deux index partiels : NULL n'est jamais égal à NULL).
        builder.HasIndex(recipe => new { recipe.TenantId, recipe.ProductId })
            .IsUnique()
            .HasFilter("variant_id IS NULL")
            .HasDatabaseName(ProductIndex);
        builder.HasIndex(recipe => new { recipe.TenantId, recipe.ProductId, recipe.VariantId })
            .IsUnique()
            .HasFilter("variant_id IS NOT NULL")
            .HasDatabaseName(VariantIndex);

        // Concurrence optimiste sur xmin ; l'unit of work réécrit la recette quand seules ses lignes changent.
        builder.Property<uint>("Version").IsRowVersion();

        builder.OwnsMany(recipe => recipe.Ingredients, component =>
        {
            component.ToTable("recipe_ingredients", table => table.HasCheckConstraint("ck_recipe_ingredients_quantity", "quantity > 0"));
            component.WithOwner().HasForeignKey(OwnerProperty);
            component.Property<RecipeId>(OwnerProperty).HasColumnName("recipe_id").HasConversion(InventoryConverters.RecipeKey);
            component.Property<long>("Id").HasColumnName("id");
            component.HasKey("Id");
            component.Property(line => line.IngredientId).HasColumnName("ingredient_id").HasConversion(InventoryConverters.IngredientKey);
            component.OwnsOne(line => line.Quantity, quantity => InventoryConverters.MapQuantity(quantity, "quantity"));
            component.Navigation(line => line.Quantity).IsRequired();
            // Quelles recettes utilisent un ingrédient.
            component.HasIndex(line => line.IngredientId).HasDatabaseName("ix_recipe_ingredients_ingredient_id");
        });
        builder.Navigation(recipe => recipe.Ingredients).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(recipe => recipe.DomainEvents);
    }
}

internal sealed class OrderConsumptionConfiguration : IEntityTypeConfiguration<OrderConsumption>
{
    public const string Key = "pk_order_consumptions";

    public void Configure(EntityTypeBuilder<OrderConsumption> builder)
    {
        builder.ToTable("order_consumptions");

        // Une consommation par commande : deux traitements simultanés du même événement se heurtent ici.
        builder.HasKey(consumption => new { consumption.TenantId, consumption.OrderId }).HasName(Key);
        builder.Property(consumption => consumption.TenantId).HasColumnName("tenant_id").HasConversion(InventoryConverters.TenantKey);
        builder.Property(consumption => consumption.OrderId).HasColumnName("order_id").HasConversion(InventoryConverters.OrderKey);
        builder.Property(consumption => consumption.LocationId).HasColumnName("location_id").HasConversion(InventoryConverters.LocationKey);
        builder.Property(consumption => consumption.ConsumedAtUtc).HasColumnName("consumed_at_utc");
    }
}
