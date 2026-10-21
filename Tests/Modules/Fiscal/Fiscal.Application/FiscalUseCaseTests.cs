using Fiscal.Domain;

namespace Fiscal.Application;

public sealed class FiscalUseCaseTests
{
    private readonly Ports _ports = new();

    private FiscalWorkspace Workspace => new(_ports, _ports, _ports, _ports, _ports, _ports);

    private async Task ActiveAsync(TaxRegime regime = TaxRegime.Vat)
    {
        await new ConfigureFiscalProfileHandler(Workspace, _ports.Clock).HandleAsync(new ConfigureFiscalProfileCommand("3201234567890", null, regime, AibRate.None));
        await new SetFiscalTokenHandler(Workspace, _ports.Clock).HandleAsync(new SetFiscalTokenCommand("eyJhbGciOiJIUzI1NiJ9.test-token-value"));
        await new SetFiscalModeHandler(Workspace, _ports.Clock).HandleAsync(new SetFiscalModeCommand(FiscalMode.Sandbox));
    }

    private Guid Order(decimal total = 7500m)
    {
        var id = Guid.NewGuid();
        _ports.Orders[id] = new FiscalOrder(
            id, Guid.NewGuid(), "20261013-004", "XOF", total,
            [new FiscalOrderLine("Poulet DG", 2, 7000m, 0.18m), new FiscalOrderLine("Bissap", 1, total - 7000m, 0.18m)],
            null,
            [new FiscalOrderPayment("Cash", 6000m), new FiscalOrderPayment("MobileMoneyOnSite", total - 6000m)]);
        return id;
    }

    private Task<FiscalInvoiceStatus> ProcessAsync(FiscalInvoice invoice) =>
        new ProcessFiscalInvoiceHandler(Workspace, _ports.Authority, _ports.Clock).HandleAsync(new ProcessFiscalInvoiceCommand(invoice.Id.Value));

    [Fact]
    public async Task The_token_is_kept_in_the_vault_and_never_in_the_profile()
    {
        await ActiveAsync();

        var profile = await new GetFiscalProfileHandler(Workspace).HandleAsync(new GetFiscalProfileQuery());

        Assert.Equal("eyJhbGciOiJIUzI1NiJ9.test-token-value", _ports.Vault[_ports.Tenant]);
        Assert.True(profile!.HasToken);
        Assert.DoesNotContain("test-token", profile.ToString());
    }

    [Fact]
    public async Task Only_a_restaurant_in_cfa_francs_issues_standardized_invoices()
    {
        _ports.Currency = "EUR";

        var refused = await Assert.ThrowsAsync<DomainException>(() =>
            new ConfigureFiscalProfileHandler(Workspace, _ports.Clock).HandleAsync(new ConfigureFiscalProfileCommand("3201234567890", null, TaxRegime.Vat, AibRate.None)));

        Assert.Equal(FiscalApplicationErrors.CurrencyNotSupported, refused.Error);
    }

    [Fact]
    public async Task A_completed_order_gets_one_sale_invoice_sent_then_confirmed()
    {
        await ActiveAsync();
        var order = Order();
        var issue = new IssueSaleInvoiceHandler(Workspace, _ports, _ports.Clock);

        var first = await issue.HandleAsync(new IssueSaleInvoiceCommand(order));
        var again = await issue.HandleAsync(new IssueSaleInvoiceCommand(order));
        var status = await ProcessAsync(_ports.Invoices.Single());

        Assert.Equal(first, again);
        Assert.Equal(FiscalInvoiceStatus.Certified, status);
        var request = Assert.Single(_ports.Authority.Submitted);
        Assert.Equal(("FV", "3201234567890", (string?)null), (request.Type, request.Ifu, request.Reference));
        Assert.Equal([("Poulet DG", 3500L, 2m, "B"), ("Bissap", 500L, 1m, "B")], request.Items.Select(item => (item.Name, item.Price, item.Quantity, item.TaxGroup)));
        Assert.Equal([("ESPECES", 6000L), ("MOBILEMONEY", 1500L)], request.Payments.Select(payment => (payment.Name, payment.Amount)));
        Assert.Equal("TEST-AAAA-BBBB-CCCC-DDDD-EEEE-FFFF", _ports.Invoices.Single().Security!.CodeMecef);
    }

