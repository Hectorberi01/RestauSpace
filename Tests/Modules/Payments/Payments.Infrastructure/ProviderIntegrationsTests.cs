using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Payments.Application.Abstractions;
using Payments.Application.Commands;
using Payments.Domain;
using Payments.Infrastructure.Gateways.Providers;
using Payments.Infrastructure.Webhooks;

namespace Payments.Infrastructure;

/// <summary>
/// Les intégrations réelles, sans réseau : un faux serveur HTTP répond comme le prestataire et note les requêtes. On
/// vérifie l'authentification des webhooks, la relecture chez le prestataire (qui seule fait foi) et la traduction des
/// statuts, et qu'aucune erreur ne contient d'identifiant.
/// </summary>
public sealed class ProviderIntegrationsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private static readonly PaymentsOptions Options = new()
    {
        PublicApiBaseUrl = new Uri("https://api.example.com"),
        CheckoutReturnUrl = new Uri("https://site.example.com/merci"),
    };

    private static GatewayPaymentRequest Request(decimal amount, Currency currency, string? phone = null) =>
        new(TenantId.New(), PaymentId.New(), OrderId.New(), Money.Of(amount, currency), Guid.NewGuid(), phone);

    // ---- FedaPay

    [Fact]
    public async Task FedaPay_opens_a_transaction_then_its_payment_link()
    {
        var server = new FakeServer()
            .On("POST", "/v1/transactions", """{"v1/transaction":{"id":4242,"status":"pending"}}""")
            .On("POST", "/v1/transactions/4242/token", """{"token":"tok","url":"https://checkout.fedapay.com/tok"}""");
        var gateway = new FedaPayIntegration(server.Http, Options).CreateGateway(FedaPayKeys);

        var checkout = await gateway.StartAsync(Request(2500m, Currency.XOF));

        Assert.Equal("4242", checkout.Reference.Value);
        Assert.Equal("https://checkout.fedapay.com/tok", checkout.CheckoutUrl!.AbsoluteUri);
        var sent = server.Requests[0];
        Assert.StartsWith("https://sandbox-api.fedapay.com/", sent.Url, StringComparison.Ordinal);
        Assert.Equal("Bearer sk_sandbox_abc", sent.Authorization);
        Assert.Equal(2500, JsonNode.Parse(sent.Body)!["amount"]!.GetValue<long>());
    }

    [Fact]
    public async Task A_FedaPay_webhook_is_signed_then_the_transaction_is_read_again()
    {
        var server = new FakeServer().On("GET", "/v1/transactions/4242",
            """{"v1/transaction":{"id":4242,"status":"approved","amount":2500,"custom_metadata":{"currency":"XOF"}}}""");
        var reader = new FedaPayIntegration(server.Http, Options).CreateWebhookReader(FedaPayKeys);
        const string body = """{"name":"transaction.approved","entity":{"id":4242,"status":"approved","amount":1}}""";
        var signed = new Dictionary<string, string> { [FedaPayWebhookReader.Header] = $"t={Now.ToUnixTimeSeconds()},s={FedaPayWebhookReader.Sign("whsec", Now.ToUnixTimeSeconds(), body)}" };

        var read = await reader.ReadAsync(signed, body, Now);
        var forged = await reader.ReadAsync(new Dictionary<string, string> { [FedaPayWebhookReader.Header] = $"t={Now.ToUnixTimeSeconds()},s=00" }, body, Now);
        var stale = await reader.ReadAsync(signed, body, Now.AddMinutes(10));

        Assert.Equal(new VerifiedWebhook("4242:approved", ProviderResultKind.Captured, "4242", 2500m, "XOF"), read.Webhook);
        Assert.Equal(WebhookReadStatus.Invalid, forged.Status);
        Assert.Equal(WebhookReadStatus.Invalid, stale.Status);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task FedaPay_refunds_are_refused_definitively()
    {
        var gateway = new FedaPayIntegration(new FakeServer().Http, Options).CreateGateway(FedaPayKeys);

        var refused = await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.RequestRefundAsync(
            new GatewayRefundRequest(TenantId.New(), PaymentId.New(), RefundId.New(), ProviderReference.Create("1"), Money.Of(1m, Currency.XOF))));

        Assert.True(refused.IsPermanent);
    }

    // ---- Stripe

    [Fact]
    public async Task Stripe_opens_a_checkout_session_in_minor_units()
    {
        var server = new FakeServer().On("POST", "/v1/checkout/sessions", """{"id":"cs_test_1","url":"https://checkout.stripe.com/c/cs_test_1"}""");
        var gateway = new StripeIntegration(server.Http, Options).CreateGateway(StripeKeys);

        var checkout = await gateway.StartAsync(Request(12.5m, Currency.EUR));

        Assert.Equal("cs_test_1", checkout.Reference.Value);
        var form = server.Requests[0].Body;
        Assert.Contains("line_items%5B0%5D%5Bprice_data%5D%5Bunit_amount%5D=1250", form, StringComparison.Ordinal);
        Assert.Contains("line_items%5B0%5D%5Bprice_data%5D%5Bcurrency%5D=eur", form, StringComparison.Ordinal);
        Assert.Equal("Bearer sk_test_abc", server.Requests[0].Authorization);
    }

    [Fact]
    public async Task Stripe_webhooks_map_sessions_and_refunds()
    {
        var reader = new StripeIntegration(new FakeServer().Http, Options).CreateWebhookReader(StripeKeys);
        var refundId = Guid.NewGuid();
        const string paid = """{"id":"evt_1","type":"checkout.session.completed","data":{"object":{"id":"cs_test_1","payment_status":"paid","amount_total":1250,"currency":"eur"}}}""";
        var refund = """{"id":"evt_2","type":"refund.updated","data":{"object":{"status":"succeeded","amount":500,"currency":"eur","metadata":{"refund_id":"REFUND","reference":"cs_test_1"}}}}""".Replace("REFUND", refundId.ToString("D"), StringComparison.Ordinal);

        var captured = await reader.ReadAsync(Sign(paid), paid, Now);
        var refunded = await reader.ReadAsync(Sign(refund), refund, Now);
        var unsigned = await reader.ReadAsync(new Dictionary<string, string>(), paid, Now);

        Assert.Equal(new VerifiedWebhook("evt_1", ProviderResultKind.Captured, "cs_test_1", 12.5m, "EUR"), captured.Webhook);
        Assert.Equal(new VerifiedWebhook("evt_2", ProviderResultKind.RefundSucceeded, "cs_test_1", 5m, "EUR", refundId), refunded.Webhook);
        Assert.Equal(WebhookReadStatus.Invalid, unsigned.Status);

        static Dictionary<string, string> Sign(string body) =>
            new() { [StripeWebhookReader.Header] = $"t={Now.ToUnixTimeSeconds()},v1={StripeWebhookReader.Sign("whsec_x", Now.ToUnixTimeSeconds(), body)}" };
    }

    // ---- CinetPay

    [Fact]
    public async Task CinetPay_needs_a_multiple_of_five_and_gives_its_notification_address()
    {
        var server = new FakeServer().On("POST", "/v2/payment", """{"code":"201","data":{"payment_url":"https://checkout.cinetpay.com/p/1"}}""");
        var gateway = new CinetPayIntegration(server.Http, Options).CreateGateway(CinetPayKeys);
        var request = Request(2500m, Currency.XOF);

        var checkout = await gateway.StartAsync(request);
        var refused = await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.StartAsync(Request(2502m, Currency.XOF)));

        Assert.StartsWith("cp", checkout.Reference.Value, StringComparison.Ordinal);
        var body = JsonNode.Parse(server.Requests[0].Body)!;
        Assert.Equal($"https://api.example.com/v1/payments/webhooks/cinetpay/{request.ConfigurationId:D}", body["notify_url"]!.GetValue<string>());
        Assert.True(refused.IsPermanent);
    }

    [Fact]
    public async Task A_CinetPay_notification_is_only_a_signal_the_check_api_decides()
    {
        var server = new FakeServer().On("POST", "/v2/payment/check", """{"code":"00","data":{"status":"ACCEPTED","amount":"2500","currency":"XOF"}}""");
        var reader = new CinetPayIntegration(server.Http, Options).CreateWebhookReader(CinetPayKeys);
        const string transaction = "cp0123456789abcdef0123456789abcdef";

        var read = await reader.ReadAsync(new Dictionary<string, string>(), $"cpm_trans_id={transaction}&cpm_site_id=site42", Now);
        var otherSite = await reader.ReadAsync(new Dictionary<string, string>(), $"cpm_trans_id={transaction}&cpm_site_id=other", Now);

        Assert.Equal(new VerifiedWebhook($"{transaction}:ACCEPTED", ProviderResultKind.Captured, transaction, 2500m, "XOF"), read.Webhook);
        Assert.Equal(WebhookReadStatus.Invalid, otherSite.Status);
        Assert.Equal(transaction, JsonNode.Parse(server.Requests[0].Body)!["transaction_id"]!.GetValue<string>());
    }

    // ---- KKiaPay

    [Fact]
    public async Task KKiaPay_opens_its_widget_and_checks_the_secret_then_the_status()
    {
        var server = new FakeServer();
        var integration = new KkiaPayIntegration(server.Http, Options);
        var checkout = await integration.CreateGateway(KkiaPayKeys).StartAsync(Request(1000m, Currency.XOF));
        var reference = checkout.Reference.Value;
        server.On("POST", "/api/v1/transactions/status", $$"""{"status":"SUCCESS","amount":1000,"partnerId":"{{reference}}","transactionId":"84"}""");
        var reader = integration.CreateWebhookReader(KkiaPayKeys);
        const string body = """{"transactionId":"84","event":"transaction.success","partnerId":"x"}""";

        var read = await reader.ReadAsync(new Dictionary<string, string> { ["x-kkiapay-secret"] = "hook" }, body, Now);
        var forged = await reader.ReadAsync(new Dictionary<string, string> { ["x-kkiapay-secret"] = "nope" }, body, Now);

        Assert.Contains("partnerId=" + reference, checkout.CheckoutUrl!.Query, StringComparison.Ordinal);
        Assert.Contains("sandbox=true", checkout.CheckoutUrl.Query, StringComparison.Ordinal);
        Assert.Equal(new VerifiedWebhook("84:SUCCESS", ProviderResultKind.Captured, reference, 1000m, "XOF"), read.Webhook);
        Assert.Equal(WebhookReadStatus.Invalid, forged.Status);
        Assert.Equal("pub", server.Requests.Single().Headers["x-api-key"]);
    }

    // ---- MTN MoMo

    [Fact]
    public async Task MTN_pushes_the_request_to_the_payer_then_reads_its_status_back()
    {
        var server = new FakeServer()
            .On("POST", "/collection/token/", """{"access_token":"jwt"}""")
            .On("POST", "/collection/v1_0/requesttopay", string.Empty, HttpStatusCode.Accepted);
        var integration = new MtnMomoIntegration(server.Http, Options);

        var missing = await Assert.ThrowsAsync<PaymentGatewayException>(() => integration.CreateGateway(MtnKeys).StartAsync(Request(1500m, Currency.XOF)));
        var checkout = await integration.CreateGateway(MtnKeys).StartAsync(Request(1500m, Currency.XOF, "22997000000"));
        server.On("GET", $"/collection/v1_0/requesttopay/{checkout.Reference.Value}", """{"status":"SUCCESSFUL","amount":"1500","currency":"XOF"}""");
        var read = await integration.CreateWebhookReader(MtnKeys).ReadAsync(
            new Dictionary<string, string>(), $$"""{"externalId":"{{checkout.Reference.Value}}","status":"SUCCESSFUL"}""", Now);

        Assert.True(missing.IsPermanent);
        Assert.Null(checkout.CheckoutUrl);
        var push = server.Requests.Single(request => request.Url.EndsWith("/requesttopay", StringComparison.Ordinal));
        Assert.Equal(checkout.Reference.Value, push.Headers["X-Reference-Id"]);
        Assert.Equal("sandbox", push.Headers["X-Target-Environment"]);
        Assert.Equal("22997000000", JsonNode.Parse(push.Body)!["payer"]!["partyId"]!.GetValue<string>());
        Assert.Equal(ProviderResultKind.Captured, read.Webhook!.Kind);
        Assert.Equal(1500m, read.Webhook.Amount);
    }

    // ---- Relecture d'un encaissement (abonnements de la plateforme)

    [Fact]
    public async Task FedaPay_MTN_and_CinetPay_read_a_transaction_state_back_and_a_label_replaces_the_order()
    {
        var server = new FakeServer()
            .On("POST", "/v1/transactions", """{"v1/transaction":{"id":4242,"status":"pending"}}""")
            .On("POST", "/v1/transactions/4242/token", """{"token":"tok","url":"https://checkout.fedapay.com/tok"}""")
            .On("GET", "/v1/transactions/4242", """{"v1/transaction":{"id":4242,"status":"approved","amount":24900,"currency":{"iso":"XOF"}}}""")
            .On("GET", "/v1/transactions/4243", """{"v1/transaction":{"id":4243,"status":"declined"}}""")
            .On("GET", "/v1/transactions/4244", """{"v1/transaction":{"id":4244,"status":"pending"}}""")
            .On("POST", "/collection/token/", """{"access_token":"jwt"}""")
            .On("GET", "/collection/v1_0/requesttopay/0b1e8f9a-0000-4000-8000-000000000001", """{"status":"FAILED","reason":"PAYER_NOT_FOUND"}""")
            .On("POST", "/payment/check", """{"code":"00","data":{"status":"ACCEPTED","amount":"1500","currency":"XOF"}}""");
        var fedapay = new FedaPayIntegration(server.Http, Options).CreateGateway(FedaPayKeys);

        _ = await fedapay.StartAsync(Request(24900m, Currency.XOF) with { Label = "Abonnement Pro" });
        var approved = await fedapay.FetchStatusAsync(ProviderReference.Create("4242"));
        var declined = await fedapay.FetchStatusAsync(ProviderReference.Create("4243"));
        var pending = await fedapay.FetchStatusAsync(ProviderReference.Create("4244"));
        var mtn = await new MtnMomoIntegration(server.Http, Options).CreateGateway(MtnKeys)
            .FetchStatusAsync(ProviderReference.Create("0b1e8f9a-0000-4000-8000-000000000001"));
        var cinetpay = await new CinetPayIntegration(server.Http, Options).CreateGateway(CinetPayKeys).FetchStatusAsync(ProviderReference.Create("cp123"));
        var kkiapay = await new KkiaPayIntegration(server.Http, Options).CreateGateway(KkiaPayKeys).FetchStatusAsync(ProviderReference.Create("kkp123"));

        Assert.Equal("Abonnement Pro", JsonNode.Parse(server.Requests[0].Body)!["description"]!.GetValue<string>());
        Assert.Equal(new GatewayTransactionStatus(GatewayTransactionState.Captured, 24900m, "XOF"), approved);
        Assert.Equal(GatewayTransactionState.Failed, declined!.State);
        Assert.Equal(GatewayTransactionState.Pending, pending!.State);
        Assert.Equal(GatewayTransactionState.Failed, mtn!.State);
        Assert.Equal(new GatewayTransactionStatus(GatewayTransactionState.Captured, 1500m, "XOF"), cinetpay);
        Assert.Null(kkiapay);
    }

    // ---- PayPal

    [Fact]
    public async Task A_PayPal_webhook_is_verified_by_PayPal_then_the_approved_order_is_captured()
    {
        var server = new FakeServer()
            .On("POST", "/v1/oauth2/token", """{"access_token":"pp"}""")
            .On("POST", "/v1/notifications/verify-webhook-signature", """{"verification_status":"SUCCESS"}""")
            .On("POST", "/v2/checkout/orders/ORDER1/capture",
                """{"id":"ORDER1","status":"COMPLETED","purchase_units":[{"payments":{"captures":[{"id":"CAP1","status":"COMPLETED","amount":{"value":"12.50","currency_code":"EUR"}}]}}]}""");
        var reader = new PayPalIntegration(server.Http, Options).CreateWebhookReader(PayPalKeys);
        var headers = new Dictionary<string, string>
        {
            ["PAYPAL-AUTH-ALGO"] = "SHA256withRSA",
            ["PAYPAL-CERT-URL"] = "https://api.paypal.com/cert",
            ["PAYPAL-TRANSMISSION-ID"] = "t1",
            ["PAYPAL-TRANSMISSION-SIG"] = "sig",
            ["PAYPAL-TRANSMISSION-TIME"] = "2026-10-11T12:00:00Z",
        };
        const string body = """{"id":"WH-1","event_type":"CHECKOUT.ORDER.APPROVED","resource":{"id":"ORDER1"}}""";

        var read = await reader.ReadAsync(headers, body, Now);
        var unsigned = await reader.ReadAsync(new Dictionary<string, string>(), body, Now);

        Assert.Equal(new VerifiedWebhook("WH-1", ProviderResultKind.Captured, "ORDER1", 12.5m, "EUR"), read.Webhook);
        Assert.Equal(WebhookReadStatus.Invalid, unsigned.Status);
        Assert.Equal("wh-id", JsonNode.Parse(server.Requests.Single(request => request.Url.EndsWith("verify-webhook-signature", StringComparison.Ordinal)).Body)!["webhook_id"]!.GetValue<string>());
    }

    // ---- Erreurs

    [Fact]
    public async Task A_refusal_never_carries_the_credentials()
    {
        var server = new FakeServer().On("POST", "/v1/checkout/sessions", """{"error":{"type":"invalid_request_error","message":"sk_test_abc is wrong"}}""", HttpStatusCode.BadRequest);
        var gateway = new StripeIntegration(server.Http, Options).CreateGateway(StripeKeys);

        var refused = await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.StartAsync(Request(10m, Currency.EUR)));

        Assert.True(refused.IsPermanent);
        Assert.DoesNotContain("sk_test_abc", refused.Message, StringComparison.Ordinal);
        Assert.Contains("invalid_request_error", refused.Message, StringComparison.Ordinal);
    }

    // ---- Test des identifiants

    [Fact]
    public async Task Stripe_checks_the_account_by_reading_its_balance()
    {
        var accepted = new FakeServer().On("GET", "/v1/balance", """{"object":"balance"}""");
        var refused = new FakeServer().On("GET", "/v1/balance", """{"error":{"type":"invalid_request_error"}}""", HttpStatusCode.Unauthorized);

        var ok = await new StripeIntegration(accepted.Http, Options).CreateGateway(StripeKeys).CheckCredentialsAsync();
        var ko = await new StripeIntegration(refused.Http, new PaymentsOptions()).CreateGateway(StripeKeys).CheckCredentialsAsync();

        Assert.Equal(GatewayCredentialStatus.Verified, ok.Status);
        Assert.True(ok.IsSandbox);
        Assert.Empty(ok.Warnings);
        Assert.Equal("Bearer sk_test_abc", accepted.Requests[0].Authorization);
        Assert.Equal(GatewayCredentialStatus.Rejected, ko.Status);
        Assert.Equal([ProviderSupport.CheckoutReturnUrlMissing], ko.Warnings);
    }

    [Fact]
    public async Task PayPal_checks_the_token_then_reports_an_unknown_webhook()
    {
        var server = new FakeServer()
            .On("POST", "/v1/oauth2/token", """{"access_token":"tok"}""")
            .On("GET", "/v1/notifications/webhooks/wh-id", """{"name":"INVALID_RESOURCE_ID"}""", HttpStatusCode.NotFound);

        var check = await new PayPalIntegration(server.Http, Options).CreateGateway(PayPalKeys).CheckCredentialsAsync();

        Assert.Equal(GatewayCredentialStatus.Verified, check.Status);
        Assert.Equal([PayPalIntegration.WebhookUnknown], check.Warnings);
        Assert.Equal("Bearer tok", server.Requests[1].Authorization);
    }

    [Fact]
    public async Task CinetPay_and_MTN_tell_a_refused_account_from_a_valid_one()
    {
        var unknownKey = new FakeServer().On("POST", "/v2/payment/check", """{"code":"609","message":"AUTH_NOT_FOUND"}""", HttpStatusCode.BadRequest);
        var unknownTransaction = new FakeServer().On("POST", "/v2/payment/check", """{"code":"627","message":"TRANSACTION_NOT_FOUND"}""");
        var mtn = new FakeServer().On("POST", "/collection/token/", """{"error":"login_failed"}""", HttpStatusCode.Unauthorized);

        var refused = await new CinetPayIntegration(unknownKey.Http, Options).CreateGateway(CinetPayKeys).CheckCredentialsAsync();
        var valid = await new CinetPayIntegration(unknownTransaction.Http, Options).CreateGateway(CinetPayKeys).CheckCredentialsAsync();
        var mtnCheck = await new MtnMomoIntegration(mtn.Http, new PaymentsOptions { PublicApiBaseUrl = new Uri("http://localhost:5080") })
            .CreateGateway(MtnKeys).CheckCredentialsAsync();

        Assert.Equal(GatewayCredentialStatus.Rejected, refused.Status);
        Assert.Equal(GatewayCredentialStatus.Verified, valid.Status);
        Assert.Equal(GatewayCredentialStatus.Rejected, mtnCheck.Status);
        Assert.Equal([ProviderSupport.PublicApiUrlNotHttps], mtnCheck.Warnings);
    }

    [Fact]
    public async Task A_provider_outage_during_a_check_is_temporary_and_names_no_secret()
    {
        var server = new FakeServer().On("GET", "/v1/transactions", "{}", HttpStatusCode.ServiceUnavailable);

        var failure = await Assert.ThrowsAsync<PaymentGatewayException>(
            () => new FedaPayIntegration(server.Http, Options).CreateGateway(FedaPayKeys).CheckCredentialsAsync());

        Assert.False(failure.IsPermanent);
        Assert.Equal(503, failure.StatusCode);
        Assert.DoesNotContain("sk_sandbox_abc", failure.Message, StringComparison.Ordinal);
    }

    // ---- Wave

    [Fact]
    public async Task Wave_opens_a_checkout_session_and_reads_it_back()
    {
        var server = new FakeServer()
            .On("POST", "/v1/checkout/sessions", """{"id":"cos-18qq25rgr100a","wave_launch_url":"https://pay.wave.com/c/cos-18qq25rgr100a","checkout_status":"open","payment_status":"processing"}""")
            .On("GET", "/v1/checkout/sessions/cos-18qq25rgr100a", """{"id":"cos-18qq25rgr100a","amount":"2500","currency":"XOF","checkout_status":"complete","payment_status":"succeeded"}""");
        var gateway = new WaveIntegration(server.Http, Options).CreateGateway(WaveKeys);
        var request = Request(2500m, Currency.XOF);

        var checkout = await gateway.StartAsync(request);
        var status = await gateway.FetchStatusAsync(checkout.Reference);

        Assert.Equal("cos-18qq25rgr100a", checkout.Reference.Value);
        Assert.Equal("https://pay.wave.com/c/cos-18qq25rgr100a", checkout.CheckoutUrl!.AbsoluteUri);
        var sent = JsonNode.Parse(server.Requests[0].Body)!;
        Assert.Equal("Bearer wave_sn_prod_abc", server.Requests[0].Authorization);
        Assert.Equal(("2500", "XOF", request.PaymentId.Value.ToString("D")), (sent["amount"]!.GetValue<string>(), sent["currency"]!.GetValue<string>(), sent["client_reference"]!.GetValue<string>()));
        Assert.StartsWith("https://site.example.com/merci?paiement=", sent["success_url"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(new GatewayTransactionStatus(GatewayTransactionState.Captured, 2500m, "XOF"), status);
    }

    [Fact]
    public async Task A_Wave_webhook_is_signed_then_the_session_is_read_again()
    {
        var server = new FakeServer().On("GET", "/v1/checkout/sessions/cos-1",
            """{"id":"cos-1","amount":"2500","currency":"XOF","checkout_status":"complete","payment_status":"succeeded"}""");
        var reader = new WaveIntegration(server.Http, Options).CreateWebhookReader(WaveKeys);
        const string body = """{"id":"EV_1","type":"checkout.session.completed","data":{"id":"cos-1","payment_status":"succeeded"}}""";
        var signed = new Dictionary<string, string> { [WaveWebhookReader.Header] = $"t={Now.ToUnixTimeSeconds()},v1={WaveWebhookReader.Sign("wave_whsec", Now.ToUnixTimeSeconds(), body)}" };

        var read = await reader.ReadAsync(signed, body, Now);
        var forged = await reader.ReadAsync(new Dictionary<string, string> { [WaveWebhookReader.Header] = $"t={Now.ToUnixTimeSeconds()},v1=00" }, body, Now);
        var stale = await reader.ReadAsync(signed, body, Now.AddMinutes(10));

        Assert.Equal(new VerifiedWebhook("cos-1:succeeded", ProviderResultKind.Captured, "cos-1", 2500m, "XOF"), read.Webhook);
        Assert.Equal(WebhookReadStatus.Invalid, forged.Status);
        Assert.Equal(WebhookReadStatus.Invalid, stale.Status);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task Wave_refunds_the_whole_session_and_an_expired_one_fails()
    {
        var server = new FakeServer()
            .On("POST", "/v1/checkout/sessions/cos-2/refund", "{}")
            .On("GET", "/v1/checkout/sessions/cos-3", """{"id":"cos-3","amount":"900","currency":"XOF","checkout_status":"expired","payment_status":"processing"}""");
        var gateway = new WaveIntegration(server.Http, Options).CreateGateway(WaveKeys);

        await gateway.RequestRefundAsync(new GatewayRefundRequest(TenantId.New(), PaymentId.New(), RefundId.New(), ProviderReference.Create("cos-2"), Money.Of(2500m, Currency.XOF)));
        var expired = await gateway.FetchStatusAsync(ProviderReference.Create("cos-3"));

        Assert.EndsWith("/v1/checkout/sessions/cos-2/refund", server.Requests[0].Url, StringComparison.Ordinal);
        Assert.Equal(GatewayTransactionState.Failed, expired!.State);
    }

    // ---- Flutterwave

    [Fact]
    public async Task Flutterwave_opens_a_hosted_payment_with_the_account_email_and_reads_it_back()
    {
        var server = new FakeServer()
            .On("POST", "/v3/payments", """{"status":"success","data":{"link":"https://checkout.flutterwave.com/v3/hosted/pay/abc"}}""")
            .On("GET", "/v3/transactions/verify_by_reference", """{"status":"success","data":{"status":"successful","amount":2500,"currency":"XOF"}}""");
        var gateway = new FlutterwaveIntegration(server.Http, Options).CreateGateway(FlutterwaveKeys);
        var request = Request(2500m, Currency.XOF);

        var checkout = await gateway.StartAsync(request);
        var status = await gateway.FetchStatusAsync(checkout.Reference);

        var sent = JsonNode.Parse(server.Requests[0].Body)!;
        Assert.StartsWith(request.PaymentId.Value.ToString("N") + "-", checkout.Reference.Value, StringComparison.Ordinal);
        Assert.Equal(checkout.Reference.Value, sent["tx_ref"]!.GetValue<string>());
        Assert.Equal("caisse@resto.example", sent["customer"]!["email"]!.GetValue<string>());
        Assert.Equal("Bearer FLWSECK_TEST-abc", server.Requests[0].Authorization);
        Assert.Equal("https://checkout.flutterwave.com/v3/hosted/pay/abc", checkout.CheckoutUrl!.AbsoluteUri);
        Assert.Equal(new GatewayTransactionStatus(GatewayTransactionState.Captured, 2500m, "XOF"), status);
    }

    [Fact]
    public async Task A_Flutterwave_webhook_needs_the_secret_hash_then_the_transaction_is_read_again()
    {
        var server = new FakeServer().On("GET", "/v3/transactions/verify_by_reference",
            """{"status":"success","data":{"status":"successful","amount":2500,"currency":"XOF"}}""");
        var reader = new FlutterwaveIntegration(server.Http, Options).CreateWebhookReader(FlutterwaveKeys);
        const string body = """{"event":"charge.completed","data":{"tx_ref":"abc-1","status":"successful","amount":1}}""";

        var read = await reader.ReadAsync(new Dictionary<string, string> { ["Verif-Hash"] = "flw-hash" }, body, Now);
        var forged = await reader.ReadAsync(new Dictionary<string, string> { ["verif-hash"] = "autre" }, body, Now);
        var missing = await reader.ReadAsync(new Dictionary<string, string>(), body, Now);

        Assert.Equal(new VerifiedWebhook("abc-1:successful", ProviderResultKind.Captured, "abc-1", 2500m, "XOF"), read.Webhook);
        Assert.Equal(WebhookReadStatus.Invalid, forged.Status);
        Assert.Equal(WebhookReadStatus.Invalid, missing.Status);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task A_Flutterwave_payment_not_yet_known_stays_pending_and_refunds_are_refused()
    {
        var server = new FakeServer().On("GET", "/v3/transactions/verify_by_reference", """{"status":"error"}""", HttpStatusCode.BadRequest);
        var gateway = new FlutterwaveIntegration(server.Http, Options).CreateGateway(FlutterwaveKeys);

        var status = await gateway.FetchStatusAsync(ProviderReference.Create("abc-2"));
        var refused = await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.RequestRefundAsync(
            new GatewayRefundRequest(TenantId.New(), PaymentId.New(), RefundId.New(), ProviderReference.Create("abc-2"), Money.Of(1m, Currency.XOF))));

        Assert.Equal(GatewayTransactionState.Pending, status!.State);
        Assert.True(refused.IsPermanent);
        Assert.Throws<PaymentGatewayException>(() => new FlutterwaveIntegration(server.Http, Options).CreateGateway(
            new Dictionary<string, string>(FlutterwaveKeys) { ["account_email"] = "pas un email" }));
    }

    // ---- Paystack

    [Fact]
    public async Task Paystack_initializes_in_minor_units_and_reads_the_transaction_back()
    {
        var server = new FakeServer()
            .On("POST", "/transaction/initialize", """{"status":true,"data":{"authorization_url":"https://checkout.paystack.com/xyz","reference":"ref-1"}}""")
            .On("GET", "/transaction/verify/ref-1", """{"status":true,"data":{"status":"success","amount":125050,"currency":"NGN"}}""");
        var gateway = new PaystackIntegration(server.Http, Options).CreateGateway(PaystackKeys);

        var checkout = await gateway.StartAsync(Request(1250.50m, Currency.FromCode("NGN")));
        var status = await gateway.FetchStatusAsync(checkout.Reference);

        var sent = JsonNode.Parse(server.Requests[0].Body)!;
        Assert.Equal(125050, sent["amount"]!.GetValue<long>());
        Assert.Equal("caisse@resto.example", sent["email"]!.GetValue<string>());
        Assert.Equal("ref-1", checkout.Reference.Value);
        Assert.Equal(new GatewayTransactionStatus(GatewayTransactionState.Captured, 1250.50m, "NGN"), status);
    }

    [Fact]
    public async Task A_Paystack_webhook_is_signed_with_the_secret_key_then_read_again()
    {
        var server = new FakeServer().On("GET", "/transaction/verify/ref-1", """{"status":true,"data":{"status":"failed","amount":5000,"currency":"GHS"}}""");
        var reader = new PaystackIntegration(server.Http, Options).CreateWebhookReader(PaystackKeys);
        const string body = """{"event":"charge.success","data":{"reference":"ref-1","status":"success"}}""";
        const string other = """{"event":"transfer.success","data":{"reference":"ref-1"}}""";

        var read = await reader.ReadAsync(new Dictionary<string, string> { [PaystackWebhookReader.Header] = PaystackWebhookReader.Sign("sk_test_abc", body) }, body, Now);
        var forged = await reader.ReadAsync(new Dictionary<string, string> { [PaystackWebhookReader.Header] = PaystackWebhookReader.Sign("autre", body) }, body, Now);
        var ignored = await reader.ReadAsync(new Dictionary<string, string> { [PaystackWebhookReader.Header] = PaystackWebhookReader.Sign("sk_test_abc", other) }, other, Now);

        Assert.Equal(new VerifiedWebhook("ref-1:failed", ProviderResultKind.Failed, "ref-1", 50m, "GHS", Reason: "failed"), read.Webhook);
        Assert.Equal(WebhookReadStatus.Invalid, forged.Status);
        Assert.Equal(WebhookReadStatus.Ignored, ignored.Status);
        Assert.Single(server.Requests);
    }

    // ---- PayDunya

    [Fact]
    public async Task PayDunya_creates_an_invoice_with_its_three_keys_and_confirms_it()
    {
        var server = new FakeServer()
            .On("POST", "/sandbox-api/v1/checkout-invoice/create", """{"response_code":"00","response_text":"https://paydunya.com/sandbox-checkout/invoice/tok_1","token":"tok_1"}""")
            .On("GET", "/sandbox-api/v1/checkout-invoice/confirm/tok_1", """{"response_code":"00","status":"completed","invoice":{"total_amount":3000}}""");
        var gateway = new PayDunyaIntegration(server.Http, Options).CreateGateway(PayDunyaKeys);

        var checkout = await gateway.StartAsync(Request(3000m, Currency.XOF));
        var status = await gateway.FetchStatusAsync(checkout.Reference);

        var sent = server.Requests[0];
        Assert.Equal(("master", "private", "token"), (sent.Headers["PAYDUNYA-MASTER-KEY"], sent.Headers["PAYDUNYA-PRIVATE-KEY"], sent.Headers["PAYDUNYA-TOKEN"]));
        Assert.Equal("3000", JsonNode.Parse(sent.Body)!["invoice"]!["total_amount"]!.GetValue<string>());
        Assert.Equal("tok_1", checkout.Reference.Value);
        Assert.Equal(new GatewayTransactionStatus(GatewayTransactionState.Captured, 3000m, "XOF"), status);
    }

    [Fact]
    public async Task A_PayDunya_notification_needs_the_master_key_hash_and_a_refused_invoice_is_definitive()
    {
        var server = new FakeServer().On("GET", "/checkout-invoice/confirm/tok_1", """{"response_code":"00","status":"completed","invoice":{"total_amount":3000}}""");
        var reader = new PayDunyaIntegration(server.Http, Options).CreateWebhookReader(PayDunyaKeys);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA512.HashData(Encoding.UTF8.GetBytes("master")));
        var body = $"data%5Bhash%5D={hash}&data%5Binvoice%5D%5Btoken%5D=tok_1&data%5Bstatus%5D=completed";

        var read = await reader.ReadAsync(new Dictionary<string, string>(), body, Now);
        var forged = await reader.ReadAsync(new Dictionary<string, string>(), body.Replace(hash, new string('0', 128), StringComparison.Ordinal), Now);
        var refused = await Assert.ThrowsAsync<PaymentGatewayException>(() => new PayDunyaIntegration(
            new FakeServer().On("POST", "/checkout-invoice/create", """{"response_code":"1001","response_text":"Invalid"}""").Http, Options)
            .CreateGateway(PayDunyaKeys).StartAsync(Request(3000m, Currency.XOF)));

        Assert.Equal(new VerifiedWebhook("tok_1:completed", ProviderResultKind.Captured, "tok_1", 3000m, "XOF"), read.Webhook);
        Assert.Equal(WebhookReadStatus.Invalid, forged.Status);
        Assert.Single(server.Requests);
        Assert.True(refused.IsPermanent);
        Assert.DoesNotContain("master", refused.Message, StringComparison.Ordinal);
    }

    // ---- Mollie

    [Fact]
    public async Task Mollie_creates_a_payment_with_its_webhook_and_reads_it_back()
    {
        var server = new FakeServer()
            .On("POST", "/v2/payments", """{"id":"tr_abc","status":"open","_links":{"checkout":{"href":"https://www.mollie.com/checkout/select-method/abc"}}}""")
            .On("GET", "/v2/payments/tr_abc", """{"id":"tr_abc","status":"paid","amount":{"value":"12.50","currency":"EUR"}}""");
        var gateway = new MollieIntegration(server.Http, Options).CreateGateway(MollieKeys);

        var checkout = await gateway.StartAsync(Request(12.5m, Currency.EUR));
        var status = await gateway.FetchStatusAsync(checkout.Reference);

        var sent = JsonNode.Parse(server.Requests[0].Body)!;
        Assert.Equal(("12.50", "EUR"), (sent["amount"]!["value"]!.GetValue<string>(), sent["amount"]!["currency"]!.GetValue<string>()));
        Assert.StartsWith("https://api.example.com/v1/payments/webhooks/mollie/", sent["webhookUrl"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("tr_abc", checkout.Reference.Value);
        Assert.Equal(new GatewayTransactionStatus(GatewayTransactionState.Captured, 12.50m, "EUR"), status);
    }

    [Fact]
    public async Task A_Mollie_webhook_carries_only_an_id_and_the_payment_read_again_decides()
    {
        var refundId = RefundId.New();
        var server = new FakeServer()
            .On("GET", "/v2/payments/tr_paid", """{"id":"tr_paid","status":"paid","amount":{"value":"20.00","currency":"EUR"}}""")
            .On("GET", "/v2/payments/tr_refunded", $$$"""{"id":"tr_refunded","status":"paid","amount":{"value":"20.00","currency":"EUR"},"_embedded":{"refunds":[{"id":"re_1","status":"refunded","amount":{"value":"5.00","currency":"EUR"},"metadata":{"refund_id":"{{{refundId.Value:D}}}"}}]}}""");
        var reader = new MollieIntegration(server.Http, Options).CreateWebhookReader(MollieKeys);
        var none = new Dictionary<string, string>();

        var paid = await reader.ReadAsync(none, "id=tr_paid", Now);
        var refunded = await reader.ReadAsync(none, "id=tr_refunded", Now);
        var unknown = await reader.ReadAsync(none, "id=tr_inconnu", Now);
        var garbage = await reader.ReadAsync(none, "id=../../x", Now);

        Assert.Equal(new VerifiedWebhook("tr_paid:paid", ProviderResultKind.Captured, "tr_paid", 20m, "EUR"), paid.Webhook);
        Assert.Equal(new VerifiedWebhook("re_1:refunded", ProviderResultKind.RefundSucceeded, "tr_refunded", 5m, "EUR", refundId.Value), refunded.Webhook);
        Assert.Equal(WebhookReadStatus.Invalid, unknown.Status);
        Assert.Equal(WebhookReadStatus.Invalid, garbage.Status);
        Assert.Equal(3, server.Requests.Count);
    }

    [Fact]
    public async Task Mollie_refunds_carry_our_id_once_and_a_test_key_is_reported_as_sandbox()
    {
        var server = new FakeServer()
            .On("POST", "/v2/payments/tr_abc/refunds", """{"id":"re_1","status":"pending"}""")
            .On("GET", "/v2/methods", """{"count":0}""");
        var gateway = new MollieIntegration(server.Http, Options).CreateGateway(MollieKeys);
        var refund = new GatewayRefundRequest(TenantId.New(), PaymentId.New(), RefundId.New(), ProviderReference.Create("tr_abc"), Money.Of(5m, Currency.EUR));

        await gateway.RequestRefundAsync(refund);
        var check = await gateway.CheckCredentialsAsync();

        Assert.Equal("refund-" + refund.RefundId.Value.ToString("D"), server.Requests[0].Headers["Idempotency-Key"]);
        Assert.Equal(refund.RefundId.Value.ToString("D"), JsonNode.Parse(server.Requests[0].Body)!["metadata"]!["refund_id"]!.GetValue<string>());
        Assert.Equal(GatewayCredentialStatus.Verified, check.Status);
        Assert.True(check.IsSandbox);
    }

    private static readonly Dictionary<string, string> FlutterwaveKeys = new() { ["secret_key"] = "FLWSECK_TEST-abc", ["webhook_hash"] = "flw-hash", ["account_email"] = "caisse@resto.example" };
    private static readonly Dictionary<string, string> PaystackKeys = new() { ["secret_key"] = "sk_test_abc", ["account_email"] = "caisse@resto.example" };
    private static readonly Dictionary<string, string> PayDunyaKeys = new() { ["master_key"] = "master", ["private_key"] = "private", ["token"] = "token", ["environment"] = "sandbox" };
    private static readonly Dictionary<string, string> MollieKeys = new() { ["api_key"] = "test_abc" };

    private static readonly Dictionary<string, string> WaveKeys = new() { ["api_key"] = "wave_sn_prod_abc", ["webhook_secret"] = "wave_whsec" };
    private static readonly Dictionary<string, string> FedaPayKeys = new() { ["secret_key"] = "sk_sandbox_abc", ["webhook_secret"] = "whsec" };
    private static readonly Dictionary<string, string> StripeKeys = new() { ["secret_key"] = "sk_test_abc", ["webhook_secret"] = "whsec_x" };
    private static readonly Dictionary<string, string> CinetPayKeys = new() { ["api_key"] = "key", ["site_id"] = "site42" };
    private static readonly Dictionary<string, string> KkiaPayKeys = new()
    {
        ["public_key"] = "pub", ["private_key"] = "priv", ["secret_key"] = "sec", ["webhook_secret"] = "hook", ["environment"] = "sandbox",
    };
    private static readonly Dictionary<string, string> MtnKeys = new()
    {
        ["api_user"] = "user", ["api_key"] = "key", ["subscription_key"] = "sub", ["environment"] = "sandbox",
    };
    private static readonly Dictionary<string, string> PayPalKeys = new()
    {
        ["client_id"] = "id", ["client_secret"] = "secret", ["webhook_id"] = "wh-id", ["environment"] = "sandbox",
    };

    /// <summary>Répond selon (méthode, fin de chemin) ; note chaque requête reçue.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        private readonly List<(string Method, string Path, string Body, HttpStatusCode Status)> _routes = [];

        public FakeServer() => Http = new ProviderHttp(new HttpClient(this));

        public ProviderHttp Http { get; }

        public List<Sent> Requests { get; } = [];

        public FakeServer On(string method, string path, string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _routes.Insert(0, (method, path, body, status));
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var url = request.RequestUri!.GetLeftPart(UriPartial.Path);
            Requests.Add(new Sent(
                request.Method.Method,
                url,
                body,
                request.Headers.Authorization?.ToString(),
                request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase)));
            var route = _routes.FirstOrDefault(candidate => candidate.Method == request.Method.Method && url.EndsWith(candidate.Path, StringComparison.Ordinal));
            return route.Method is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(route.Status) { Content = new StringContent(route.Body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed record Sent(string Method, string Url, string Body, string? Authorization, Dictionary<string, string> Headers);
}
