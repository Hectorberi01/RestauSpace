using System.Text;
using Printing.Application.Commands;
using Printing.Application.Queries;
using Printing.Application.Rendering;
using Printing.Domain;

namespace Printing.Application;

public sealed class PrintingUseCaseTests
{
    private readonly PrintingFixture _fixture = new();

    private static readonly CustomerTicketInput Ticket = new(
        "Chez Awa", "Cotonou", "20261012-007", "À emporter",
        [new CustomerTicketLine(2, "Burger Awa · Double", "Alloco, Œuf", "12 000 F CFA"), new CustomerTicketLine(1, "Bissap", null, "500 F CFA")],
        "12 500 F CFA", "À PAYER AU COMPTOIR", "12/10/2026 13:05");

    private Task<IssuedPrinterToken> StarAsync(bool customerTickets = true) =>
        _fixture.Create.HandleAsync(new CreatePrinterCommand(_fixture.Cotonou.Value, "Comptoir", PrinterProtocol.StarCloudPrnt, customerTickets));

    [Fact]
    public async Task A_printer_token_is_shown_once_kept_as_a_fingerprint_and_opens_its_own_restaurant_only()
    {
        var issued = await StarAsync();

        var fingerprint = Assert.Single(_fixture.Printers).TokenHash;
        var principal = await _fixture.SignInAsync(issued.Token);
        var forged = await _fixture.SignInAsync(issued.Token + "x");
        _fixture.CurrentTenantId = _fixture.TenantA;
        var rotated = await _fixture.Rotate.HandleAsync(new RotatePrinterTokenCommand(issued.Id));
        var old = await _fixture.SignInAsync(issued.Token);
        _fixture.CurrentTenantId = _fixture.TenantB;
        var foreign = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Rotate.HandleAsync(new RotatePrinterTokenCommand(issued.Id)));

