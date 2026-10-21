using Catalog.Domain;

namespace Catalog.Application.Commands;

// Référentiel de taxes du tenant. Le code est fixé à la création et n'est jamais réutilisé (archivées comprises).

/// <summary>Crée une catégorie fiscale active. <paramref name="Rate"/> est une fraction (0.18 = 18 %). Renvoie son id.</summary>
public sealed record CreateTaxCategoryCommand(string? Code, string? Name, decimal Rate) : ICommand<Guid>;

public sealed class CreateTaxCategoryHandler(
    CatalogWorkspace workspace, ITaxCategoryCodeUniquenessChecker uniqueness, IClock clock)
    : ICommandHandler<CreateTaxCategoryCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateTaxCategoryCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var code = TaxCategoryCode.Create(command.Code);

        var category = TaxCategory.Create(
            tenant,
            code,
            TaxCategoryName.Create(command.Name),
            TaxRate.Create(command.Rate),
            await uniqueness.IsUniqueAsync(tenant, code, cancellationToken),
            clock);

        workspace.TaxCategories.Add(category);
        await workspace.SaveAsync(cancellationToken);
        return category.Id.Value;
    }
}

public sealed record RenameTaxCategoryCommand(Guid TaxCategoryId, string? Name) : ICommand;

public sealed class RenameTaxCategoryHandler(CatalogWorkspace workspace) : ICommandHandler<RenameTaxCategoryCommand>
{
    public Task HandleAsync(RenameTaxCategoryCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditTaxCategoryAsync(
            command.TaxCategoryId, category => category.Rename(TaxCategoryName.Create(command.Name)), cancellationToken);
}

/// <summary>Ventes futures uniquement : les lignes de commande gardent le taux qu'elles ont figé.</summary>
public sealed record ChangeTaxRateCommand(Guid TaxCategoryId, decimal Rate) : ICommand;

public sealed class ChangeTaxRateHandler(CatalogWorkspace workspace, IClock clock) : ICommandHandler<ChangeTaxRateCommand>
{
    public Task HandleAsync(ChangeTaxRateCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditTaxCategoryAsync(
            command.TaxCategoryId, category => category.ChangeRate(TaxRate.Create(command.Rate), clock), cancellationToken);
}

/// <summary>Les produits qui l'utilisent la gardent ; elle ne peut plus être affectée à un autre produit.</summary>
public sealed record ArchiveTaxCategoryCommand(Guid TaxCategoryId) : ICommand;

public sealed class ArchiveTaxCategoryHandler(CatalogWorkspace workspace, IClock clock) : ICommandHandler<ArchiveTaxCategoryCommand>
{
    public Task HandleAsync(ArchiveTaxCategoryCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditTaxCategoryAsync(command.TaxCategoryId, category => category.Archive(clock), cancellationToken);
}
