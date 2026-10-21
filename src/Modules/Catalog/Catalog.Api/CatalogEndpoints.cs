using Catalog.Application;
using Catalog.Application.Commands;
using Catalog.Application.Queries;
using Catalog.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using SaasRestaurant.BuildingBlocks.Api;

namespace Catalog.Api;

/// <summary>
/// Catalogue du restaurant sélectionné dans l'access token : catégories fiscales, groupes d'options, produits, menus.
/// Aucune route ne prend de tenant : ce n'est jamais une valeur de requête. L'hôte chaîne l'autorisation
/// (<c>Tenancy.TenantMemberPolicy</c>) sur le groupe renvoyé ; les lectures sont ouvertes à tout membre, chaque
/// écriture exige en plus <see cref="CatalogPermissions.Manage"/>.
/// </summary>
public static class CatalogEndpoints
{
    private const string Manage = CatalogPermissions.Manage;
    private const string GetTaxCategoryRoute = "GetTaxCategory";
    private const string GetModifierGroupRoute = "GetModifierGroup";
    private const string GetProductRoute = "GetProduct";
    private const string GetMenuRoute = "GetMenu";

    public static RouteGroupBuilder MapCatalogEndpoints(this IEndpointRouteBuilder routes)
    {
        var catalog = routes.MapGroup("/catalog").WithTags("Catalog")
            .AllowApiKeyRead(CatalogPermissions.Manage, "orders.take");
        MapTaxCategories(catalog.MapGroup("/tax-categories"));
        MapModifierGroups(catalog.MapGroup("/modifier-groups"));
        MapProducts(catalog.MapGroup("/products"));
        MapMenus(catalog.MapGroup("/menus"));

        // Le référentiel des allergènes (codes) : le libellé est traduit par le front.
        catalog.MapGet("/allergens", () => TypedResults.Ok(AllergenCodes.All));
        return catalog;
    }

    private static void MapTaxCategories(RouteGroupBuilder categories)
    {
        categories.MapPost("/", async (
            CreateTaxCategoryRequest request, ICommandHandler<CreateTaxCategoryCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new CreateTaxCategoryCommand(request.Code, request.Name, request.Rate), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetTaxCategoryRoute, new { id });
        }).RequirePermission(Manage);

