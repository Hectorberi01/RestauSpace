using System.Security.Cryptography;
using Floor.Application.Abstractions;
using Floor.Domain;

namespace Floor.Application.Commands;

/// <summary>Jeton de QR : 16 octets aléatoires en base64url (22 caractères), sans rien de la table.</summary>
public static class TableQrTokenFactory
{
    public static string New() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool LooksValid(string? token) =>
        token is { Length: >= 16 and <= TableQrTokens.MaxLength } && token.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

public sealed record TableQrDto(Guid TableId, Guid DiningAreaId, string AreaName, string Label, string Token);

/// <summary>Les QR des tables d'un établissement, à imprimer : chaque table sans jeton en reçoit un.</summary>
public sealed record PrepareTableQrCodesCommand(Guid LocationId) : ICommand<IReadOnlyList<TableQrDto>>;

public sealed class PrepareTableQrCodesHandler(FloorWorkspace workspace) : ICommandHandler<PrepareTableQrCodesCommand, IReadOnlyList<TableQrDto>>
{
    public async Task<IReadOnlyList<TableQrDto>> HandleAsync(PrepareTableQrCodesCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        if (command.LocationId == Guid.Empty)
        {
            return [];
        }

        var areas = await workspace.DiningAreas.ListForUpdateAsync(workspace.CurrentTenant, LocationId.From(command.LocationId), cancellationToken);
        if (areas.Sum(area => area.EnsureQrTokens(TableQrTokenFactory.New)) > 0)
        {
            await workspace.SaveAsync(cancellationToken);
        }

        return areas
            .SelectMany(area => area.Tables.Select(table => new TableQrDto(table.Id.Value, area.Id.Value, area.Name, table.Label.Value, table.QrToken!)))
            .ToList();
    }
}

/// <summary>Nouveau QR pour une table (ancien QR perdu ou copié) : l'ancien ne commande plus rien.</summary>
public sealed record RotateTableQrTokenCommand(Guid TableId) : ICommand<TableQrDto>;

public sealed class RotateTableQrTokenHandler(FloorWorkspace workspace) : ICommandHandler<RotateTableQrTokenCommand, TableQrDto>
{
    public async Task<TableQrDto> HandleAsync(RotateTableQrTokenCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var area = await workspace.LoadDiningAreaOfTableAsync(workspace.CurrentTenant, command.TableId, cancellationToken);
        var tableId = TableId.From(command.TableId);
        var token = TableQrTokenFactory.New();
        area.RotateQrToken(tableId, token);
        await workspace.SaveAsync(cancellationToken);
        var table = area.Tables.Single(item => item.Id == tableId);
        return new TableQrDto(table.Id.Value, area.Id.Value, area.Name, table.Label.Value, token);
    }
}

/// <param name="OpenSessionId">Session en cours sur la table ; null si elle est libre.</param>
public sealed record TableByQrDto(Guid TableId, Guid LocationId, string Label, string AreaName, bool IsInService, Guid? OpenSessionId);

/// <summary>Pour la commande à table (via l'hôte) : la table du tenant courant qui porte ce jeton ; null sinon.</summary>
public sealed record FindTableByQrTokenQuery(string? Token) : IQuery<TableByQrDto?>
{
    public override string ToString() => "FindTableByQrTokenQuery (***)";
}

public sealed class FindTableByQrTokenHandler(FloorWorkspace workspace) : IQueryHandler<FindTableByQrTokenQuery, TableByQrDto?>
{
    public async Task<TableByQrDto?> HandleAsync(FindTableByQrTokenQuery query, CancellationToken cancellationToken = default)
    {
        if (!TableQrTokenFactory.LooksValid(query.Token))
        {
            return null;
        }

        var tenant = workspace.CurrentTenant;
        var area = await workspace.DiningAreas.FindByQrTokenAsync(tenant, query.Token!, cancellationToken);
        var table = area?.Tables.SingleOrDefault(item => item.QrToken == query.Token);
        if (area is null || table is null)
        {
            return null;
        }

        var open = (await workspace.Sessions.ListOpenAsync(tenant, area.LocationId, cancellationToken)).FirstOrDefault(session => session.TableId == table.Id);
        return new TableByQrDto(table.Id.Value, area.LocationId.Value, table.Label.Value, area.Name, table.IsInService, open?.Id.Value);
    }
}
