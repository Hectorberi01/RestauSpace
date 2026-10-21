using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Fiscal.Application;
using Fiscal.Domain;
using Fiscal.Infrastructure.Emecf;
using Fiscal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fiscal.Infrastructure;

public sealed class FiscalInfrastructureTests(FiscalDatabase database) : IClassFixture<FiscalDatabase>
{
    private FakeClock Clock => database.Clock;

    private async Task<FiscalProfile> ProfileAsync(TenantId tenant)
    {
        await using var scope = database.NewScope(tenant);
        var services = scope.ServiceProvider;
        var profile = FiscalProfile.Create(tenant, "3201234567890", null, TaxRegime.Vat, AibRate.None, Clock);
        await services.GetRequiredService<IFiscalTokenStore>().StoreAsync(tenant, "eyJ.secret-token-" + tenant.Value.ToString("N"));
        profile.MarkTokenStored(Clock);
        profile.SetMode(FiscalMode.Sandbox, Clock);
        services.GetRequiredService<IFiscalProfileRepository>().Add(profile);
        await services.GetRequiredService<IFiscalUnitOfWork>().SaveChangesAsync();
        return profile;
    }

    [Fact]
    public async Task The_token_is_encrypted_and_readable_only_for_its_restaurant()
    {
        var tenant = TenantId.New();
        await ProfileAsync(tenant);

        await using var scope = database.NewScope(tenant);
        var context = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
        var stored = await context.Tokens.AsNoTracking().SingleAsync(token => token.TenantId == tenant.Value);
        var store = scope.ServiceProvider.GetRequiredService<IFiscalTokenStore>();

        Assert.DoesNotContain("secret-token", stored.CipherText);
        Assert.Equal("eyJ.secret-token-" + tenant.Value.ToString("N"), await store.ReadAsync(tenant));
        Assert.Null(await store.ReadAsync(TenantId.New()));
        var other = TenantId.New();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO fiscal.fiscal_tokens (tenant_id, cipher_text, updated_at_utc) SELECT {other.Value}, cipher_text, updated_at_utc FROM fiscal.fiscal_tokens WHERE tenant_id = {tenant.Value}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadAsync(other));
    }

    [Fact]
    public async Task An_invoice_keeps_its_lines_and_is_listed_in_the_due_queue_until_certified()
    {
        var tenant = TenantId.New();
        var profile = await ProfileAsync(tenant);
        var order = Guid.NewGuid();
        await using (var scope = database.NewScope(tenant))
        {
            scope.ServiceProvider.GetRequiredService<IFiscalInvoiceRepository>().Add(FiscalInvoice.CreateSale(
                profile, order, Guid.NewGuid(), "20261013-004",
                [new FiscalLine("Poulet DG", 3500, 2m, "B"), new FiscalLine("Jus", 1000, 1.5m, "B")],
                [new FiscalPayment("ESPECES", 8500)], Clock));
            await scope.ServiceProvider.GetRequiredService<IFiscalUnitOfWork>().SaveChangesAsync();
        }

        await using (var scope = database.NewScope(null))
        {
            var due = await scope.ServiceProvider.GetRequiredService<IDueFiscalInvoices>().ListAsync(Clock.UtcNow, 100);
            Assert.Contains(due, item => item.TenantId == tenant);
        }

        await using (var scope = database.NewScope(tenant))
        {
            var repository = scope.ServiceProvider.GetRequiredService<IFiscalInvoiceRepository>();
            var invoice = (await repository.FindBySourceAsync(tenant, FiscalInvoiceKind.Sale, order))!;
            Assert.Equal([("Poulet DG", 3500L, 2m), ("Jus", 1000L, 1.5m)], invoice.Lines.Select(line => (line.Name, line.UnitPrice, line.Quantity)));
            invoice.MarkSubmitted("uid-1", Clock);
            invoice.Certify(new FiscalSecurity("TEST-AAAA-BBBB-CCCC-DDDD-EEEE-FFFF", "F;TS01;TEST", "1/1 FV", "TS01000835", "13/10/2026"), Clock);
            await scope.ServiceProvider.GetRequiredService<IFiscalUnitOfWork>().SaveChangesAsync();
        }

        await using (var scope = database.NewScope(tenant))
        {
            var invoice = (await scope.ServiceProvider.GetRequiredService<IFiscalInvoiceRepository>().FindBySourceAsync(tenant, FiscalInvoiceKind.Sale, order))!;
            Assert.Equal(("TS01000835", FiscalInvoiceStatus.Certified), (invoice.Security!.Nim, invoice.Status));
            var due = await scope.ServiceProvider.GetRequiredService<IDueFiscalInvoices>().ListAsync(Clock.UtcNow, 100);
            Assert.DoesNotContain(due, item => item.Id == invoice.Id);
        }
    }

    [Fact]
    public async Task A_second_invoice_for_the_same_order_is_refused_by_the_database()
    {
        var tenant = TenantId.New();
        var profile = await ProfileAsync(tenant);
        var order = Guid.NewGuid();
        FiscalInvoice Sale() => FiscalInvoice.CreateSale(profile, order, Guid.NewGuid(), "N", [new FiscalLine("A", 100, 1m, "B")], [new FiscalPayment("ESPECES", 100)], Clock);

        await using (var scope = database.NewScope(tenant))
        {
            scope.ServiceProvider.GetRequiredService<IFiscalInvoiceRepository>().Add(Sale());
            await scope.ServiceProvider.GetRequiredService<IFiscalUnitOfWork>().SaveChangesAsync();
        }

        await using var second = database.NewScope(tenant);
        second.ServiceProvider.GetRequiredService<IFiscalInvoiceRepository>().Add(Sale());
        await Assert.ThrowsAsync<ConcurrencyException>(() => second.ServiceProvider.GetRequiredService<IFiscalUnitOfWork>().SaveChangesAsync());
    }

    [Fact]
    public async Task The_client_sends_the_token_as_bearer_and_reads_dgi_errors_without_leaking_it()
    {
        using var server = new FakeEmecf();
        var client = new EmecfClient(new FiscalOptions { SandboxBaseUrl = server.BaseUrl, TokenKey = new byte[32] });
        var request = new EmecfInvoiceRequest("3201234567890", "FV", null, [new EmecfItem("Poulet DG", 3500, 2m, "B")], [new EmecfPayment("ESPECES", 7000)], "Caisse", null);

        var uid = await client.SubmitAsync(FiscalMode.Sandbox, "tok-123456", request);
        var security = await client.ConfirmAsync(FiscalMode.Sandbox, "tok-123456", uid);
        server.Fail = """{"errorCode":"9","errorDesc":"Groupe de taxation invalide"}""";
        var refused = await Assert.ThrowsAsync<FiscalAuthorityException>(() => client.SubmitAsync(FiscalMode.Sandbox, "tok-123456", request));
        server.Status = HttpStatusCode.Unauthorized;
        var rejected = await Assert.ThrowsAsync<FiscalAuthorityException>(() => client.StatusAsync(FiscalMode.Sandbox, "tok-123456"));

        Assert.Equal("Bearer tok-123456", server.Authorizations[0]);
        var body = JsonNode.Parse(server.Bodies[0])!;
        Assert.Equal(("FV", "B", 3500L), ((string)body["type"]!, (string)body["items"]![0]!["taxGroup"]!, (long)body["items"]![0]!["price"]!));
        Assert.Null(body["aib"]);
        Assert.Equal(("uid-1", "TEST-AAAA-BBBB-CCCC-DDDD-EEEE-FFFF"), (uid, security.CodeMecef));
        Assert.Equal(("dgi_9", true), (refused.Code, refused.Permanent));
        Assert.Equal(("token_rejected", true), (rejected.Code, rejected.Permanent));
        Assert.DoesNotContain("tok-123456", refused.Message + rejected.Message);
    }

    /// <summary>Un faux service e-MECeF sur un port local.</summary>
    private sealed class FakeEmecf : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _loop;

        public FakeEmecf()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            BaseUrl = new Uri($"http://127.0.0.1:{port}/api/");
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _loop = Task.Run(LoopAsync);
        }

        public Uri BaseUrl { get; }

        public List<string> Authorizations { get; } = [];

        public List<string> Bodies { get; } = [];

        public string? Fail { get; set; }

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        private async Task LoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }

                Authorizations.Add(context.Request.Headers["Authorization"] ?? string.Empty);
                using (var reader = new StreamReader(context.Request.InputStream))
                {
                    Bodies.Add(await reader.ReadToEndAsync());
                }

                var path = context.Request.Url!.AbsolutePath;
                var answer = Fail ?? (path.EndsWith("/confirm", StringComparison.Ordinal)
                    ? """{"codeMECeFDGI":"TEST-AAAA-BBBB-CCCC-DDDD-EEEE-FFFF","qrCode":"F;TS01000835;TEST","counters":"1/1 FV","nim":"TS01000835","dateTime":"13/10/2026 12:00:00"}"""
                    : path.EndsWith("/status", StringComparison.Ordinal) ? """{"status":true,"ifu":"3201234567890","nim":"TS01000835"}""" : """{"uid":"uid-1","total":7000}""");
                context.Response.StatusCode = (int)Status;
                var bytes = Encoding.UTF8.GetBytes(answer);
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
    }
}
