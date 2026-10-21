namespace Printing.Domain;

public sealed class PrinterTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private readonly FakeClock _clock = new();

    private static TokenHash Hash(char c) => TokenHash.Create(new string(c, TokenHash.Length));

    private Printer Star() => Printer.Create(Tenant, LocationId.New(), " Comptoir ", PrinterProtocol.StarCloudPrnt, Hash('a'), "Ab3_", true, _clock);

    [Fact]
    public void A_printer_is_named_rotated_disabled_and_seen_at_most_once_a_minute()
    {
        var printer = Star();

        Assert.Equal(("Comptoir", PrinterStatus.Active), (printer.Name, printer.Status));
        Assert.True(printer.MarkSeen(_clock));
        _clock.Advance(TimeSpan.FromSeconds(20));
        Assert.False(printer.MarkSeen(_clock));
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(printer.MarkSeen(_clock));
        printer.RotateToken(Hash('b'), "wxyz", _clock);
        DomainAssert.Throws(PrinterErrors.TokenInvalid, () => printer.RotateToken(Hash('b'), "wxyz", _clock));
        printer.Disable(_clock);
        DomainAssert.Throws(PrinterErrors.AlreadyDisabled, () => printer.Disable(_clock));
        Assert.Equal(Hash('b'), printer.TokenHash);
        Assert.DoesNotContain("bbbb", printer.TokenHash.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_printers_are_refused()
    {
        DomainAssert.Throws(PrinterErrors.NameRequired, () => Printer.Create(Tenant, LocationId.New(), " ", PrinterProtocol.EscPosRelay, Hash('a'), "abcd", true, _clock));
        DomainAssert.Throws(PrinterErrors.ProtocolInvalid, () => Printer.Create(Tenant, LocationId.New(), "X", (PrinterProtocol)9, Hash('a'), "abcd", true, _clock));
        DomainAssert.Throws(PrinterErrors.TokenInvalid, () => TokenHash.Create("ABC"));
    }
}

public sealed class PrintJobTests
{
    private readonly FakeClock _clock = new();

    private Printer NewPrinter() =>
        Printer.Create(TenantId.New(), LocationId.New(), "Comptoir", PrinterProtocol.StarCloudPrnt, TokenHash.Create(new string('a', 64)), "abcd", true, _clock);

    private static TicketDocument Document() => TicketDocument.Create([new TicketLine(TicketLineKind.Text, "Bonjour")]);

    [Fact]
    public void A_job_is_sent_resent_without_acknowledgement_then_printed()
    {
        var job = PrintJob.Create(NewPrinter(), PrintJobKind.CustomerTicket, "order-1", Document(), _clock);

        job.MarkSent(_clock);
        Assert.False(job.IsDue(_clock.UtcNow));
        _clock.Advance(PrintJob.ResendAfter);
        Assert.True(job.IsDue(_clock.UtcNow));
        job.MarkSent(_clock);
        job.Complete(true, null, _clock);
        job.Complete(true, null, _clock);

        Assert.Equal((PrintJobStatus.Printed, 2), (job.Status, job.Attempts));
        DomainAssert.Throws(PrintJobErrors.NotDue, () => job.MarkSent(_clock));
    }

    [Fact]
    public void A_failure_keeps_its_code_and_a_job_never_delivered_expires()
    {
        var failed = PrintJob.Create(NewPrinter(), PrintJobKind.CustomerTicket, "order-1", Document(), _clock);
        var stale = PrintJob.Create(NewPrinter(), PrintJobKind.CustomerTicket, "order-2", Document(), _clock);

        failed.MarkSent(_clock);
        failed.Complete(false, "520 Paper empty", _clock);
        Assert.False(stale.ExpireIfStale(_clock));
        _clock.Advance(PrintJob.ExpireAfter);

        Assert.Equal((PrintJobStatus.Failed, "520 Paper empty"), (failed.Status, failed.FailureCode));
        Assert.True(stale.ExpireIfStale(_clock));
        Assert.Equal(PrintJobStatus.Expired, stale.Status);
        DomainAssert.Throws(PrintJobErrors.NotSent, () => stale.Complete(true, null, _clock));
    }

    [Fact]
    public void A_disabled_printer_takes_no_job_and_a_document_carries_no_printer_command()
    {
        var printer = NewPrinter();
        printer.Disable(_clock);

        DomainAssert.Throws(PrintJobErrors.PrinterDisabled, () => PrintJob.Create(printer, PrintJobKind.TestPage, "t", Document(), _clock));
        DomainAssert.Throws(PrintJobErrors.DocumentInvalid, () => TicketDocument.Create([new TicketLine(TicketLineKind.Text, "\u001B@")]));
        DomainAssert.Throws(PrintJobErrors.DocumentInvalid, () => TicketDocument.Create([new TicketLine(TicketLineKind.Barcode, "abc 12")]));
        DomainAssert.Throws(PrintJobErrors.DocumentInvalid, () => TicketDocument.Create([]));
    }
}