        categories.MapGet("/", async (
            bool? includeArchived, IQueryHandler<ListTaxCategoriesQuery, IReadOnlyList<TaxCategoryDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListTaxCategoriesQuery(includeArchived ?? false), ct)));

        categories.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetTaxCategoryByIdQuery, TaxCategoryDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetTaxCategoryByIdQuery(id), ct)))
            .WithName(GetTaxCategoryRoute);

        categories.MapPut("/{id:guid}/name", (
            Guid id, NameRequest request, ICommandHandler<RenameTaxCategoryCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RenameTaxCategoryCommand(id, request.Name), ct)))
            .RequirePermission(Manage);

        categories.MapPut("/{id:guid}/rate", (
            Guid id, TaxRateRequest request, ICommandHandler<ChangeTaxRateCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeTaxRateCommand(id, request.Rate), ct)))
            .RequirePermission(Manage);

        categories.MapPost("/{id:guid}/archive", (
            Guid id, ICommandHandler<ArchiveTaxCategoryCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ArchiveTaxCategoryCommand(id), ct)))
            .RequirePermission(Manage);
    }

    private static void MapModifierGroups(RouteGroupBuilder groups)
    {
        groups.MapPost("/", async (
            CreateModifierGroupRequest request, ICommandHandler<CreateModifierGroupCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(
                new CreateModifierGroupCommand(request.Name, request.MinSelections, request.MaxSelections), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetModifierGroupRoute, new { id });
        }).RequirePermission(Manage);

        groups.MapGet("/", async (
            IQueryHandler<ListModifierGroupsQuery, IReadOnlyList<ModifierGroupDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListModifierGroupsQuery(), ct)));

        groups.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetModifierGroupByIdQuery, ModifierGroupDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetModifierGroupByIdQuery(id), ct)))
            .WithName(GetModifierGroupRoute);

        groups.MapPut("/{id:guid}/name", (
            Guid id, NameRequest request, ICommandHandler<RenameModifierGroupCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RenameModifierGroupCommand(id, request.Name), ct)))
            .RequirePermission(Manage);

        groups.MapPut("/{id:guid}/selection-rule", (
            Guid id, SelectionRuleRequest request, ICommandHandler<ChangeSelectionRuleCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeSelectionRuleCommand(id, request.MinSelections, request.MaxSelections), ct)))
            .RequirePermission(Manage);

        // Options du groupe.
        groups.MapPost("/{id:guid}/modifiers", async (
            Guid id, AddModifierRequest request, ICommandHandler<AddModifierCommand, Guid> handler, CancellationToken ct) =>
        {
            var modifierId = await handler.HandleAsync(new AddModifierCommand(id, request.Name, request.PriceDelta), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(modifierId), GetModifierGroupRoute, new { id });
        }).RequirePermission(Manage);

        groups.MapPut("/{id:guid}/modifiers/{modifierId:guid}/name", (
            Guid id, Guid modifierId, NameRequest request, ICommandHandler<RenameModifierCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RenameModifierCommand(id, modifierId, request.Name), ct)))
            .RequirePermission(Manage);

        groups.MapPut("/{id:guid}/modifiers/{modifierId:guid}/price", (
            Guid id, Guid modifierId, PriceDeltaRequest request, ICommandHandler<ChangeModifierPriceCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeModifierPriceCommand(id, modifierId, request.PriceDelta), ct)))
            .RequirePermission(Manage);

        groups.MapPut("/{id:guid}/modifiers/{modifierId:guid}/availability", (
            Guid id, Guid modifierId, AvailabilityRequest request, ICommandHandler<SetModifierAvailabilityCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetModifierAvailabilityCommand(id, modifierId, request.IsAvailable), ct)))
            .RequirePermission(Manage);

        groups.MapDelete("/{id:guid}/modifiers/{modifierId:guid}", (
            Guid id, Guid modifierId, ICommandHandler<RemoveModifierCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RemoveModifierCommand(id, modifierId), ct)))
            .RequirePermission(Manage);
    }

    private static void MapProducts(RouteGroupBuilder products)
    {
        products.MapPost("/", async (
            CreateProductRequest request, ICommandHandler<CreateProductCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(
                new CreateProductCommand(request.Name, request.Description, request.BasePrice, request.TaxCategoryId), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetProductRoute, new { id });
        }).RequirePermission(Manage);

        products.MapGet("/", async (
            int? page,
            int? pageSize,
            ProductStatus? status,
            IQueryHandler<ListProductsQuery, PagedResult<ProductSummaryDto>> handler,
            CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(
                new ListProductsQuery(page ?? 1, pageSize ?? ListProductsQuery.DefaultPageSize, status), ct)));

        products.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetProductByIdQuery, ProductDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetProductByIdQuery(id), ct)))
            .WithName(GetProductRoute);

        // Réglages : chacun est remplacé en entier.
        products.MapPut("/{id:guid}/name", (
            Guid id, NameRequest request, ICommandHandler<RenameProductCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RenameProductCommand(id, request.Name), ct)))
            .RequirePermission(Manage);

        products.MapPut("/{id:guid}/description", (
            Guid id, DescriptionRequest request, ICommandHandler<ChangeProductDescriptionCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeProductDescriptionCommand(id, request.Description), ct)))
            .RequirePermission(Manage);

        products.MapPut("/{id:guid}/tax-category", (
            Guid id, TaxCategoryRequest request, ICommandHandler<ChangeProductTaxCategoryCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeProductTaxCategoryCommand(id, request.TaxCategoryId), ct)))
            .RequirePermission(Manage);

        products.MapPut("/{id:guid}/base-price", (
            Guid id, BasePriceRequest request, ICommandHandler<ChangeProductBasePriceCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeProductBasePriceCommand(id, request.BasePrice), ct)))
            .RequirePermission(Manage);

        // Cycle de vie.
        products.MapPost("/{id:guid}/activate", (
            Guid id, ICommandHandler<ActivateProductCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ActivateProductCommand(id), ct)))
            .RequirePermission(Manage);

        products.MapPost("/{id:guid}/mark-unavailable", (
            Guid id, ICommandHandler<MarkProductUnavailableCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new MarkProductUnavailableCommand(id), ct)))
            .RequirePermission(Manage);

        products.MapPost("/{id:guid}/archive", (
            Guid id, ICommandHandler<ArchiveProductCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ArchiveProductCommand(id), ct)))
            .RequirePermission(Manage);

        // Variantes.
        products.MapPost("/{id:guid}/variants", async (
            Guid id, AddVariantRequest request, ICommandHandler<AddProductVariantCommand, Guid> handler, CancellationToken ct) =>
        {
            var variantId = await handler.HandleAsync(new AddProductVariantCommand(id, request.Name, request.PriceDelta, request.Sku), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(variantId), GetProductRoute, new { id });
        }).RequirePermission(Manage);

        products.MapPut("/{id:guid}/variants/{variantId:guid}/price-delta", (
            Guid id, Guid variantId, PriceDeltaRequest request, ICommandHandler<ChangeVariantPriceDeltaCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeVariantPriceDeltaCommand(id, variantId, request.PriceDelta), ct)))
            .RequirePermission(Manage);

        products.MapPut("/{id:guid}/variants/{variantId:guid}/availability", (
            Guid id, Guid variantId, AvailabilityRequest request, ICommandHandler<SetVariantAvailabilityCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetVariantAvailabilityCommand(id, variantId, request.IsAvailable), ct)))
            .RequirePermission(Manage);

        products.MapDelete("/{id:guid}/variants/{variantId:guid}", (
            Guid id, Guid variantId, ICommandHandler<RemoveProductVariantCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RemoveProductVariantCommand(id, variantId), ct)))
            .RequirePermission(Manage);

        // Groupes d'options attachés.
        products.MapPost("/{id:guid}/modifier-groups", (
            Guid id, ModifierGroupRequest request, ICommandHandler<AttachModifierGroupCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new AttachModifierGroupCommand(id, request.ModifierGroupId), ct)))
            .RequirePermission(Manage);

        products.MapDelete("/{id:guid}/modifier-groups/{modifierGroupId:guid}", (
            Guid id, Guid modifierGroupId, ICommandHandler<DetachModifierGroupCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new DetachModifierGroupCommand(id, modifierGroupId), ct)))
            .RequirePermission(Manage);

        // Vente en ligne : allergènes affichés et canaux où le produit est masqué.
        products.MapPut("/{id:guid}/allergens", (
            Guid id, AllergensRequest request, ICommandHandler<SetProductAllergensCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetProductAllergensCommand(id, request.Allergens), ct)))
            .RequirePermission(Manage);

        // Prix propres à un établissement ou à un canal : posés (PUT) ou retirés (DELETE, mêmes critères en requête).
        products.MapPut("/{id:guid}/price-overrides", (
            Guid id, PriceOverrideRequest request, ICommandHandler<SetProductPriceOverrideCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetProductPriceOverrideCommand(id, request.LocationId, request.Channel, request.BasePrice), ct)))
            .RequirePermission(Manage);

        products.MapDelete("/{id:guid}/price-overrides", (
            Guid id, Guid? locationId, PriceChannel? channel, ICommandHandler<RemoveProductPriceOverrideCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RemoveProductPriceOverrideCommand(id, locationId, channel), ct)))
            .RequirePermission(Manage);

        // Établissements où le produit est vendu (aucun : tous), et rupture dans un seul établissement.
        products.MapPut("/{id:guid}/locations", (
            Guid id, ProductLocationsRequest request, ICommandHandler<SetProductLocationsCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetProductLocationsCommand(id, request.LocationIds), ct)))
            .RequirePermission(Manage);

        products.MapPut("/{id:guid}/locations/{locationId:guid}/availability", (
            Guid id, Guid locationId, LocationAvailabilityRequest request, ICommandHandler<SetProductAvailabilityAtLocationCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetProductAvailabilityAtLocationCommand(id, locationId, request.IsAvailable), ct)))
            .RequirePermission(Manage);

        // Formule (menu composé) : ses étapes et les produits proposés ; aucune étape en refait un produit ordinaire.
        products.MapPut("/{id:guid}/formula", (
            Guid id, FormulaRequest request, ICommandHandler<SetProductFormulaCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetProductFormulaCommand(id, request.Steps), ct)))
            .RequirePermission(Manage);

        products.MapPut("/{id:guid}/hidden-channels", (
            Guid id, HiddenChannelsRequest request, ICommandHandler<SetProductHiddenChannelsCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetProductHiddenChannelsCommand(id, request.Channels), ct)))
            .RequirePermission(Manage);
    }

    private static void MapMenus(RouteGroupBuilder menus)
    {
        menus.MapPost("/", async (
            CreateMenuRequest request, ICommandHandler<CreateMenuCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new CreateMenuCommand(request.LocationId, request.Name), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetMenuRoute, new { id });
        }).RequirePermission(Manage);

        menus.MapGet("/", async (
            Guid? locationId, IQueryHandler<ListMenusQuery, IReadOnlyList<MenuSummaryDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListMenusQuery(locationId), ct)));

        menus.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetMenuByIdQuery, MenuDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetMenuByIdQuery(id), ct)))
            .WithName(GetMenuRoute);

        // Créneaux (midi, soir, happy hour) : modifiables même publié, le contenu ne change pas.
        menus.MapPut("/{id:guid}/schedule", (
            Guid id, MenuScheduleRequest request, ICommandHandler<SetMenuScheduleCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetMenuScheduleCommand(id, request.Slots), ct)))
            .RequirePermission(Manage);

        // Contenu : modifiable en Draft seulement.
        menus.MapPut("/{id:guid}/name", (
            Guid id, NameRequest request, ICommandHandler<RenameMenuCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RenameMenuCommand(id, request.Name), ct)))
            .RequirePermission(Manage);

        menus.MapPost("/{id:guid}/sections", async (
            Guid id, NameRequest request, ICommandHandler<AddMenuSectionCommand, Guid> handler, CancellationToken ct) =>
        {
            var sectionId = await handler.HandleAsync(new AddMenuSectionCommand(id, request.Name), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(sectionId), GetMenuRoute, new { id });
        }).RequirePermission(Manage);

        menus.MapPut("/{id:guid}/sections/{sectionId:guid}/name", (
            Guid id, Guid sectionId, NameRequest request, ICommandHandler<RenameMenuSectionCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RenameMenuSectionCommand(id, sectionId, request.Name), ct)))
            .RequirePermission(Manage);

        menus.MapPut("/{id:guid}/sections/{sectionId:guid}/position", (
            Guid id, Guid sectionId, PositionRequest request, ICommandHandler<MoveMenuSectionCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new MoveMenuSectionCommand(id, sectionId, request.Position), ct)))
            .RequirePermission(Manage);

        menus.MapDelete("/{id:guid}/sections/{sectionId:guid}", (
            Guid id, Guid sectionId, ICommandHandler<RemoveMenuSectionCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RemoveMenuSectionCommand(id, sectionId), ct)))
            .RequirePermission(Manage);

        menus.MapPost("/{id:guid}/sections/{sectionId:guid}/products", (
            Guid id, Guid sectionId, ProductRequest request, ICommandHandler<AddProductToMenuSectionCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new AddProductToMenuSectionCommand(id, sectionId, request.ProductId), ct)))
            .RequirePermission(Manage);

        menus.MapPut("/{id:guid}/sections/{sectionId:guid}/products/{productId:guid}/position", (
            Guid id, Guid sectionId, Guid productId, PositionRequest request,
            ICommandHandler<MoveProductInMenuSectionCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new MoveProductInMenuSectionCommand(id, sectionId, productId, request.Position), ct)))
            .RequirePermission(Manage);

        menus.MapDelete("/{id:guid}/sections/{sectionId:guid}/products/{productId:guid}", (
            Guid id, Guid sectionId, Guid productId, ICommandHandler<RemoveProductFromMenuSectionCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RemoveProductFromMenuSectionCommand(id, sectionId, productId), ct)))
            .RequirePermission(Manage);

        // Cycle de vie.
        menus.MapPost("/{id:guid}/publish", (
            Guid id, ICommandHandler<PublishMenuCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new PublishMenuCommand(id), ct)))
            .RequirePermission(Manage);

        menus.MapPost("/{id:guid}/unpublish", (
            Guid id, ICommandHandler<UnpublishMenuCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new UnpublishMenuCommand(id), ct)))
            .RequirePermission(Manage);

        menus.MapPost("/{id:guid}/archive", (
            Guid id, ICommandHandler<ArchiveMenuCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ArchiveMenuCommand(id), ct)))
            .RequirePermission(Manage);
    }

    private static async Task<NoContent> Run(Task command)
    {
        await command;
        return TypedResults.NoContent();
    }
}
