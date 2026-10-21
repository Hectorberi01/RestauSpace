using Printing.Application.Commands;
using Printing.Application.Rendering;
using Printing.Domain;

namespace Printing.Application;

public sealed class KitchenAndReceiptTests
{
    private readonly PrintingFixture _fixture = new();
    private readonly FakeReceipts _receipts = new();
    private static readonly Guid Grill = Guid.NewGuid();
    private static readonly Guid Bar = Guid.NewGuid();

    private KitchenTicketInput Ticket => new(
        "20261013-012",
        "Sur place",
        Urgent: true,
        [
            new KitchenTicketItemInput(Grill, 2, "Poulet DG", ["Grande", "Piment"], "Bien cuit"),
            new KitchenTicketItemInput(Bar, 1, "Bissap", [], null),
        ],
        new Dictionary<Guid, string> { [Grill] = "Grill", [Bar] = "Bar" },
        "13/10/2026 12:30");

    private async Task<Printer> PrinterAsync(string name, bool customerTickets, bool kitchen, Guid? station = null, LocationId? location = null)
    {
        var issued = await _fixture.Create.HandleAsync(new CreatePrinterCommand((location ?? _fixture.Cotonou).Value, name, PrinterProtocol.EscPosRelay, customerTickets));
        if (kitchen)
        {
            await new SetPrinterKitchenRoleHandler(_fixture.Workspace, _fixture.Clock).HandleAsync(new SetPrinterKitchenRoleCommand(issued.Id, true, station));
        }

        return _fixture.Printers.Single(printer => printer.Id.Value == issued.Id);
    }

    private static string Text(PrintJob job) => TicketRenderer.PlainText(job.Document);

    [Fact]
    public async Task Each_kitchen_printer_gets_its_own_lines_once()
    {
        var kitchen = await PrinterAsync("Cuisine", false, true);
        var bar = await PrinterAsync("Bar", false, true, Bar);
        await PrinterAsync("Comptoir", true, false);
        var command = new PrintKitchenTicketCommand(_fixture.Cotonou.Value, "kitchen-1", Ticket);
        var handler = new PrintKitchenTicketHandler(_fixture.Workspace, _fixture.Clock);

        var first = await handler.HandleAsync(command);
        var again = await handler.HandleAsync(command);

        Assert.Equal((2, 0), (first, again));
        var all = Text(_fixture.Jobs.Single(job => job.PrinterId == kitchen.Id));
        var drinks = Text(_fixture.Jobs.Single(job => job.PrinterId == bar.Id));
        Assert.Contains("2 x Poulet DG", all);
        Assert.Contains("+ Piment", all);
        Assert.Contains("! Bien cuit", all);
        Assert.Contains("URGENT", all);
        Assert.Contains("BAR", drinks);
        Assert.DoesNotContain("Poulet", drinks);
        Assert.All(_fixture.Jobs, job => Assert.Equal(PrintJobKind.KitchenTicket, job.Kind));
    }

    [Fact]
    public async Task A_station_printer_without_lines_for_its_station_prints_nothing()
    {
        await PrinterAsync("Desserts", false, true, Guid.NewGuid());

        var queued = await new PrintKitchenTicketHandler(_fixture.Workspace, _fixture.Clock)
            .HandleAsync(new PrintKitchenTicketCommand(_fixture.Cotonou.Value, "kitchen-2", Ticket));

        Assert.Equal(0, queued);
        Assert.Empty(_fixture.Jobs);
    }

    [Fact]
    public async Task A_station_is_chosen_only_for_a_kitchen_printer()
    {
        var printer = await PrinterAsync("Comptoir", true, false);

        var refused = Assert.Throws<DomainException>(() => printer.SetKitchenRole(false, Guid.NewGuid(), _fixture.Clock));

        Assert.Equal(PrinterErrors.StationInvalid, refused.Error);
    }

