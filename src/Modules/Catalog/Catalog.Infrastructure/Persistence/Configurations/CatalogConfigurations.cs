using Catalog.Domain;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Catalog.Infrastructure.Persistence.Configurations;

internal static class CatalogConverters
{
    public static readonly ValueConverter<TenantId, Guid> TenantKey = new(id => id.Value, value => TenantId.From(value));
    public static readonly ValueConverter<ProductId, Guid> ProductKey = new(id => id.Value, value => ProductId.From(value));
    public static readonly ValueConverter<VariantId, Guid> VariantKey = new(id => id.Value, value => VariantId.From(value));
    public static readonly ValueConverter<ModifierGroupId, Guid> ModifierGroupKey = new(id => id.Value, value => ModifierGroupId.From(value));
    public static readonly ValueConverter<ModifierId, Guid> ModifierKey = new(id => id.Value, value => ModifierId.From(value));
    public static readonly ValueConverter<MenuId, Guid> MenuKey = new(id => id.Value, value => MenuId.From(value));
    public static readonly ValueConverter<MenuSectionId, Guid> MenuSectionKey = new(id => id.Value, value => MenuSectionId.From(value));
    public static readonly ValueConverter<TaxCategoryId, Guid> TaxCategoryKey = new(id => id.Value, value => TaxCategoryId.From(value));
    public static readonly ValueConverter<LocationId, Guid> LocationKey = new(id => id.Value, value => LocationId.From(value));
    public static readonly ValueConverter<CatalogName, string> NameText = new(name => name.Value, value => CatalogName.Create(value));
    public static readonly ValueConverter<Currency, string> CurrencyCode = new(currency => currency.Code, value => Currency.FromCode(value));

    // Listes d'ids en jsonb, relues via les fabriques des ids : un id vide stocké échoue bruyamment au lieu de se charger.
    public static readonly ValueConverter<List<ModifierGroupId>, string> ModifierGroupIds = new(
        ids => CatalogJson.WriteIds(ids.Select(id => id.Value)),
        json => CatalogJson.ReadIds(json).Select(value => ModifierGroupId.From(value)).ToList());

    // Codes du référentiel relus par sa validation : un code inconnu stocké échoue bruyamment au lieu de se charger.
    public static readonly ValueConverter<List<string>, string> AllergenCodes = new(
        codes => CatalogJson.WriteCodes(codes),
        json => Catalog.Domain.AllergenCodes.Normalize(CatalogJson.ReadCodes(json)).ToList());

    public static readonly ValueConverter<List<MenuSlot>, string> MenuSlots = new(
        slots => CatalogJson.WriteSlots(slots), json => CatalogJson.ReadSlots(json));

    public static readonly ValueConverter<List<PriceOverride>, string> PriceOverrides = new(
        overrides => CatalogJson.WritePriceOverrides(overrides), json => CatalogJson.ReadPriceOverrides(json));

    public static readonly ValueConverter<List<FormulaStep>, string> FormulaSteps = new(
        steps => CatalogJson.WriteFormulaSteps(steps), json => CatalogJson.ReadFormulaSteps(json));

    public static readonly ValueConverter<List<OnlineChannel>, string> OnlineChannels = new(
        channels => CatalogJson.WriteChannels(channels),
        json => CatalogJson.ReadChannels(json));

    public static readonly ValueConverter<List<LocationId>, string> LocationIds = new(
        ids => CatalogJson.WriteIds(ids.Select(id => id.Value)),
        json => CatalogJson.ReadIds(json).Select(value => LocationId.From(value)).ToList());

    public static readonly ValueConverter<List<ProductId>, string> ProductIds = new(
        ids => CatalogJson.WriteIds(ids.Select(id => id.Value)),
        json => CatalogJson.ReadIds(json).Select(value => ProductId.From(value)).ToList());

    /// <summary>Une liste jsonb est modifiée sur place par le domaine : on la compare élément par élément, dans l'ordre.</summary>
    public static ValueComparer<List<T>> ListComparer<T>() => new(
        (left, right) => left!.SequenceEqual(right!),
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item!.GetHashCode())),
        list => list.ToList());

    /// <summary>
    /// Montant + devise en deux colonnes. 19 chiffres dont 3 décimales : couvre <see cref="Money.MaxAbsoluteAmount"/>
    /// et les devises à 3 décimales.
    /// </summary>
    public static void MapMoney(ComplexPropertyBuilder<Money> money, string amountColumn, string currencyColumn)
    {
        money.Property(m => m.Amount).HasColumnName(amountColumn).HasPrecision(19, 3);
        money.Property(m => m.Currency)
            .HasColumnName(currencyColumn)
            .HasConversion(CurrencyCode)
            .HasMaxLength(3)
            .IsFixedLength();
    }
}

