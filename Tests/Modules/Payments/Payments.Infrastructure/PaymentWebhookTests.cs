using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payments.Application.Abstractions;
using Payments.Domain;
using Payments.Infrastructure.Gateways;
using Payments.Infrastructure.Persistence;
using Payments.Infrastructure.Webhooks;

namespace Payments.Infrastructure;

public sealed class PaymentWebhookTests(PaymentsDatabase database) : IClassFixture<PaymentsDatabase>
{
    private static Money Xof(decimal amount) => Money.Of(amount, Currency.XOF);

    /// <summary>Un restaurant qui accepte le sandbox (secret <see cref="PaymentsDatabase.SandboxSecret"/>) ; renvoie l'id de sa configuration.</summary>
    private async Task<(TenantId Tenant, Guid Method)> GivenSandboxMethodAsync(string secret = PaymentsDatabase.SandboxSecret)
    {
        var tenant = TenantId.New();
        var method = await database.GivenOnlineConfigurationAsync(tenant, "sandbox", new Dictionary<string, string> { ["webhook_secret"] = secret });
        return (tenant, method.Id.Value);
    }

    /// <summary>Un paiement sandbox en attente d'un restaurant qui accepte le sandbox ; renvoie sa référence chez le prestataire.</summary>
    private async Task<(TenantId Tenant, Guid Method, Payment Payment, string Reference)> GivenSandboxPaymentAsync()
    {
        var (tenant, method) = await GivenSandboxMethodAsync();
        var reference = "sbx_" + Guid.NewGuid().ToString("N");
        var payment = Payment.Create(
            database.Accepting(tenant, "sandbox"),
            PaymentMethodType.Card,
            OrderReference.Create(tenant, OrderId.New(), PaymentsDatabase.Location, Xof(7000m)),
            Xof(7000m),
            database.Clock);
        payment.RegisterAttempt(ProviderReference.Create(reference), database.Clock);
        await using var scope = database.NewScope(tenant);
        scope.ServiceProvider.GetRequiredService<IPaymentRepository>().Add(payment);
        await scope.ServiceProvider.GetRequiredService<IPaymentsUnitOfWork>().SaveChangesAsync();
        return (tenant, method, payment, reference);
    }

    private static string Body(string type, string reference, decimal? amount = null, string? eventId = null) =>
        JsonSerializer.Serialize(new { id = eventId ?? "evt_" + Guid.NewGuid().ToString("N"), type, reference, amount, currency = "XOF" });