    [Fact]
    public async Task Nothing_is_invoiced_while_e_mecef_is_disabled()
    {
        await new ConfigureFiscalProfileHandler(Workspace, _ports.Clock).HandleAsync(new ConfigureFiscalProfileCommand("3201234567890", null, TaxRegime.Vat, AibRate.None));

        var issued = await new IssueSaleInvoiceHandler(Workspace, _ports, _ports.Clock).HandleAsync(new IssueSaleInvoiceCommand(Order()));

        Assert.Null(issued);
        Assert.Empty(_ports.Invoices);
    }

    [Fact]
    public async Task A_dgi_outage_is_retried_and_a_refusal_fails_at_once()
    {
        await ActiveAsync();
        await new IssueSaleInvoiceHandler(Workspace, _ports, _ports.Clock).HandleAsync(new IssueSaleInvoiceCommand(Order()));
        var invoice = _ports.Invoices.Single();

        _ports.Authority.Next = new FiscalAuthorityException("unreachable", "e-MECeF submit: service unreachable.", permanent: false);
        var outage = await ProcessAsync(invoice);
        _ports.Clock.Advance(TimeSpan.FromMinutes(2));
        _ports.Authority.Next = new FiscalAuthorityException("dgi_9", "e-MECeF submit: groupe de taxation invalide", permanent: true);
        var refused = await ProcessAsync(invoice);

        Assert.Equal((FiscalInvoiceStatus.Pending, FiscalInvoiceStatus.Failed), (outage, refused));
        Assert.Equal("dgi_9", invoice.ErrorCode);
    }

    [Fact]
    public async Task A_refund_gets_a_credit_note_that_waits_for_its_certified_sale()
    {
        await ActiveAsync(TaxRegime.Tps);
        var order = Order();
        await new IssueSaleInvoiceHandler(Workspace, _ports, _ports.Clock).HandleAsync(new IssueSaleInvoiceCommand(order));
        var sale = _ports.Invoices.Single();
        var refund = Guid.NewGuid();
        await new IssueCreditNoteHandler(Workspace, _ports.Clock).HandleAsync(new IssueCreditNoteCommand(order, refund, 500m, "Cash"));
        var credit = _ports.Invoices.Single(invoice => invoice.Kind == FiscalInvoiceKind.CreditNote);

        var waiting = await ProcessAsync(credit);
        await ProcessAsync(sale);
        _ports.Clock.Advance(TimeSpan.FromMinutes(2));
        var certified = await ProcessAsync(credit);

        Assert.Equal((FiscalInvoiceStatus.Pending, FiscalInvoiceStatus.Certified), (waiting, certified));
        var request = _ports.Authority.Submitted.Last();
        Assert.Equal(("FA", "TESTAAAABBBBCCCCDDDDEEEEFFFF"), (request.Type, request.Reference));
        Assert.Equal(("E", 500L), (request.Items[0].TaxGroup, request.Items[0].Price));
    }

    [Fact]
    public async Task A_refund_of_an_order_never_invoiced_gives_no_credit_note()
    {
        await ActiveAsync();

        var credit = await new IssueCreditNoteHandler(Workspace, _ports.Clock).HandleAsync(new IssueCreditNoteCommand(Guid.NewGuid(), Guid.NewGuid(), 500m, "Cash"));

        Assert.Null(credit);
    }