/// <summary>Position (base zéro) d'un enfant dans la liste ordonnée de son agrégat : posée par l'unit of work.</summary>
internal static class Ordering
{
    public const string PositionProperty = "Position";
}

internal sealed class TaxCategoryConfiguration : IEntityTypeConfiguration<TaxCategory>
{
    public const string TenantCodeIndex = "ix_tax_categories_tenant_id_code";

    public void Configure(EntityTypeBuilder<TaxCategory> builder)
    {
        builder.ToTable("tax_categories");

        builder.HasKey(category => category.Id);
        builder.Property(category => category.Id).HasColumnName("id").HasConversion(CatalogConverters.TaxCategoryKey).ValueGeneratedNever();
        builder.Property(category => category.TenantId).HasColumnName("tenant_id").HasConversion(CatalogConverters.TenantKey);

        // Déjà en majuscules (TaxCategoryCode) : l'égalité simple suffit. Unique par tenant, archivées comprises :
        // garde-fou final contre deux créations concurrentes.
        builder.Property(category => category.Code)
            .HasColumnName("code")
            .HasConversion(code => code.Value, value => TaxCategoryCode.Create(value))
            .HasMaxLength(TaxCategoryCode.MaxLength);
        builder.HasIndex(category => new { category.TenantId, category.Code }).IsUnique().HasDatabaseName(TenantCodeIndex);

        builder.Property(category => category.Name)
            .HasColumnName("name")
            .HasConversion(name => name.Value, value => TaxCategoryName.Create(value))
            .HasMaxLength(TaxCategoryName.MaxLength);

        // Fraction entre 0 et 1 à 6 décimales au plus (TaxRate).
        builder.Property(category => category.Rate)
            .HasColumnName("rate")
            .HasConversion(rate => rate.Value, value => TaxRate.Create(value))
            .HasPrecision(7, 6);

        // Numérique : les valeurs de l'enum sont explicites et stables.
        builder.Property(category => category.Status).HasColumnName("status");

        // Concurrence optimiste sur la colonne système xmin de PostgreSQL.
        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(category => category.DomainEvents);
        builder.Ignore(category => category.IsArchived);
    }
}

internal sealed class ModifierGroupConfiguration : IEntityTypeConfiguration<ModifierGroup>
{
    public const string OwnerProperty = "ModifierGroupId";

    public void Configure(EntityTypeBuilder<ModifierGroup> builder)
    {
        builder.ToTable("modifier_groups");

        builder.HasKey(group => group.Id);
        builder.Property(group => group.Id).HasColumnName("id").HasConversion(CatalogConverters.ModifierGroupKey).ValueGeneratedNever();
        builder.Property(group => group.TenantId).HasColumnName("tenant_id").HasConversion(CatalogConverters.TenantKey);
        builder.HasIndex(group => group.TenantId).HasDatabaseName("ix_modifier_groups_tenant_id");

        builder.Property(group => group.Name).HasColumnName("name").HasConversion(CatalogConverters.NameText).HasMaxLength(CatalogName.MaxLength);
        builder.Property(group => group.Currency)
            .HasColumnName("currency")
            .HasConversion(CatalogConverters.CurrencyCode)
            .HasMaxLength(3)
            .IsFixedLength();

        builder.ComplexProperty(group => group.SelectionRule, rule =>
        {
            rule.Property(r => r.MinSelections).HasColumnName("min_selections");
            rule.Property(r => r.MaxSelections).HasColumnName("max_selections");
        });

        // Concurrence optimiste sur la colonne système xmin de PostgreSQL.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasMany(group => group.Modifiers)
            .WithOne()
            .HasForeignKey(OwnerProperty)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(group => group.Modifiers).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(group => group.DomainEvents);
        builder.Ignore(group => group.MinSelections);
        builder.Ignore(group => group.MaxSelections);
        builder.Ignore(group => group.IsSatisfiable);
    }
}

