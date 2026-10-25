using Storefront.Application.Abstractions;
using Storefront.Domain;

namespace Storefront.Application.Commands;

// Les applications de vente du restaurant (console, permission storefront.manage). La clé n'est renvoyée qu'ici, une
// fois ; aucune lecture ne la renvoie.

/// <summary>La clé publique émise (une seule fois) et l'application qui la porte.</summary>
public sealed record IssuedSalesAppKey(Guid Id, string Key)
{
    public override string ToString() => $"IssuedSalesAppKey {Id} (***)";
}

/// <summary>
/// Crée une application de vente. Le canal (site, application, borne) doit être une option du restaurant ; les
/// établissements cités sont du restaurant courant (aucun = tous).
/// </summary>
public sealed record CreateSalesAppCommand(
    string? Name,
    SalesAppKind Kind,
    IReadOnlyList<string>? AllowedOrigins = null,
    IReadOnlyList<Guid>? LocationIds = null,
    IReadOnlyList<string>? ReturnUrls = null,
    bool RequireHumanCheck = false) : ICommand<IssuedSalesAppKey>;

public sealed class CreateSalesAppHandler(StorefrontWorkspace workspace, IClock clock, IHumanCheck? humanCheck = null)
    : ICommandHandler<CreateSalesAppCommand, IssuedSalesAppKey>
{
    public async Task<IssuedSalesAppKey> HandleAsync(CreateSalesAppCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        DomainException.ThrowIf(!Enum.IsDefined(command.Kind), SalesAppErrors.KindInvalid);
        await workspace.RequireOptionAsync(command.Kind, cancellationToken);
        var locations = await workspace.RequireLocationsAsync(command.LocationIds, clock.UtcNow, cancellationToken);

        var key = PublishableKeys.NewKey();
        var app = SalesApp.Create(workspace.CurrentTenant, command.Name, command.Kind, PublishableKeys.Hash(key), PublishableKeys.Hint(key), clock);
        app.SetAllowedOrigins(command.AllowedOrigins, clock);
        app.SetLocations(locations, clock);
        app.SetReturnUrls(command.ReturnUrls, clock);
        HumanChecks.Apply(app, command.RequireHumanCheck, humanCheck, clock);
        workspace.Apps.Add(app);
        await workspace.SaveAsync(cancellationToken);
        return new IssuedSalesAppKey(app.Id.Value, key);
    }
}

/// <summary>Remplace le nom, les origines, les établissements et les pages de retour (le type ne change pas).</summary>
public sealed record UpdateSalesAppCommand(
    Guid AppId,
    string? Name,
    IReadOnlyList<string>? AllowedOrigins,
    IReadOnlyList<Guid>? LocationIds,
    IReadOnlyList<string>? ReturnUrls,
    bool? RequireHumanCheck = null) : ICommand;

public sealed class UpdateSalesAppHandler(StorefrontWorkspace workspace, IClock clock, IHumanCheck? humanCheck = null) : ICommandHandler<UpdateSalesAppCommand>
{
    public async Task HandleAsync(UpdateSalesAppCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var app = await workspace.LoadAppAsync(command.AppId, cancellationToken);
        var locations = await workspace.RequireLocationsAsync(command.LocationIds, clock.UtcNow, cancellationToken);
        app.Rename(command.Name, clock);
        app.SetAllowedOrigins(command.AllowedOrigins, clock);
        app.SetLocations(locations, clock);
        app.SetReturnUrls(command.ReturnUrls, clock);
        if (command.RequireHumanCheck is { } required)
        {
            HumanChecks.Apply(app, required, humanCheck, clock);
        }

        await workspace.SaveAsync(cancellationToken);
    }
}

internal static class HumanChecks
{
    /// <summary>On ne peut exiger le contrôle que si la plateforme l'a configuré ; on peut toujours le retirer.</summary>
    public static void Apply(SalesApp app, bool required, IHumanCheck? humanCheck, IClock clock)
    {
        DomainException.ThrowIf(required && humanCheck is not { IsAvailable: true }, StorefrontApplicationErrors.HumanCheckUnavailable);
        app.SetHumanCheck(required, clock);
    }
}

/// <summary>
/// Le contrôle anti-robot d'une requête d'application : exigé si l'application l'a activé ; le jeton du widget est
/// vérifié chez le prestataire. Sans application (requête non authentifiée par clé), rien n'est exigé ici.
/// </summary>
/// <summary>
/// Après l'authentification par clé publique : le plan permet-il ce canal (<c>storefront.web</c>, <c>mobile_app</c>,
/// <c>kiosk</c>) ? Sinon 403 <c>common.plan.feature_required</c>. Une borne gelée (au-delà de la limite) : 403
/// <c>common.plan.over_limit</c>.
/// </summary>
public sealed record EnsureStorefrontAccessCommand(SalesAppKind Channel) : ICommand;

