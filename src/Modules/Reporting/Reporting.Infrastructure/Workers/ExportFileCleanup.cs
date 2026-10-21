using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Reporting.Application.Commands;

namespace Reporting.Infrastructure.Workers;

internal sealed class ExportFileCleanup(IServiceScopeFactory scopes) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if ((message.Module, message.Type) is not (DependencyInjection.Schema, "ExportExpired"))
        {
            return;
        }

        using var content = JsonDocument.Parse(message.Content);
        var root = content.RootElement;
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(root.GetProperty("TenantId").GetGuid()));
        await scope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteExpiredExportFileCommand, bool>>()
            .HandleAsync(new DeleteExpiredExportFileCommand(root.GetProperty("ExportJobId").GetGuid()), cancellationToken);
    }
}
