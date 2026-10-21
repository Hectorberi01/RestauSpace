using Printing.Application.Abstractions;
using Printing.Domain;

namespace Printing.Application.Commands;

/// <summary>Un jeton d'imprimante, renvoyé une seule fois (création, rotation).</summary>
public sealed record IssuedPrinterToken(Guid Id, string Token)
{
    public override string ToString() => $"IssuedPrinterToken({Id}, ***)";
}

/// <summary>Déclare une imprimante dans un établissement du restaurant ; renvoie son jeton, une seule fois.</summary>
public sealed record CreatePrinterCommand(Guid LocationId, string? Name, PrinterProtocol Protocol, bool PrintsCustomerTickets = true) : ICommand<IssuedPrinterToken>;

public sealed class CreatePrinterHandler(PrintingWorkspace workspace, IClock clock) : ICommandHandler<CreatePrinterCommand, IssuedPrinterToken>
{
    public async Task<IssuedPrinterToken> HandleAsync(CreatePrinterCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var location = await workspace.LoadLocationAsync(command.LocationId, cancellationToken);
        var token = PrinterTokens.New();
        var printer = Printer.Create(
            workspace.CurrentTenant, location.LocationId, command.Name, command.Protocol, PrinterTokens.Hash(token), PrinterTokens.Hint(token),
            command.PrintsCustomerTickets, clock);
        workspace.Printers.Add(printer);
        await workspace.SaveAsync(cancellationToken);
        return new IssuedPrinterToken(printer.Id.Value, token);
    }
}

public sealed record UpdatePrinterCommand(Guid PrinterId, string? Name, bool PrintsCustomerTickets) : ICommand;