    private async Task<WebhookReceipt> SendAsync(Guid method, string body, string? signature = null, string provider = "sandbox")
    {
        await using var scope = database.NewScope(tenantId: null);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sandbox-signature"] = signature ?? SandboxSignature.Sign(PaymentsDatabase.SandboxSecret, body, database.Clock.UtcNow),
        };
        return await scope.ServiceProvider.GetRequiredService<PaymentWebhookReceiver>().ReceiveAsync(provider, method, headers, body);
    }

    private Task ProcessAsync() => database.Services.GetRequiredService<PaymentWebhookProcessor>().ProcessPendingAsync();

    private async Task<Payment> ReloadAsync(TenantId tenant, PaymentId id)
    {
        await using var scope = database.NewScope(tenant);
        return (await scope.ServiceProvider.GetRequiredService<IPaymentRepository>().FindAsync(tenant, id))!;
    }

    private async Task<PaymentWebhook> InboxAsync(string eventId)
    {
        await using var scope = database.NewScope(tenantId: null);
        return await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Webhooks.AsNoTracking().SingleAsync(webhook => webhook.EventId == eventId);
    }

    [Fact]
    public async Task Only_a_webhook_signed_with_the_secret_of_the_restaurant_is_kept_and_only_once()
    {
        var (_, method) = await GivenSandboxMethodAsync();
        var (_, otherRestaurant) = await GivenSandboxMethodAsync(secret: "another-restaurant-secret");
        var body = Body("payment.captured", "sbx_x", 7000m, eventId: "evt_once_" + Guid.NewGuid().ToString("N"));

        var unsigned = await SendAsync(method, body, signature: "t=1,v1=" + new string('0', 64));
        var tampered = await SendAsync(method, body.Replace("7000", "1", StringComparison.Ordinal), SandboxSignature.Sign(PaymentsDatabase.SandboxSecret, body, database.Clock.UtcNow));
        var stale = await SendAsync(method, body, SandboxSignature.Sign(PaymentsDatabase.SandboxSecret, body, database.Clock.UtcNow.AddMinutes(-6)));
        var otherSecret = await SendAsync(otherRestaurant, body);
        var wrongProvider = await SendAsync(method, body, provider: "stripe");
        var unknownMethod = await SendAsync(Guid.NewGuid(), body);
        var ignored = await SendAsync(method, JsonSerializer.Serialize(new { id = "evt_" + Guid.NewGuid().ToString("N"), type = "customer.created" }));
        var accepted = await SendAsync(method, body);
        var resent = await SendAsync(method, body);

        Assert.Equal(
            new[] { WebhookReceipt.Invalid, WebhookReceipt.Invalid, WebhookReceipt.Invalid, WebhookReceipt.Invalid, WebhookReceipt.UnknownEndpoint, WebhookReceipt.UnknownEndpoint, WebhookReceipt.Ignored, WebhookReceipt.Accepted, WebhookReceipt.Duplicate },
            new[] { unsigned, tampered, stale, otherSecret, wrongProvider, unknownMethod, ignored, accepted, resent });
    }

    [Fact]
    public async Task A_captured_webhook_pays_the_payment_of_its_tenant_once()
    {
        var (tenant, method, payment, reference) = await GivenSandboxPaymentAsync();
        var eventId = "evt_" + Guid.NewGuid().ToString("N");
        var body = Body("payment.captured", reference, 7000m, eventId);

        await SendAsync(method, body);
        await ProcessAsync();
        await SendAsync(method, Body("payment.captured", reference, 7000m));
        await ProcessAsync();

        var paid = await ReloadAsync(tenant, payment.Id);
        var webhook = await InboxAsync(eventId);
        Assert.Equal(PaymentStatus.Captured, paid.Status);
        Assert.Equal(Xof(7000m), paid.CapturedAmount);
        Assert.Equal("applied", webhook.Outcome);
        Assert.Equal(body, webhook.RawBody);
        Assert.NotNull(webhook.ProcessedAtUtc);
    }

    [Fact]
    public async Task A_contradicting_webhook_is_kept_as_rejected_and_never_retried()
    {
        var (tenant, method, payment, reference) = await GivenSandboxPaymentAsync();
        await SendAsync(method, Body("payment.captured", reference, 7000m));
        await ProcessAsync();
        var eventId = "evt_" + Guid.NewGuid().ToString("N");

        await SendAsync(method, Body("payment.failed", reference, eventId: eventId));
        await ProcessAsync();

        var webhook = await InboxAsync(eventId);
        Assert.Equal("rejected:" + PaymentErrors.AttemptAlreadySucceeded.Code, webhook.Outcome);
        Assert.Equal(PaymentStatus.Captured, (await ReloadAsync(tenant, payment.Id)).Status);
    }

    [Fact]
    public async Task A_webhook_ahead_of_its_payment_is_retried_then_rejected_after_the_grace_period()
    {
        var (_, method) = await GivenSandboxMethodAsync();
        var eventId = "evt_" + Guid.NewGuid().ToString("N");
        await SendAsync(method, Body("payment.captured", "sbx_not_yet_" + Guid.NewGuid().ToString("N"), 7000m, eventId));

        await ProcessAsync();
        var waiting = await InboxAsync(eventId);
        database.Clock.Advance(PaymentWebhookProcessor.UnknownReferenceGrace + TimeSpan.FromMinutes(1));
        await ProcessAsync();
        var rejected = await InboxAsync(eventId);

        Assert.Null(waiting.ProcessedAtUtc);
        Assert.Equal(1, waiting.Attempts);
        Assert.True(waiting.NextAttemptAtUtc > waiting.ReceivedAtUtc);
        Assert.Equal("rejected:payments.payment.not_found", rejected.Outcome);
    }

    [Fact]
    public async Task A_webhook_signed_for_one_restaurant_never_touches_the_payment_of_another()
    {
        var (_, _, victim, reference) = await GivenSandboxPaymentAsync();
        var (_, attacker) = await GivenSandboxMethodAsync(secret: "attacker-secret");
        var eventId = "evt_" + Guid.NewGuid().ToString("N");
        var body = Body("payment.captured", reference, 7000m, eventId);

        // L'attaquant signe avec SON secret, à SON adresse, en visant la référence d'un autre restaurant.
        await SendAsync(attacker, body, SandboxSignature.Sign("attacker-secret", body, database.Clock.UtcNow));
        database.Clock.Advance(PaymentWebhookProcessor.UnknownReferenceGrace + TimeSpan.FromMinutes(1));
        await ProcessAsync();

        Assert.Equal("rejected:payments.payment.not_found", (await InboxAsync(eventId)).Outcome);
        Assert.Equal(PaymentStatus.Pending, (await ReloadAsync(victim.TenantId, victim.Id)).Status);
    }
}
