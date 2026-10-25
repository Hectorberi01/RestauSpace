using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SaasRestaurant.Host.Demo;

/// <summary>
/// Effacement brut (SQL) des lignes d'un tenant pour des entités données, dépendants d'abord. Les tables possédées
/// (lignes d'une commande…) suivent par la cascade de la base. Réservé aux restaurants de démonstration.
/// </summary>
internal static class DemoPurge
{
    public static async Task<int> DeleteAsync(DbContext context, IReadOnlyCollection<string> entityNames, TenantId tenant, CancellationToken cancellationToken)
    {
        var types = context.Model.GetEntityTypes()
            .Where(type => !type.IsOwned() && entityNames.Contains(type.ClrType.Name) && type.GetTableName() is not null)
            .ToList();
        var deleted = 0;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        foreach (var type in DependentsFirst(types))
        {
            var table = StoreObjectIdentifier.Table(type.GetTableName()!, type.GetSchema());
            var column = type.FindProperty("TenantId")?.GetColumnName(table);
            if (column is null)
            {
                continue;
            }

            var name = type.GetSchema() is { } schema ? $"\"{schema}\".\"{table.Name}\"" : $"\"{table.Name}\"";
#pragma warning disable EF1002 // noms tirés du modèle EF, valeur passée en paramètre
            deleted += await context.Database.ExecuteSqlRawAsync($"DELETE FROM {name} WHERE \"{column}\" = {{0}}", [tenant.Value], cancellationToken);
#pragma warning restore EF1002
        }

        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    // Une entité qui référence une autre de la liste est effacée avant elle.
    private static List<IEntityType> DependentsFirst(List<IEntityType> types)
    {
        var ordered = new List<IEntityType>();
        var visiting = new HashSet<IEntityType>();
        void Visit(IEntityType type)
        {
            if (ordered.Contains(type) || !visiting.Add(type))
            {
                return;
            }

            foreach (var dependent in types.Where(other => other != type && other.GetForeignKeys().Any(key => key.PrincipalEntityType == type)))
            {
                Visit(dependent);
            }

            ordered.Add(type);
        }

        foreach (var type in types)
        {
            Visit(type);
        }

        return ordered;
    }
}