        Assert.StartsWith(PrinterTokens.Prefix, issued.Token, StringComparison.Ordinal);
        Assert.Equal(PrinterTokens.Hash(issued.Token), fingerprint);
        Assert.DoesNotContain(issued.Token, issued.ToString(), StringComparison.Ordinal);
        Assert.Equal(_fixture.TenantA.Value, principal!.TenantId);
        Assert.Null(forged);
        Assert.Null(old);
        Assert.NotEqual(issued.Token, rotated.Token);
        Assert.Equal(PrintingApplicationErrors.PrinterNotFound, foreign.Error);
    }

    [Fact]
    public async Task A_printer_is_declared_in_a_location_of_its_restaurant_only()
    {
        var foreign = await Assert.ThrowsAsync<NotFoundException>(() =>
            _fixture.Create.HandleAsync(new CreatePrinterCommand(_fixture.Paris.Value, "X", PrinterProtocol.EscPosRelay)));
        _fixture.Writable[_fixture.TenantA] = false;
        var suspended = await Assert.ThrowsAsync<DomainException>(() => StarAsync());

        Assert.Equal(PrintingApplicationErrors.LocationNotFound, foreign.Error);
        Assert.Equal(PrintingApplicationErrors.TenantNotWritable, suspended.Error);
        Assert.Empty(_fixture.Printers);
    }

    [Fact]
    public async Task A_kiosk_ticket_goes_once_to_the_customer_ticket_printer_which_polls_fetches_and_acknowledges_it()
    {
        await StarAsync(customerTickets: false);
        var counter = await StarAsync();
        var reference = Guid.NewGuid().ToString("N");

        var first = await _fixture.CustomerTicket.HandleAsync(new PrintCustomerTicketCommand(_fixture.Cotonou.Value, reference, Ticket));
        var again = await _fixture.CustomerTicket.HandleAsync(new PrintCustomerTicketCommand(_fixture.Cotonou.Value, reference, Ticket));
        await _fixture.SignInAsync(counter.Token);
        var peeked = await _fixture.Peek.HandleAsync(new PeekPrintJobCommand());
        var content = await _fixture.Fetch.HandleAsync(new FetchPrintJobCommand(peeked!.Value, TicketFormats.StarPrnt));
        var afterFetch = await _fixture.Peek.HandleAsync(new PeekPrintJobCommand());
        await _fixture.Complete.HandleAsync(new CompletePrintJobCommand(peeked.Value, true, "200 OK"));
        _fixture.CurrentTenantId = _fixture.TenantA;
        var jobs = await _fixture.ListJobs.HandleAsync(new ListPrintJobsQuery(_fixture.Cotonou.Value));
        var printers = await _fixture.ListPrinters.HandleAsync(new ListPrintersQuery());

        Assert.Equal(first, again);
        Assert.Equal(first, peeked);
        Assert.Equal(TicketFormats.StarPrnt, content!.ContentType);
        Assert.Null(afterFetch);
        var job = Assert.Single(jobs);
        Assert.Equal((PrintJobStatus.Printed, counter.Id), (job.Status, job.PrinterId));
        Assert.True(printers.Single(printer => printer.Id == counter.Id).IsOnline);
    }

    [Fact]
    public async Task Without_a_customer_ticket_printer_nothing_is_queued_and_a_printer_never_reads_another_printer_job()
    {
        var none = await _fixture.CustomerTicket.HandleAsync(new PrintCustomerTicketCommand(_fixture.Cotonou.Value, "o-1", Ticket));
        var first = await StarAsync();
        var second = await StarAsync(customerTickets: false);
        var test = await _fixture.TestPage.HandleAsync(new PrintTestPageCommand(first.Id));
        await _fixture.SignInAsync(second.Token);

        var stolen = await _fixture.Fetch.HandleAsync(new FetchPrintJobCommand(test, TicketFormats.PlainText));
        var ack = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Complete.HandleAsync(new CompletePrintJobCommand(test, true)));

        Assert.Null(none);
        Assert.Null(stolen);
        Assert.Equal(PrintingApplicationErrors.JobNotFound, ack.Error);
    }

    [Fact]
    public async Task A_failed_ticket_can_be_reprinted_and_a_disabled_printer_is_refused()
    {
        var printer = await StarAsync();
        var job = await _fixture.TestPage.HandleAsync(new PrintTestPageCommand(printer.Id));
        await _fixture.SignInAsync(printer.Token);
        await _fixture.Fetch.HandleAsync(new FetchPrintJobCommand(job, TicketFormats.StarPrnt));
        await _fixture.Complete.HandleAsync(new CompletePrintJobCommand(job, false, "520 Paper empty"));
        _fixture.CurrentTenantId = _fixture.TenantA;
        var reprint = await _fixture.Reprint.HandleAsync(new ReprintJobCommand(job));
        await _fixture.SetDisabled.HandleAsync(new SetPrinterDisabledCommand(printer.Id, true));
        var disabled = await _fixture.SignInAsync(printer.Token);

        Assert.NotEqual(job, reprint);
        Assert.Equal(PrintJobStatus.Pending, _fixture.Jobs.Single(item => item.Id.Value == reprint).Status);
        Assert.Null(disabled);
    }

    [Fact]
    public void The_customer_ticket_shows_the_number_lines_total_and_payment_without_personal_data()
    {
        var document = CustomerTickets.Build(Ticket);
        var text = TicketRenderer.PlainText(document);
        var rows = text.Split('\n');
        var burger = Array.FindIndex(rows, row => row.StartsWith("2 x Burger Awa", StringComparison.Ordinal));

        Assert.Contains("20261012-007", text, StringComparison.Ordinal);
        Assert.Contains("À PAYER AU COMPTOIR", text, StringComparison.Ordinal);
        Assert.Contains(rows, row => row.StartsWith("2 x Burger Awa · Double", StringComparison.Ordinal) && row.EndsWith("12 000 F CFA", StringComparison.Ordinal) && row.Length == 48);
        Assert.Contains(document.Lines, line => line is { Kind: TicketLineKind.Barcode, Text: "20261012-007" });
        Assert.Equal("   Alloco, Œuf", rows[burger + 1]);
        Assert.Contains(rows, row => row == new string(' ', 18) + "20261012-007");
        Assert.All(rows, row => Assert.True(row.Length <= 48));
    }

    [Fact]
    public void Star_and_escpos_bytes_initialise_encode_in_1252_print_the_barcode_and_cut()
    {
        var document = CustomerTickets.Build(Ticket);

        var star = TicketRenderer.Render(document, TicketFormats.StarPrnt);
        var escpos = TicketRenderer.Render(document, TicketFormats.EscPos);

        Assert.Equal(new byte[] { 0x1B, 0x40, 0x1B, 0x1D, 0x74, 0x20 }, star[..6]);
        Assert.Equal(new byte[] { 0x1B, 0x64, 0x03 }, star[^3..]);
        Assert.True(Contains(star, [0x1B, 0x62, 0x36, 0x32, 0x32, 0x50, .. Encoding.ASCII.GetBytes("20261012-007"), 0x1E]));
        Assert.True(Contains(star, [0x8C]));
        Assert.True(Contains(star, [0xC0]));
        Assert.Equal(new byte[] { 0x1B, 0x40, 0x1B, 0x74, 0x10 }, escpos[..5]);
        Assert.Equal(new byte[] { 0x1D, 0x56, 0x42, 0x03 }, escpos[^4..]);
        Assert.True(Contains(escpos, [0x1D, 0x6B, 0x49, 14, .. Encoding.ASCII.GetBytes("{B20261012-007")]));
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }

        return false;
    }
}