    [Fact]
    public async Task A_receipt_goes_to_the_customer_ticket_printer_of_the_order_location()
    {
        await PrinterAsync("Cuisine", false, true);
        var counter = await PrinterAsync("Comptoir", true, false);
        var order = _receipts.Add(_fixture.TenantA, _fixture.Cotonou);
        var handler = new PrintOrderReceiptHandler(_fixture.Workspace, _receipts, _fixture.Clock);

        var first = await handler.HandleAsync(new PrintOrderReceiptCommand(order, null));
        _fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var reprint = await handler.HandleAsync(new PrintOrderReceiptCommand(order, counter.Id.Value));

        Assert.NotEqual(first, reprint);
        var job = _fixture.Jobs.First();
        Assert.Equal((PrintJobKind.OrderReceipt, counter.Id), (job.Kind, job.PrinterId));
        var text = Text(job);
        Assert.Contains("TOTAL", text);
        Assert.Contains("Espèces", text);
        Assert.Contains("1 x Bissap", text);
    }

    [Fact]
    public async Task A_receipt_needs_an_order_of_the_restaurant_and_a_printer_of_its_location()
    {
        var elsewhere = await PrinterAsync("Paris", true, false, location: _fixture.Cotonou);
        var foreign = _receipts.Add(_fixture.TenantB, _fixture.Paris);
        var handler = new PrintOrderReceiptHandler(_fixture.Workspace, _receipts, _fixture.Clock);
        var order = _receipts.Add(_fixture.TenantA, LocationId.New());

        var notFound = await Assert.ThrowsAsync<NotFoundException>(() => handler.HandleAsync(new PrintOrderReceiptCommand(foreign, null)));
        var none = await Assert.ThrowsAsync<DomainException>(() => handler.HandleAsync(new PrintOrderReceiptCommand(order, null)));
        var wrong = await Assert.ThrowsAsync<DomainException>(() => handler.HandleAsync(new PrintOrderReceiptCommand(order, elsewhere.Id.Value)));

        Assert.Equal(ReceiptErrors.OrderNotFound, notFound.Error);
        Assert.Equal(ReceiptErrors.NoPrinter, none.Error);
        Assert.Equal(ReceiptErrors.PrinterElsewhere, wrong.Error);
    }

    [Fact]
    public void A_certified_receipt_prints_its_fiscal_mentions_and_qr_code()
    {
        var receipt = new OrderReceiptInput(
            Guid.NewGuid(), "Chez Awa", "Cotonou", null, "20261013-012", "Sur place", "13/10/2026 12:45",
            [new CustomerTicketLine(1, "Bissap", null, "500 F CFA")], [], "500 F CFA", [new ReceiptLine("Espèces", "500 F CFA")],
            ["IFU : 3201234567890", "Code MECeF/DGI : TEST-AAAA-BBBB-CCCC-DDDD-EEEE-FFFF"], "F;TS01000835;TESTAAAA");

        var document = Receipts.Build(receipt);
        var escPos = TicketRenderer.Render(document, TicketFormats.EscPos);

        Assert.Equal("F;TS01000835;TESTAAAA", Assert.Single(document.Lines, line => line.Kind == TicketLineKind.QrCode).Text);
        Assert.Contains("Code MECeF/DGI", TicketRenderer.PlainText(document));
        Assert.True(escPos.AsSpan().IndexOf(new byte[] { 0x1D, 0x28, 0x6B }) >= 0);
    }

    private sealed class FakeReceipts : IPrintingReceipts
    {
        private readonly Dictionary<Guid, (TenantId Tenant, OrderReceiptInput Receipt)> _orders = [];

        public Guid Add(TenantId tenant, LocationId location)
        {
            var id = Guid.NewGuid();
            _orders[id] = (tenant, new OrderReceiptInput(
                location.Value, "Chez Awa", "Cotonou", "Rue 12, Cotonou", "20261013-012", "Sur place", "13/10/2026 12:45",
                [new CustomerTicketLine(2, "Poulet DG", "+ Piment", "7 000 F CFA"), new CustomerTicketLine(1, "Bissap", null, "500 F CFA")],
                [new ReceiptLine("Sous-total", "7 500 F CFA")],
                "7 500 F CFA",
                [new ReceiptLine("Espèces", "7 500 F CFA")],
                []));
            return id;
        }

        public Task<OrderReceiptInput?> FindAsync(TenantId tenantId, Guid orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_orders.TryGetValue(orderId, out var order) && order.Tenant == tenantId ? order.Receipt : null);
    }
}