public sealed class UpdatePrinterHandler(PrintingWorkspace workspace, IClock clock) : ICommandHandler<UpdatePrinterCommand>
{
    public async Task HandleAsync(UpdatePrinterCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        (await workspace.LoadPrinterAsync(command.PrinterId, cancellationToken)).Update(command.Name, command.PrintsCustomerTickets, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Nouveau jeton (l'ancien cesse aussitôt de fonctionner) ; renvoyé une seule fois.</summary>
public sealed record RotatePrinterTokenCommand(Guid PrinterId) : ICommand<IssuedPrinterToken>;

public sealed class RotatePrinterTokenHandler(PrintingWorkspace workspace, IClock clock) : ICommandHandler<RotatePrinterTokenCommand, IssuedPrinterToken>
{
    public async Task<IssuedPrinterToken> HandleAsync(RotatePrinterTokenCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var printer = await workspace.LoadPrinterAsync(command.PrinterId, cancellationToken);
        var token = PrinterTokens.New();
        printer.RotateToken(PrinterTokens.Hash(token), PrinterTokens.Hint(token), clock);
        await workspace.SaveAsync(cancellationToken);
        return new IssuedPrinterToken(printer.Id.Value, token);
    }
}

/// <summary>Couper (vrai) ou rétablir (faux) une imprimante. Couper est toujours possible.</summary>
public sealed record SetPrinterDisabledCommand(Guid PrinterId, bool Disabled) : ICommand;

public sealed class SetPrinterDisabledHandler(PrintingWorkspace workspace, IClock clock) : ICommandHandler<SetPrinterDisabledCommand>
{
    public async Task HandleAsync(SetPrinterDisabledCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!command.Disabled)
        {
            await workspace.RequireWritableTenantAsync(cancellationToken);
        }

        var printer = await workspace.LoadPrinterAsync(command.PrinterId, cancellationToken);
        if (command.Disabled)
        {
            printer.Disable(clock);
        }
        else
        {
            printer.Enable(clock);
        }

        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Page de test : l'enseigne du ticket, l'établissement, l'heure et un code-barres.</summary>
public sealed record PrintTestPageCommand(Guid PrinterId) : ICommand<Guid>;

public sealed class PrintTestPageHandler(PrintingWorkspace workspace, IClock clock) : ICommandHandler<PrintTestPageCommand, Guid>
{
    public async Task<Guid> HandleAsync(PrintTestPageCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var printer = await workspace.LoadPrinterAsync(command.PrinterId, cancellationToken);
        var location = await workspace.LoadLocationAsync(printer.LocationId.Value, cancellationToken);
        var document = TicketDocument.Create(
        [
            new TicketLine(TicketLineKind.Text, "PAGE DE TEST", TicketAlign.Center, Bold: true, Large: true),
            new TicketLine(TicketLineKind.Text, location.Name, TicketAlign.Center),
            new TicketLine(TicketLineKind.Text, printer.Name, TicketAlign.Center),
            new TicketLine(TicketLineKind.Separator),
            new TicketLine(TicketLineKind.Text, "Accents : é è à ç ô œ €", Right: "OK"),
            new TicketLine(TicketLineKind.Text, "Gras", Bold: true),
            new TicketLine(TicketLineKind.Barcode, "TEST-0001", TicketAlign.Center),
            new TicketLine(TicketLineKind.Feed),
        ]);
        var job = PrintJob.Create(printer, PrintJobKind.TestPage, $"test-{clock.UtcNow:yyyyMMddHHmmssfff}", document, clock);
        workspace.Jobs.Add(job);
        await workspace.SaveAsync(cancellationToken);
        return job.Id.Value;
    }
}

/// <summary>Réimprime un ticket déjà imprimé ou en échec, sur la même imprimante ; renvoie le nouveau ticket.</summary>
public sealed record ReprintJobCommand(Guid JobId) : ICommand<Guid>;

public sealed class ReprintJobHandler(PrintingWorkspace workspace, IClock clock) : ICommandHandler<ReprintJobCommand, Guid>
{
    public async Task<Guid> HandleAsync(ReprintJobCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var original = (command.JobId == Guid.Empty ? null : await workspace.Jobs.FindAsync(tenant, PrintJobId.From(command.JobId), cancellationToken))
            ?? throw new NotFoundException(PrintingApplicationErrors.JobNotFound);
        var printer = await workspace.LoadPrinterAsync(original.PrinterId.Value, cancellationToken);
        var reference = $"{original.Reference[..Math.Min(original.Reference.Length, 60)]}/r{clock.UtcNow:HHmmssfff}";
        var job = PrintJob.Create(printer, original.Kind, reference, original.Document, clock);
        workspace.Jobs.Add(job);
        await workspace.SaveAsync(cancellationToken);
        return job.Id.Value;
    }
}

/// <summary>Une ligne du ticket client : quantité, désignation (variante comprise), détails (options) et montant formaté.</summary>
public sealed record CustomerTicketLine(int Quantity, string Name, string? Details, string Amount);

/// <summary>
/// Le ticket remis au client (borne) : textes déjà formatés par l'appelant (montants, heure locale). Aucune donnée
/// personnelle : ni nom, ni téléphone.
/// </summary>
/// <param name="PaymentText">« À PAYER AU COMPTOIR » ou « PAYÉE » ; imprimé en gras.</param>
public sealed record CustomerTicketInput(
    string RestaurantName,
    string LocationName,
    string OrderNumber,
    string ServiceMode,
    IReadOnlyList<CustomerTicketLine> Lines,
    string Total,
    string PaymentText,
    string PrintedAt);

/// <summary>
/// Pour l'hôte (commande passée à une borne) : le ticket client part sur l'imprimante active de l'établissement chargée
/// des tickets client. Idempotent par <paramref name="Reference"/> (la commande). Null quand aucune imprimante n'est prête.
/// </summary>
public sealed record PrintCustomerTicketCommand(Guid LocationId, string Reference, CustomerTicketInput Ticket) : ICommand<Guid?>;

public sealed class PrintCustomerTicketHandler(PrintingWorkspace workspace, IClock clock) : ICommandHandler<PrintCustomerTicketCommand, Guid?>
{
    public async Task<Guid?> HandleAsync(PrintCustomerTicketCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Ticket);
        var tenant = workspace.CurrentTenant;
        if (command.LocationId == Guid.Empty)
        {
            return null;
        }

        var printer = (await workspace.Printers.ListAsync(tenant, LocationId.From(command.LocationId), cancellationToken))
            .Where(item => item is { IsActive: true, PrintsCustomerTickets: true })
            .OrderByDescending(item => item.LastSeenAtUtc ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
        if (printer is null)
        {
            return null;
        }

        var existing = await workspace.Jobs.FindByReferenceAsync(tenant, printer.Id, PrintJobKind.CustomerTicket, command.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing.Id.Value;
        }

        var job = PrintJob.Create(printer, PrintJobKind.CustomerTicket, command.Reference, CustomerTickets.Build(command.Ticket), clock);
        workspace.Jobs.Add(job);
        await workspace.SaveAsync(cancellationToken);
        return job.Id.Value;
    }
}

public static class CustomerTickets
{
    private static string Cut(string? text, int length = TicketDocument.MaxTextLength) =>
        new string((text ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim() is var clean && clean.Length > length ? clean[..length] : clean;

    public static TicketDocument Build(CustomerTicketInput ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var lines = new List<TicketLine>
        {
            new(TicketLineKind.Text, Cut(ticket.RestaurantName), TicketAlign.Center, Bold: true),
            new(TicketLineKind.Text, Cut(ticket.LocationName), TicketAlign.Center),
            new(TicketLineKind.Feed),
            new(TicketLineKind.Text, "COMMANDE", TicketAlign.Center),
            new(TicketLineKind.Text, Cut(ticket.OrderNumber, 24), TicketAlign.Center, Bold: true, Large: true),
            new(TicketLineKind.Text, Cut(ticket.ServiceMode).ToUpperInvariant(), TicketAlign.Center, Bold: true),
            new(TicketLineKind.Separator),
        };

        foreach (var line in ticket.Lines)
        {
            lines.Add(new TicketLine(TicketLineKind.Text, Cut($"{line.Quantity} x {line.Name}", 100), Right: Cut(line.Amount, 18)));
            if (!string.IsNullOrWhiteSpace(line.Details))
            {
                lines.Add(new TicketLine(TicketLineKind.Text, "   " + Cut(line.Details, TicketDocument.MaxTextLength - 3)));
            }
        }

        lines.Add(new TicketLine(TicketLineKind.Separator));
        lines.Add(new TicketLine(TicketLineKind.Text, "TOTAL", Bold: true, Right: Cut(ticket.Total, 18)));
        lines.Add(new TicketLine(TicketLineKind.Feed));
        lines.Add(new TicketLine(TicketLineKind.Text, Cut(ticket.PaymentText), TicketAlign.Center, Bold: true, Large: true));
        lines.Add(new TicketLine(TicketLineKind.Feed));
        if (TicketDocument.IsBarcodeText(ticket.OrderNumber))
        {
            lines.Add(new TicketLine(TicketLineKind.Barcode, ticket.OrderNumber, TicketAlign.Center));
        }

        lines.Add(new TicketLine(TicketLineKind.Text, Cut(ticket.PrintedAt), TicketAlign.Center));
        lines.Add(new TicketLine(TicketLineKind.Feed));
        return TicketDocument.Create(lines);
    }
}

// ---- Côté imprimante (jeton authentifié) : le tenant et l'imprimante viennent du jeton.

/// <summary>Imprimante authentifiée par son jeton : son tenant et son id. Null : refusée.</summary>
public sealed record PrinterPrincipal(Guid TenantId, Guid PrinterId, PrinterProtocol Protocol);

public sealed record AuthenticatePrinterQuery(string? Token) : IQuery<PrinterPrincipal?>
{
    public override string ToString() => "AuthenticatePrinterQuery (***)";
}

public sealed class AuthenticatePrinterHandler(IPrinterTokenLookup lookup, IPrintingTenants tenants) : IQueryHandler<AuthenticatePrinterQuery, PrinterPrincipal?>
{
    public async Task<PrinterPrincipal?> HandleAsync(AuthenticatePrinterQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!PrinterTokens.LooksValid(query.Token))
        {
            return null;
        }

        var printer = await lookup.FindByTokenHashAsync(PrinterTokens.Hash(query.Token!), cancellationToken);
        return printer is { IsActive: true } && await tenants.AcceptsBusinessWritesAsync(printer.TenantId, cancellationToken)
            ? new PrinterPrincipal(printer.TenantId.Value, printer.Id.Value, printer.Protocol)
            : null;
    }
}

/// <summary>Le prochain ticket à remettre, sans le remettre ; l'imprimante est marquée présente. Null : rien à imprimer.</summary>
public sealed record PeekPrintJobCommand : ICommand<Guid?>;

public sealed class PeekPrintJobHandler(PrintingWorkspace workspace, IPrinterClient client, IClock clock) : ICommandHandler<PeekPrintJobCommand, Guid?>
{
    public async Task<Guid?> HandleAsync(PeekPrintJobCommand command, CancellationToken cancellationToken = default)
    {
        var printer = await PrinterSide.CurrentPrinterAsync(workspace, client, cancellationToken);
        var jobs = await workspace.Jobs.ListOpenAsync(workspace.CurrentTenant, printer.Id, 20, cancellationToken);
        var changed = printer.MarkSeen(clock);
        foreach (var job in jobs)
        {
            changed |= job.ExpireIfStale(clock);
        }

        if (changed)
        {
            await workspace.SaveAsync(cancellationToken);
        }

        return jobs.FirstOrDefault(job => job.IsDue(clock.UtcNow))?.Id.Value;
    }
}

/// <summary>Le ticket rendu dans le format demandé ; il est marqué remis (il le sera à nouveau sans accusé).</summary>
public sealed record FetchPrintJobCommand(Guid JobId, string Format) : ICommand<PrintJobContent?>;

public sealed record PrintJobContent(Guid JobId, string ContentType, byte[] Content);

public sealed class FetchPrintJobHandler(PrintingWorkspace workspace, IPrinterClient client, IClock clock) : ICommandHandler<FetchPrintJobCommand, PrintJobContent?>
{
    public async Task<PrintJobContent?> HandleAsync(FetchPrintJobCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var printer = await PrinterSide.CurrentPrinterAsync(workspace, client, cancellationToken);
        var job = command.JobId == Guid.Empty ? null : await workspace.Jobs.FindAsync(workspace.CurrentTenant, PrintJobId.From(command.JobId), cancellationToken);
        if (job is null || job.PrinterId != printer.Id || !job.IsDue(clock.UtcNow))
        {
            return null;
        }

        job.MarkSent(clock);
        await workspace.SaveAsync(cancellationToken);
        var format = command.Format is Rendering.TicketFormats.StarPrnt or Rendering.TicketFormats.EscPos ? command.Format : Rendering.TicketFormats.PlainText;
        return new PrintJobContent(job.Id.Value, format, Rendering.TicketRenderer.Render(job.Document, format));
    }
}

/// <summary>Accusé de l'imprimante (ou du relais) pour un ticket remis : imprimé ou non, avec son code.</summary>
public sealed record CompletePrintJobCommand(Guid JobId, bool Printed, string? Code = null) : ICommand;

public sealed class CompletePrintJobHandler(PrintingWorkspace workspace, IPrinterClient client, IClock clock) : ICommandHandler<CompletePrintJobCommand>
{
    public async Task HandleAsync(CompletePrintJobCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var printer = await PrinterSide.CurrentPrinterAsync(workspace, client, cancellationToken);
        var job = (command.JobId == Guid.Empty ? null : await workspace.Jobs.FindAsync(workspace.CurrentTenant, PrintJobId.From(command.JobId), cancellationToken))
            ?? throw new NotFoundException(PrintingApplicationErrors.JobNotFound);
        if (job.PrinterId != printer.Id)
        {
            throw new NotFoundException(PrintingApplicationErrors.JobNotFound);
        }

        job.Complete(command.Printed, command.Code, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

internal static class PrinterSide
{
    public static async Task<Printer> CurrentPrinterAsync(PrintingWorkspace workspace, IPrinterClient client, CancellationToken cancellationToken)
    {
        var id = client.PrinterId ?? throw new DomainException(PrintingApplicationErrors.PrinterRequired);
        var printer = await workspace.Printers.FindAsync(workspace.CurrentTenant, id, cancellationToken);
        return printer is { IsActive: true } ? printer : throw new NotFoundException(PrintingApplicationErrors.PrinterNotFound);
    }
}