    [Fact]
    public void Lines_carry_an_odd_division_as_one_line_and_payments_fall_back_to_other()
    {
        var profile = FiscalProfile.Create(TenantId.New(), "3201234567890", null, TaxRegime.Vat, AibRate.None, new FakeClock());
        var order = new FiscalOrder(Guid.NewGuid(), Guid.NewGuid(), "N", "XOF", 1001m,
            [new FiscalOrderLine("Jus", 3, 1000m, 0.18m)], 1m, [new FiscalOrderPayment("Cash", 999m)]);

        var lines = FiscalAmounts.Lines(profile, order);
        var payments = FiscalAmounts.Payments(order, 1001);

        Assert.Equal([("3 x Jus", 1000L, 1m, "B"), ("Frais de livraison", 1L, 1m, "A")], lines.Select(line => (line.Name, line.UnitPrice, line.Quantity, line.TaxGroup)));
        Assert.Equal(("AUTRE", 1001L), (payments.Single().Type, payments.Single().Amount));
    }

    private sealed class Ports
        : ITenantContext, IFiscalTenants, IFiscalProfileRepository, IFiscalInvoiceRepository, IFiscalTokenStore, IFiscalUnitOfWork, IFiscalOrders
    {
        public TenantId Tenant { get; } = TenantId.New();

        public TenantId? CurrentTenantId => Tenant;

        public string Currency { get; set; } = "XOF";

        public FakeClock Clock { get; } = new();

        public FakeAuthority Authority { get; } = new();

        public List<FiscalProfile> Profiles { get; } = [];

        public List<FiscalInvoice> Invoices { get; } = [];

        public Dictionary<TenantId, string> Vault { get; } = [];

        public Dictionary<Guid, FiscalOrder> Orders { get; } = [];

        public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<string?> CurrencyAsync(TenantId tenantId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(Currency);

        Task<FiscalProfile?> IFiscalProfileRepository.FindAsync(TenantId tenantId, CancellationToken cancellationToken) =>
            Task.FromResult(Profiles.SingleOrDefault(profile => profile.TenantId == tenantId));

        public void Add(FiscalProfile profile) => Profiles.Add(profile);

        public Task<FiscalInvoice?> FindAsync(TenantId tenantId, FiscalInvoiceId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Invoices.SingleOrDefault(invoice => invoice.TenantId == tenantId && invoice.Id == id));

        public Task<FiscalInvoice?> FindBySourceAsync(TenantId tenantId, FiscalInvoiceKind kind, Guid sourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Invoices.SingleOrDefault(invoice => invoice.TenantId == tenantId && invoice.Kind == kind && invoice.SourceId == sourceId));

        public Task<IReadOnlyList<FiscalInvoice>> ListAsync(
            TenantId tenantId, FiscalInvoiceStatus? status, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FiscalInvoice>>(Invoices.Where(invoice => invoice.TenantId == tenantId && (status is null || invoice.Status == status)).Take(take).ToList());

        public void Add(FiscalInvoice invoice) => Invoices.Add(invoice);

        public Task StoreAsync(TenantId tenantId, string token, CancellationToken cancellationToken = default)
        {
            Vault[tenantId] = token;
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(TenantId tenantId, CancellationToken cancellationToken = default) => Task.FromResult(Vault.GetValueOrDefault(tenantId));

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        Task<FiscalOrder?> IFiscalOrders.FindAsync(TenantId tenantId, Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult(tenantId == Tenant ? Orders.GetValueOrDefault(orderId) : null);
    }

    private sealed class FakeAuthority : IFiscalAuthority
    {
        public List<EmecfInvoiceRequest> Submitted { get; } = [];

        public FiscalAuthorityException? Next { get; set; }

        public Task<EmecfStatus> StatusAsync(FiscalMode mode, string token, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmecfStatus("3201234567890", "TS01000835", null));

        public Task<string> SubmitAsync(FiscalMode mode, string token, EmecfInvoiceRequest request, CancellationToken cancellationToken = default)
        {
            if (Next is { } failure)
            {
                Next = null;
                throw failure;
            }

            Submitted.Add(request);
            return Task.FromResult("uid-" + Submitted.Count);
        }

        public Task<FiscalSecurity> ConfirmAsync(FiscalMode mode, string token, string uid, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FiscalSecurity("TEST-AAAA-BBBB-CCCC-DDDD-EEEE-FFFF", "F;TS01000835;TEST;" + uid, "1/1 FV", "TS01000835", "13/10/2026 12:00:00"));
    }
}