internal sealed class ModifierConfiguration : IEntityTypeConfiguration<Modifier>
{
    public void Configure(EntityTypeBuilder<Modifier> builder)
    {
        builder.ToTable("modifiers");

        builder.HasKey(modifier => modifier.Id);
        // Jamais généré : une option atteinte via son groupe avec un id déjà défini est une NOUVELLE ligne.
        builder.Property(modifier => modifier.Id).HasColumnName("id").HasConversion(CatalogConverters.ModifierKey).ValueGeneratedNever();
        builder.Property<ModifierGroupId>(ModifierGroupConfiguration.OwnerProperty)
            .HasColumnName("modifier_group_id")
            .HasConversion(CatalogConverters.ModifierGroupKey);
        builder.Property<int>(Ordering.PositionProperty).HasColumnName("position");

        builder.Property(modifier => modifier.Name).HasColumnName("name").HasConversion(CatalogConverters.NameText).HasMaxLength(CatalogName.MaxLength);
        builder.ComplexProperty(modifier => modifier.PriceDelta, price => CatalogConverters.MapMoney(price, "price_delta", "currency"));
        builder.Property(modifier => modifier.IsAvailable).HasColumnName("is_available");
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public const string OwnerProperty = "ProductId";

    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.HasKey(product => product.Id);
        builder.Property(product => product.Id).HasColumnName("id").HasConversion(CatalogConverters.ProductKey).ValueGeneratedNever();
        builder.Property(product => product.TenantId).HasColumnName("tenant_id").HasConversion(CatalogConverters.TenantKey);
        builder.HasIndex(product => product.TenantId).HasDatabaseName("ix_products_tenant_id");

        builder.Property(product => product.Name).HasColumnName("name").HasConversion(CatalogConverters.NameText).HasMaxLength(CatalogName.MaxLength);
        builder.Property(product => product.Description).HasColumnName("description").HasMaxLength(Product.MaxDescriptionLength);
        // Numérique : les valeurs de l'enum sont explicites et stables.
        builder.Property(product => product.Status).HasColumnName("status");
        builder.ComplexProperty(product => product.BasePrice, price => CatalogConverters.MapMoney(price, "base_price", "currency"));
        builder.Property(product => product.TaxCategoryId).HasColumnName("tax_category_id").HasConversion(CatalogConverters.TaxCategoryKey);

        // Références par id vers d'autres agrégats, sans clé étrangère (règle des agrégats) : listes jsonb.
        builder.Property<List<ModifierGroupId>>("_modifierGroupIds")
            .HasColumnName("modifier_group_ids")
            .HasColumnType("jsonb")
            .HasConversion(CatalogConverters.ModifierGroupIds, CatalogConverters.ListComparer<ModifierGroupId>());
        builder.Property<List<string>>("_allergens")
            .HasColumnName("allergens")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .HasConversion(CatalogConverters.AllergenCodes, CatalogConverters.ListComparer<string>());
        builder.Property<List<OnlineChannel>>("_hiddenChannels")
            .HasColumnName("hidden_channels")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .HasConversion(CatalogConverters.OnlineChannels, CatalogConverters.ListComparer<OnlineChannel>());
        builder.Property<List<LocationId>>("_locationIds")
            .HasColumnName("location_ids")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .HasConversion(CatalogConverters.LocationIds, CatalogConverters.ListComparer<LocationId>());
        builder.Property<List<LocationId>>("_unavailableAt")
            .HasColumnName("unavailable_at")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .HasConversion(CatalogConverters.LocationIds, CatalogConverters.ListComparer<LocationId>());
        builder.Property<List<PriceOverride>>("_priceOverrides")
            .HasColumnName("price_overrides")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .HasConversion(CatalogConverters.PriceOverrides, CatalogConverters.ListComparer<PriceOverride>());
        // Formule (menu composé) : étapes et produits proposés, par id, sans clé étrangère.
        builder.Property<List<FormulaStep>>("_formulaSteps")
            .HasColumnName("formula_steps")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .HasConversion(CatalogConverters.FormulaSteps, CatalogConverters.ListComparer<FormulaStep>());

        // Concurrence optimiste sur la colonne système xmin de PostgreSQL.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasMany(product => product.Variants)
            .WithOne()
            .HasForeignKey(OwnerProperty)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(product => product.Variants).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(product => product.DomainEvents);
        builder.Ignore(product => product.ModifierGroupIds);
        builder.Ignore(product => product.Allergens);
        builder.Ignore(product => product.HiddenChannels);
        builder.Ignore(product => product.PriceOverrides);
        builder.Ignore(product => product.LocationIds);
        builder.Ignore(product => product.UnavailableAt);
        builder.Ignore(product => product.FormulaSteps);
        builder.Ignore(product => product.IsFormula);
        builder.Ignore(product => product.Currency);
        builder.Ignore(product => product.IsSellable);
    }
}

