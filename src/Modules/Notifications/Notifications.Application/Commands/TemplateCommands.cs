using Notifications.Application.Abstractions;
using Notifications.Domain;

namespace Notifications.Application.Commands;

// Templates (§ 9-11, 35). Un restaurant ne voit et ne modifie que les SIENS ; ceux de la plateforme (routes
// d'administration) sont lus comme repli, jamais modifiés par un restaurant. « Supprimer » = archiver : une version
// publiée prouve ce qui a été envoyé.

/// <summary>Le propriétaire d'un template : le restaurant authentifié, ou la plateforme (routes d'administration).</summary>
public enum TemplateOwner
{
    Tenant = 1,
    Platform = 2,
}

internal static class TemplateOwners
{
    public static TenantId? Resolve(this NotificationsWorkspace workspace, TemplateOwner owner) =>
        owner == TemplateOwner.Platform ? null : workspace.CurrentTenant;
}

/// <summary>Crée la version 1 (Draft) d'un template pour (type, canal, langue).</summary>
public sealed record CreateTemplateCommand(
    TemplateOwner Owner, string Key, NotificationChannel Channel, string Language, string? Subject, string? Body) : ICommand<Guid>;

public sealed class CreateTemplateHandler(NotificationsWorkspace workspace, IClock clock) : ICommandHandler<CreateTemplateCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateTemplateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var owner = workspace.Resolve(command.Owner);
        if (owner is not null)
        {
            await workspace.RequireWritableTenantAsync(cancellationToken);
        }

        var key = TemplateKey.Create(command.Key);
        var language = NotificationLanguage.Create(command.Language);
        var unique = await workspace.Templates.IsKeyUniqueAsync(owner, key, command.Channel, language, cancellationToken);
        var template = owner is { } tenant
            ? NotificationTemplate.Create(tenant, key, command.Channel, language, command.Subject, command.Body, unique, clock)
            : NotificationTemplate.CreatePlatformTemplate(key, command.Channel, language, command.Subject, command.Body, unique, clock);
        workspace.Templates.Add(template);
        await workspace.SaveAsync(cancellationToken);
        return template.Id.Value;
    }
}

/// <summary>Modifie un Draft (une version publiée ne change pas : <see cref="CreateTemplateVersionCommand"/>).</summary>
public sealed record UpdateTemplateCommand(TemplateOwner Owner, Guid TemplateId, string? Subject, string? Body) : ICommand;

public sealed class UpdateTemplateHandler(NotificationsWorkspace workspace, IClock clock) : ICommandHandler<UpdateTemplateCommand>
{
    public Task HandleAsync(UpdateTemplateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return workspace.EditTemplateAsync(command.Owner, command.TemplateId, template => template.UpdateContent(command.Subject, command.Body, clock), cancellationToken);
    }
}

/// <summary>Une action sur un template de ce propriétaire.</summary>
public enum TemplateAction
{
    Publish = 1,
    Enable = 2,
    Disable = 3,

    /// <summary>« DELETE » : la version sort du service, pour toujours.</summary>
    Archive = 4,
}

public sealed record ChangeTemplateCommand(TemplateOwner Owner, Guid TemplateId, TemplateAction Action) : ICommand;

public sealed class ChangeTemplateHandler(NotificationsWorkspace workspace, IClock clock) : ICommandHandler<ChangeTemplateCommand>
{
    public Task HandleAsync(ChangeTemplateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return workspace.EditTemplateAsync(
            command.Owner,
            command.TemplateId,
            template =>
            {
                switch (command.Action)
                {
                    case TemplateAction.Publish:
                        template.Publish(clock);
                        break;
                    case TemplateAction.Enable:
                        template.Enable(clock);
                        break;
                    case TemplateAction.Disable:
                        template.Disable(clock);
                        break;
                    case TemplateAction.Archive:
                        template.Archive(clock);
                        break;
                    default:
                        throw new DomainException(NotificationsApplicationErrors.TemplateActionUnknown);
                }
            },
            cancellationToken);
    }
}

/// <summary>Démarre la version suivante d'un template publié (copie Draft) ; renvoie son id.</summary>
public sealed record CreateTemplateVersionCommand(TemplateOwner Owner, Guid TemplateId) : ICommand<Guid>;

public sealed class CreateTemplateVersionHandler(NotificationsWorkspace workspace, IClock clock) : ICommandHandler<CreateTemplateVersionCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateTemplateVersionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var owner = workspace.Resolve(command.Owner);
        if (owner is not null)
        {
            await workspace.RequireWritableTenantAsync(cancellationToken);
        }

        var current = await workspace.LoadTemplateAsync(owner, command.TemplateId, cancellationToken);
        var next = current.CreateNextVersion(
            await workspace.Templates.IsVersionUniqueAsync(owner, current.Key, current.Channel, current.Language, current.Version + 1, cancellationToken),
            clock);
        workspace.Templates.Add(next);
        await workspace.SaveAsync(cancellationToken);
        return next.Id.Value;
    }
}

internal static class TemplateEditing
{
    public static async Task EditTemplateAsync(
        this NotificationsWorkspace workspace, TemplateOwner owner, Guid id, Action<NotificationTemplate> change, CancellationToken cancellationToken)
    {
        var tenant = workspace.Resolve(owner);
        if (tenant is not null)
        {
            await workspace.RequireWritableTenantAsync(cancellationToken);
        }

        change(await workspace.LoadTemplateAsync(tenant, id, cancellationToken));
        await workspace.SaveAsync(cancellationToken);
    }
}
