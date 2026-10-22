using Storefront.Application.Abstractions;
using Storefront.Application.Commands;
using Storefront.Application.Receipts;
using Storefront.Domain;

namespace Storefront.Application;

public sealed class ReceiptTests : IStorefrontReceipts, IStorefrontReceiptMessages
{
    private readonly StorefrontFixture _fixture = new();
    private readonly ReceiptLinks _links = new(Enumerable.Range(1, 32).Select(i => (byte)(i * 3)).ToArray());
    private readonly Dictionary<(TenantId, OrderId), StorefrontReceipt> _receipts = [];
    private readonly List<ReceiptMessage> _sent = [];
    private string? _skip;

    private GetReceiptLinkHandler Link => new(_fixture.Workspace, this, _links);

    private GetPublicReceiptHandler Public => new(_fixture.Workspace, this, _links);

    private SendReceiptHandler Send => new(_fixture.Workspace, this, this, _links, _fixture.Clock);

    private OrderId GivenReceipt(TenantId? tenant = null, CustomerId? customer = null)
    {
        var id = OrderId.New();
        _receipts[(tenant ?? _fixture.TenantA, id)] = new StorefrontReceipt(
            id, _fixture.Cotonou, customer, "Chez Awa", "Cotonou", "Rue 12, Cotonou", "20261016-021", "Sur place", "16/10/2026 12:30", false,
            [new ReceiptItem(2, "Poulet braisé", "+ Alloco", "7 000 F CFA")],
            [new ReceiptAmount("Sous-total", "7 000 F CFA"), new ReceiptAmount("dont taxes", "1 068 F CFA")],
            "7 000 F CFA",
            [new ReceiptAmount("Espèces", "7 000 F CFA")],
            null,
            ["IFU : 3202412345678"],
            null,
            true);
        return id;
    }

    private Task GivenSiteAsync(string origin = "https://chezawa.bj") =>
        _fixture.CreateApp.HandleAsync(new CreateSalesAppCommand("Site", SalesAppKind.Web, [origin]));

    [Fact]
    public async Task The_link_opens_the_receipt_of_this_order_only_with_the_key_of_its_restaurant()
    {
        await GivenSiteAsync();
        var order = GivenReceipt();

        var link = await Link.HandleAsync(new GetReceiptLinkQuery(order.Value));
        var token = Uri.UnescapeDataString(link.Url!.Split("jeton=")[1]);
        var receipt = await Public.HandleAsync(new GetPublicReceiptQuery(token));

        Assert.StartsWith("https://chezawa.bj/recu?jeton=", link.Url, StringComparison.Ordinal);
        Assert.True(link.CanSendAutomatically);
        Assert.Contains("2 x Poulet braisé : 7 000 F CFA", link.Text, StringComparison.Ordinal);
        Assert.Contains(link.Url, link.Text, StringComparison.Ordinal);
        Assert.Equal(("20261016-021", "7 000 F CFA"), (receipt.OrderNumber, receipt.Total));

        _fixture.CurrentTenantId = _fixture.TenantB;
        await Assert.ThrowsAsync<NotFoundException>(() => Public.HandleAsync(new GetPublicReceiptQuery(token)));
        await Assert.ThrowsAsync<NotFoundException>(() => Public.HandleAsync(new GetPublicReceiptQuery("faux")));
    }

    [Fact]
    public async Task Without_an_active_site_the_receipt_is_only_text()
    {
        var order = GivenReceipt();

        var link = await Link.HandleAsync(new GetReceiptLinkQuery(order.Value));
        var sent = await Send.HandleAsync(new SendReceiptCommand(order.Value, ReceiptChannel.WhatsApp, "+229 97 00 00 01"));

        Assert.Null(link.Url);
        Assert.False(link.CanSendAutomatically);
        Assert.Equal(ReceiptSendStatus.NoLink, sent.Status);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task The_link_is_sent_to_the_number_given_or_to_the_customer_of_the_order()
    {
        await GivenSiteAsync();
        var customer = CustomerId.New();
        var order = GivenReceipt(customer: customer);

        var toNumber = await Send.HandleAsync(new SendReceiptCommand(order.Value, ReceiptChannel.WhatsApp, "+229 97 00 00 01"));
        var toCustomer = await Send.HandleAsync(new SendReceiptCommand(order.Value, ReceiptChannel.Sms));

        Assert.Equal((ReceiptSendStatus.Sent, ReceiptSendStatus.Sent), (toNumber.Status, toCustomer.Status));
        Assert.Equal("+22997000001", _sent[0].Phone!.Value);
        Assert.Null(_sent[0].CustomerId);
        Assert.Equal((customer, (PhoneNumber?)null, ReceiptChannel.Sms), (_sent[1].CustomerId, _sent[1].Phone, _sent[1].Channel));
        Assert.StartsWith("https://chezawa.bj/recu?jeton=", _sent[0].Url, StringComparison.Ordinal);
        // La clé ne contient pas le numéro (une empreinte hexadécimale peut contenir « 97 » par hasard).
        Assert.DoesNotContain("97000001", _sent[0].IdempotencyKey, StringComparison.Ordinal);
        Assert.NotEqual(_sent[0].IdempotencyKey, _sent[1].IdempotencyKey);
    }

    [Fact]
    public async Task A_skipped_sending_returns_the_reason_and_the_text_to_send_by_hand()
    {
        await GivenSiteAsync();
        var order = GivenReceipt();
        _skip = "NoTemplate";

        var result = await Send.HandleAsync(new SendReceiptCommand(order.Value, ReceiptChannel.WhatsApp, "+229 97 00 00 01"));

        Assert.Equal((ReceiptSendStatus.Skipped, "NoTemplate"), (result.Status, result.Reason));
        Assert.Contains("TOTAL : 7 000 F CFA", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_receipt_needs_a_recipient_and_an_order_of_this_restaurant()
    {
        await GivenSiteAsync();
        var order = GivenReceipt();
        var foreign = GivenReceipt(_fixture.TenantB);

        var noRecipient = await Assert.ThrowsAsync<DomainException>(() => Send.HandleAsync(new SendReceiptCommand(order.Value, ReceiptChannel.WhatsApp)));

        Assert.Equal(ReceiptErrors.RecipientRequired.Code, noRecipient.Code);
        await Assert.ThrowsAsync<NotFoundException>(() => Link.HandleAsync(new GetReceiptLinkQuery(foreign.Value)));
        await Assert.ThrowsAsync<NotFoundException>(() => Send.HandleAsync(new SendReceiptCommand(foreign.Value, ReceiptChannel.WhatsApp, "+22997000001")));
    }

    Task<StorefrontReceipt?> IStorefrontReceipts.FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken) =>
        Task.FromResult(_receipts.GetValueOrDefault((tenantId, orderId)));

    Task<ReceiptMessageOutcome> IStorefrontReceiptMessages.SendAsync(TenantId tenantId, ReceiptMessage message, CancellationToken cancellationToken)
    {
        if (_skip is not null)
        {
            return Task.FromResult(new ReceiptMessageOutcome(false, _skip));
        }

        _sent.Add(message);
        return Task.FromResult(new ReceiptMessageOutcome(true, null));
    }
}
