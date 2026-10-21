using Printing.Application.Abstractions;
using Printing.Domain;

namespace Printing.Application.Commands;

// Bons de cuisine (automatiques, à l'acceptation d'une commande) et reçus demandés depuis la console. Le contenu vient
// des autres modules par l'hôte, déjà mis en forme (textes et montants) : Printing ne connaît ni commandes ni prix.
// Aucune donnée personnelle du client sur un bon ni sur un reçu (décision T16).

/// <summary>Rôle d'une imprimante en cuisine : imprimer les bons, éventuellement pour un seul poste (bar, grill...).</summary>
public sealed record SetPrinterKitchenRoleCommand(Guid PrinterId, bool PrintsKitchenTickets, Guid? KitchenStationId) : ICommand;

public sealed class SetPrinterKitchenRoleHandler(PrintingWorkspace workspace, IClock clock) : ICommandHandler<SetPrinterKitchenRoleCommand>
{
    public async Task HandleAsync(SetPrinterKitchenRoleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        (await workspace.LoadPrinterAsync(command.PrinterId, cancellationToken)).SetKitchenRole(command.PrintsKitchenTickets, command.KitchenStationId, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <param name="StationId">Poste vers lequel la ligne est routée.</param>
/// <param name="Details">Variante et options, une par ligne.</param>
public sealed record KitchenTicketItemInput(Guid StationId, int Quantity, string Name, IReadOnlyList<string> Details, string? Note);

/// <param name="ServiceMode">« Sur place », « À emporter », « Livraison ».</param>
/// <param name="StationNames">Nom de chaque poste, pour l'en-tête d'un bon limité à un poste.</param>
public sealed record KitchenTicketInput(
    string OrderNumber,
    string ServiceMode,
    bool Urgent,
    IReadOnlyList<KitchenTicketItemInput> Items,
    IReadOnlyDictionary<Guid, string> StationNames,
    string PrintedAt);

/// <summary>
/// Un bon par imprimante de cuisine active de l'établissement ; une imprimante liée à un poste ne reçoit que les lignes de
/// ce poste (rien s'il n'en a aucune). Idempotent par <paramref name="Reference"/> et par imprimante. Renvoie le nombre de
/// bons mis en file.
/// </summary>
public sealed record PrintKitchenTicketCommand(Guid LocationId, string Reference, KitchenTicketInput Ticket) : ICommand<int>;

public sealed class PrintKitchenTicketHandler(PrintingWorkspace workspace, IClock clock) : ICommandHandler<PrintKitchenTicketCommand, int>
{
    public async Task<int> HandleAsync(PrintKitchenTicketCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Ticket);
        var tenant = workspace.CurrentTenant;
        if (command.LocationId == Guid.Empty || command.Ticket.Items.Count == 0)
        {
            return 0;
        }

        var printers = (await workspace.Printers.ListAsync(tenant, LocationId.From(command.LocationId), cancellationToken))
            .Where(printer => printer is { IsActive: true, PrintsKitchenTickets: true })
            .ToList();
        var queued = 0;
        foreach (var printer in printers)
        {
            var items = command.Ticket.Items.Where(item => printer.KitchenStationId is not { } station || item.StationId == station).ToList();
            if (items.Count == 0
                || await workspace.Jobs.FindByReferenceAsync(tenant, printer.Id, PrintJobKind.KitchenTicket, command.Reference, cancellationToken) is not null)
            {
                continue;
            }

            var station = printer.KitchenStationId is { } id ? command.Ticket.StationNames.GetValueOrDefault(id) : null;
            workspace.Jobs.Add(PrintJob.Create(printer, PrintJobKind.KitchenTicket, command.Reference, KitchenTickets.Build(command.Ticket, items, station), clock));
            queued++;
        }

        if (queued > 0)
        {
            await workspace.SaveAsync(cancellationToken);
        }

        return queued;
    }
}

public static class KitchenTickets
{
    public static TicketDocument Build(KitchenTicketInput ticket, IReadOnlyList<KitchenTicketItemInput> items, string? station)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(items);
        var lines = new List<TicketLine>
        {
            new(TicketLineKind.Text, Text.Cut(station ?? "CUISINE").ToUpperInvariant(), TicketAlign.Center, Bold: true),
            new(TicketLineKind.Text, Text.Cut(ticket.OrderNumber, 24), TicketAlign.Center, Bold: true, Large: true),
            new(TicketLineKind.Text, Text.Cut(ticket.ServiceMode).ToUpperInvariant(), TicketAlign.Center, Bold: true),
        };
        if (ticket.Urgent)
        {
            lines.Add(new TicketLine(TicketLineKind.Text, "*** URGENT ***", TicketAlign.Center, Bold: true, Large: true));
        }

        lines.Add(new TicketLine(TicketLineKind.Text, Text.Cut(ticket.PrintedAt), TicketAlign.Center));
        lines.Add(new TicketLine(TicketLineKind.Separator));
        foreach (var item in items.Take(100))
        {
            lines.Add(new TicketLine(TicketLineKind.Text, Text.Cut($"{item.Quantity} x {item.Name}", 100), Bold: true, Large: true));
            foreach (var detail in item.Details.Take(10))
            {
                lines.Add(new TicketLine(TicketLineKind.Text, "   + " + Text.Cut(detail, TicketDocument.MaxTextLength - 5)));
            }

            if (!string.IsNullOrWhiteSpace(item.Note))
            {
                lines.Add(new TicketLine(TicketLineKind.Text, "   ! " + Text.Cut(item.Note, TicketDocument.MaxTextLength - 5), Bold: true));
            }
        }

        lines.Add(new TicketLine(TicketLineKind.Separator));
        lines.Add(new TicketLine(TicketLineKind.Text, $"{items.Sum(item => item.Quantity)} article(s)", TicketAlign.Center));
        lines.Add(new TicketLine(TicketLineKind.Feed));
        return TicketDocument.Create(lines);
    }
}

/// <param name="Label">Texte de gauche (sous-total, remise, moyen de paiement...).</param>
public sealed record ReceiptLine(string Label, string Amount, bool Bold = false);

/// <summary>
/// Reçu d'une commande, mis en forme par l'hôte. <see cref="Footer"/> : mentions de bas de ticket (mentions fiscales) ;
/// <see cref="QrCode"/> : QR code de la facture normalisée, s'il y en a une.
/// </summary>
public sealed record OrderReceiptInput(
    Guid LocationId,
    string RestaurantName,
    string LocationName,
    string? Address,
    string OrderNumber,
    string ServiceMode,
    string Date,
    IReadOnlyList<CustomerTicketLine> Lines,
    IReadOnlyList<ReceiptLine> Totals,
    string Total,
    IReadOnlyList<ReceiptLine> Payments,
    IReadOnlyList<string> Footer,
    string? QrCode = null);

/// <summary>Le reçu d'une commande du tenant courant, fourni par l'hôte (Orders, Payments, Tenants, Locations) ; null si elle n'existe pas.</summary>
public interface IPrintingReceipts
{
    Task<OrderReceiptInput?> FindAsync(TenantId tenantId, Guid orderId, CancellationToken cancellationToken = default);
}

public static class ReceiptErrors
{
    public static readonly DomainError OrderNotFound = new("printing.receipt.order_not_found", "The order does not exist.");
    public static readonly DomainError NoPrinter = new("printing.receipt.no_printer", "No active receipt printer in the location of this order.");
    public static readonly DomainError PrinterElsewhere = new("printing.receipt.printer_elsewhere", "This printer is disabled or belongs to another location.");
}

/// <summary>
/// Imprime le reçu d'une commande sur l'imprimante donnée, ou à défaut sur l'imprimante de tickets client de son
/// établissement vue le plus récemment. Chaque demande crée un ticket (réimpression possible). Renvoie l'id du ticket.
/// </summary>
public sealed record PrintOrderReceiptCommand(Guid OrderId, Guid? PrinterId) : ICommand<Guid>;

public sealed class PrintOrderReceiptHandler(PrintingWorkspace workspace, IPrintingReceipts receipts, IClock clock) : ICommandHandler<PrintOrderReceiptCommand, Guid>
{
    public async Task<Guid> HandleAsync(PrintOrderReceiptCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var receipt = (command.OrderId == Guid.Empty ? null : await receipts.FindAsync(tenant, command.OrderId, cancellationToken))
            ?? throw new NotFoundException(ReceiptErrors.OrderNotFound);
        var location = LocationId.From(receipt.LocationId);

        Printer printer;
        if (command.PrinterId is { } chosen)
        {
            printer = await workspace.LoadPrinterAsync(chosen, cancellationToken);
            DomainException.ThrowIf(!printer.IsActive || printer.LocationId != location, ReceiptErrors.PrinterElsewhere);
        }
        else
        {
            printer = (await workspace.Printers.ListAsync(tenant, location, cancellationToken))
                .Where(item => item is { IsActive: true, PrintsCustomerTickets: true })
                .OrderByDescending(item => item.LastSeenAtUtc ?? DateTimeOffset.MinValue)
                .FirstOrDefault() ?? throw new DomainException(ReceiptErrors.NoPrinter);
        }

        var job = PrintJob.Create(printer, PrintJobKind.OrderReceipt, $"receipt-{command.OrderId:N}-{clock.UtcNow:HHmmssfff}", Receipts.Build(receipt), clock);
        workspace.Jobs.Add(job);
        await workspace.SaveAsync(cancellationToken);
        return job.Id.Value;
    }
}

public static class Receipts
{
    public static TicketDocument Build(OrderReceiptInput receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var lines = new List<TicketLine>
        {
            new(TicketLineKind.Text, Text.Cut(receipt.RestaurantName), TicketAlign.Center, Bold: true, Large: true),
            new(TicketLineKind.Text, Text.Cut(receipt.LocationName), TicketAlign.Center),
        };
        if (!string.IsNullOrWhiteSpace(receipt.Address))
        {
            lines.Add(new TicketLine(TicketLineKind.Text, Text.Cut(receipt.Address), TicketAlign.Center));
        }

        lines.Add(new TicketLine(TicketLineKind.Separator));
        lines.Add(new TicketLine(TicketLineKind.Text, "Reçu n°", Right: Text.Cut(receipt.OrderNumber, 30)));
        lines.Add(new TicketLine(TicketLineKind.Text, "Date", Right: Text.Cut(receipt.Date, 30)));
        lines.Add(new TicketLine(TicketLineKind.Text, "Type", Right: Text.Cut(receipt.ServiceMode, 30)));
        lines.Add(new TicketLine(TicketLineKind.Separator));
        foreach (var line in receipt.Lines.Take(150))
        {
            lines.Add(new TicketLine(TicketLineKind.Text, Text.Cut($"{line.Quantity} x {line.Name}", 100), Right: Text.Cut(line.Amount, 18)));
            if (!string.IsNullOrWhiteSpace(line.Details))
            {
                lines.Add(new TicketLine(TicketLineKind.Text, "   " + Text.Cut(line.Details, TicketDocument.MaxTextLength - 3)));
            }
        }

        lines.Add(new TicketLine(TicketLineKind.Separator));
        lines.AddRange(receipt.Totals.Take(20).Select(total => new TicketLine(TicketLineKind.Text, Text.Cut(total.Label, 60), Bold: total.Bold, Right: Text.Cut(total.Amount, 18))));
        lines.Add(new TicketLine(TicketLineKind.Text, "TOTAL", Bold: true, Large: true, Right: Text.Cut(receipt.Total, 18)));
        if (receipt.Payments.Count > 0)
        {
            lines.Add(new TicketLine(TicketLineKind.Feed));
            lines.AddRange(receipt.Payments.Take(20).Select(payment => new TicketLine(TicketLineKind.Text, Text.Cut(payment.Label, 60), Right: Text.Cut(payment.Amount, 18))));
        }

        lines.Add(new TicketLine(TicketLineKind.Feed));
        if (TicketDocument.IsBarcodeText(receipt.OrderNumber))
        {
            lines.Add(new TicketLine(TicketLineKind.Barcode, receipt.OrderNumber, TicketAlign.Center));
        }

        if (receipt.Footer.Count > 0)
        {
            lines.Add(new TicketLine(TicketLineKind.Separator));
            lines.AddRange(receipt.Footer.Take(12).Select(text => new TicketLine(TicketLineKind.Text, Text.Cut(text), TicketAlign.Center)));
        }

        if (!string.IsNullOrWhiteSpace(receipt.QrCode) && receipt.QrCode.Length <= TicketDocument.MaxQrLength)
        {
            lines.Add(new TicketLine(TicketLineKind.QrCode, receipt.QrCode.Trim(), TicketAlign.Center));
        }

        lines.Add(new TicketLine(TicketLineKind.Text, "Merci de votre visite", TicketAlign.Center));
        lines.Add(new TicketLine(TicketLineKind.Feed));
        return TicketDocument.Create(lines);
    }
}

internal static class Text
{
    public static string Cut(string? text, int length = TicketDocument.MaxTextLength)
    {
        var clean = new string((text ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length > length ? clean[..length] : clean;
    }
}