internal sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("product_variants");

        builder.HasKey(variant => variant.Id);
        // Jamais généré : une variante atteinte via son produit avec un id déjà défini est une NOUVELLE ligne.
        builder.Property(variant => variant.Id).HasColumnName("id").HasConversion(CatalogConverters.VariantKey).ValueGeneratedNever();
        builder.Property<ProductId>(ProductConfiguration.OwnerProperty)
            .HasColumnName("product_id")
            .HasConversion(CatalogConverters.ProductKey);
        builder.Property<int>(Ordering.PositionProperty).HasColumnName("position");

        builder.Property(variant => variant.Name).HasColumnName("name").HasConversion(CatalogConverters.NameText).HasMaxLength(CatalogName.MaxLength);
        builder.ComplexProperty(variant => variant.PriceDelta, price => CatalogConverters.MapMoney(price, "price_delta", "currency"));
        builder.Property(variant => variant.Sku)
            .HasColumnName("sku")
            .HasConversion(sku => sku!.Value, value => Sku.Create(value))
            .HasMaxLength(Sku.MaxLength);
        builder.Property(variant => variant.IsAvailable).HasColumnName("is_available");
    }
}

internal sealed class MenuConfiguration : IEntityTypeConfiguration<Menu>
{
    public const string OwnerProperty = "MenuId";

    public void Configure(EntityTypeBuilder<Menu> builder)
    {
        builder.ToTable("menus");

        builder.HasKey(menu => menu.Id);
        builder.Property(menu => menu.Id).HasColumnName("id").HasConversion(CatalogConverters.MenuKey).ValueGeneratedNever();
        builder.Property(menu => menu.TenantId).HasColumnName("tenant_id").HasConversion(CatalogConverters.TenantKey);
        builder.Property(menu => menu.LocationId).HasColumnName("location_id").HasConversion(CatalogConverters.LocationKey);
        // Les menus se lisent par tenant, souvent par établissement.
        builder.HasIndex(menu => new { menu.TenantId, menu.LocationId }).HasDatabaseName("ix_menus_tenant_id_location_id");

        builder.Property(menu => menu.Name).HasColumnName("name").HasConversion(CatalogConverters.NameText).HasMaxLength(CatalogName.MaxLength);
        // Numérique : les valeurs de l'enum sont explicites et stables.
        builder.Property(menu => menu.Status).HasColumnName("status");
        builder.Property(menu => menu.PublishedAtUtc).HasColumnName("published_at_utc");
        builder.Property<List<MenuSlot>>("_slots")
            .HasColumnName("slots")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .HasConversion(CatalogConverters.MenuSlots, CatalogConverters.ListComparer<MenuSlot>());
        builder.Ignore(menu => menu.Slots);

        // Concurrence optimiste sur la colonne système xmin de PostgreSQL.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasMany(menu => menu.Sections)
            .WithOne()
            .HasForeignKey(OwnerProperty)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(menu => menu.Sections).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(menu => menu.DomainEvents);
        builder.Ignore(menu => menu.CanBePublished);
    }
}

internal sealed class MenuSectionConfiguration : IEntityTypeConfiguration<MenuSection>
{
    public void Configure(EntityTypeBuilder<MenuSection> builder)
    {
        builder.ToTable("menu_sections");

        builder.HasKey(section => section.Id);
        // Jamais généré : une section atteinte via son menu avec un id déjà défini est une NOUVELLE ligne.
        builder.Property(section => section.Id).HasColumnName("id").HasConversion(CatalogConverters.MenuSectionKey).ValueGeneratedNever();
        builder.Property<MenuId>(MenuConfiguration.OwnerProperty).HasColumnName("menu_id").HasConversion(CatalogConverters.MenuKey);
        builder.Property<int>(Ordering.PositionProperty).HasColumnName("position");

        builder.Property(section => section.Name).HasColumnName("name").HasConversion(CatalogConverters.NameText).HasMaxLength(CatalogName.MaxLength);
        // Les produits de la section, dans l'ordre d'affichage.
        builder.Property<List<ProductId>>("_productIds")
            .HasColumnName("product_ids")
            .HasColumnType("jsonb")
            .HasConversion(CatalogConverters.ProductIds, CatalogConverters.ListComparer<ProductId>());

        builder.Ignore(section => section.ProductIds);
        builder.Ignore(section => section.IsEmpty);
    }
}