public sealed class EnsureStorefrontAccessHandler(StorefrontWorkspace workspace, IStorefrontClient client, IKioskDeviceRepository devices)
    : ICommandHandler<EnsureStorefrontAccessCommand>
{
    public async Task HandleAsync(EnsureStorefrontAccessCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        if (!await workspace.Entitlements.AllowsAsync(tenant, command.Channel, cancellationToken))
        {
            throw PlanFeatures.Required(StorefrontFeatures.Of(command.Channel));
        }

        if (command.Channel == SalesAppKind.Kiosk && client.DeviceId is { } deviceId
            && (await Kiosks.KioskLimits.FrozenAsync(workspace, devices, tenant, cancellationToken)).Contains(deviceId))
        {
            throw PlanFeatures.OverLimit("kiosk");
        }
    }
}

/// <summary>Codes des droits des canaux de vente (catalogue Billing).</summary>
public static class StorefrontFeatures
{
    public const string Web = "storefront.web";
    public const string MobileApp = "storefront.mobile_app";
    public const string Kiosk = "storefront.kiosk";

    public static string Of(SalesAppKind channel) => channel switch
    {
        SalesAppKind.Web => Web,
        SalesAppKind.MobileApp => MobileApp,
        SalesAppKind.Kiosk => Kiosk,
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown sales channel."),
    };
}

public sealed record VerifyHumanCheckCommand(string? Token, string? RemoteIp) : ICommand
{
    public override string ToString() => "VerifyHumanCheckCommand (***)";
}

public sealed class VerifyHumanCheckHandler(StorefrontWorkspace workspace, IStorefrontClient client, IHumanCheck? humanCheck = null)
    : ICommandHandler<VerifyHumanCheckCommand>
{
    public async Task HandleAsync(VerifyHumanCheckCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (client.AppId is not { } appId || client.DeviceId is not null)
        {
            return;
        }

        var app = await workspace.Apps.FindAsync(workspace.CurrentTenant, appId, cancellationToken);
        if (app is not { RequiresHumanCheck: true })
        {
            return;
        }

        var passed = humanCheck is { IsAvailable: true } check && await check.VerifyAsync(command.Token, command.RemoteIp, cancellationToken);
        DomainException.ThrowIf(!passed, StorefrontApplicationErrors.HumanCheckFailed);
    }
}

/// <summary>Émet une nouvelle clé ; l'ancienne cesse aussitôt de fonctionner. Renvoie la nouvelle, une fois.</summary>
public sealed record RotateSalesAppKeyCommand(Guid AppId) : ICommand<IssuedSalesAppKey>;

public sealed class RotateSalesAppKeyHandler(StorefrontWorkspace workspace, IClock clock) : ICommandHandler<RotateSalesAppKeyCommand, IssuedSalesAppKey>
{
    public async Task<IssuedSalesAppKey> HandleAsync(RotateSalesAppKeyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var app = await workspace.LoadAppAsync(command.AppId, cancellationToken);
        var key = PublishableKeys.NewKey();
        app.RotateKey(PublishableKeys.Hash(key), PublishableKeys.Hint(key), clock);
        await workspace.SaveAsync(cancellationToken);
        return new IssuedSalesAppKey(app.Id.Value, key);
    }
}

/// <summary>Toujours possible, même pour un restaurant suspendu : couper un canal ne doit jamais être bloqué.</summary>
public sealed record DisableSalesAppCommand(Guid AppId) : ICommand;

public sealed class DisableSalesAppHandler(StorefrontWorkspace workspace, IClock clock) : ICommandHandler<DisableSalesAppCommand>
{
    public async Task HandleAsync(DisableSalesAppCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        (await workspace.LoadAppAsync(command.AppId, cancellationToken)).Disable(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Réactive l'application ; son canal doit toujours être une option du restaurant.</summary>
public sealed record EnableSalesAppCommand(Guid AppId) : ICommand;

public sealed class EnableSalesAppHandler(StorefrontWorkspace workspace, IClock clock) : ICommandHandler<EnableSalesAppCommand>
{
    public async Task HandleAsync(EnableSalesAppCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var app = await workspace.LoadAppAsync(command.AppId, cancellationToken);
        await workspace.RequireOptionAsync(app.Kind, cancellationToken);
        app.Enable(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}
