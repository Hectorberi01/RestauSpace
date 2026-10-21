using System.Net;
using System.Text;
using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Notifications.Application.Sending;
using Notifications.Domain;
using Notifications.Infrastructure.Persistence;
using Notifications.Infrastructure.Senders;
using Notifications.Infrastructure.Webhooks;

namespace Notifications.Infrastructure;

public sealed class ProviderIntegrationsTests
{
    private static readonly Dictionary<string, string> SmtpCredentials = new()
    {
        ["host"] = "smtp.example.com",
        ["port"] = "587",
        ["security"] = "starttls",
        ["username"] = "apikey",
        ["password"] = "s3cret pass",
        ["from_email"] = "bonjour@restauspace.example",
        ["from_name"] = "RestauSpace",
    };

    private static readonly Dictionary<string, string> MetaCredentials = new()
    {
        ["access_token"] = "EAAG-token",
        ["phone_number_id"] = "1234567890",
        ["app_secret"] = "app-secret",
        ["verify_token"] = "verify-me",
        ["template_name"] = "notification_generique",
        ["template_language"] = "fr",
    };

    private static NotificationSendRequest Request(NotificationRecipient recipient, string? subject, string body) => new(
        NotificationId.New(), NotificationDeliveryId.New(), TenantId.New(), "reservation.confirmed", NotificationPriority.Normal,
        recipient, NotificationContent.Create(recipient.Channel, subject, body));

    // --- SMTP.

    private sealed class RecordingTransport : ISmtpTransport
    {
        public List<(SmtpAccount Account, MimeMessage Message)> Sent { get; } = [];

        public Exception? Failure { get; set; }

        public Task SendAsync(SmtpAccount account, MimeMessage message, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Sent.Add((account, message));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Smtp_sends_with_the_account_of_the_vault_and_returns_the_message_id()
    {
        var transport = new RecordingTransport();
        var factory = new SmtpSenderFactory(transport);
        var sender = factory.Create(NotificationChannel.Email, SmtpCredentials);
        var request = Request(NotificationRecipient.ForEmail(Email.Create("kofi@example.com")), "Réservation confirmée", "Bonjour Kofi,\nà ce soir.");

        var result = await sender.SendAsync(request);

        Assert.True(result.IsAccepted);
        var (account, message) = Assert.Single(transport.Sent);
        Assert.Equal(("smtp.example.com", 587, SecureSocketOptions.StartTls, "s3cret pass"), (account.Host, account.Port, account.Security, account.Password));
        Assert.Equal("kofi@example.com", ((MailboxAddress)message.To[0]).Address);
        var from = (MailboxAddress)message.From[0];
        Assert.Equal(("RestauSpace", "bonjour@restauspace.example"), (from.Name, from.Address));
        Assert.Equal("Réservation confirmée", message.Subject);
        Assert.Equal($"{request.DeliveryId.Value:N}@restauspace.example", result.MessageId!.Value);
        Assert.Null(message.HtmlBody);
        Assert.DoesNotContain("s3cret", account.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Smtp_sends_html_when_the_body_is_html()
    {
        var request = Request(NotificationRecipient.ForEmail(Email.Create("kofi@example.com")), "Sujet", "<p>Bonjour</p>");

        var message = SmtpSenderFactory.Build(SmtpAccount.From(SmtpCredentials), request);

        Assert.Equal("<p>Bonjour</p>", message.HtmlBody);
    }

    [Theory]
    [InlineData("port", "70000")]
    [InlineData("security", "quantum")]
    [InlineData("from_email", "pas-une-adresse")]
    [InlineData("host", " ")]
    public void An_incomplete_smtp_account_cannot_send(string field, string value)
    {
        var credentials = new Dictionary<string, string>(SmtpCredentials) { [field] = value };

        Assert.Throws<InvalidOperationException>(() => new SmtpSenderFactory(new RecordingTransport()).Create(NotificationChannel.Email, credentials));
    }

    [Fact]
    public async Task Smtp_errors_become_failures_without_the_server_message()
    {
        var transport = new RecordingTransport
        {
            Failure = new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "550 kofi@example.com unknown"),
        };
        var sender = new SmtpSenderFactory(transport).Create(NotificationChannel.Email, SmtpCredentials);

        var result = await sender.SendAsync(Request(NotificationRecipient.ForEmail(Email.Create("kofi@example.com")), "Sujet", "Corps"));

        Assert.False(result.IsAccepted);
        Assert.Equal((NotificationFailure.InvalidEmail, true, (string?)null), (result.Failure!.Code, result.Failure.IsPermanent, result.Failure.Reason));
        Assert.Equal("PROVIDER_AUTH_FAILED", SmtpSenderFactory.Translate(new AuthenticationException("bad")).Code);
        Assert.False(SmtpSenderFactory.Translate(new IOException("reset")).IsPermanent);
    }

    // --- WhatsApp Cloud API (Meta).

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static (INotificationSender Sender, StubHandler Handler) Meta(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        var factory = new WhatsAppMetaSenderFactory(new HttpClient(handler), new WhatsAppMetaOptions());
        return (factory.Create(NotificationChannel.WhatsApp, MetaCredentials), handler);
    }

    [Fact]
    public async Task WhatsApp_sends_the_rendered_message_through_the_generic_template()
    {
        var (sender, handler) = Meta(HttpStatusCode.OK, """{"messages":[{"id":"wamid.HBgM"}]}""");

        var result = await sender.SendAsync(Request(
            NotificationRecipient.ForWhatsApp(PhoneNumber.Create("+229 97 00 00 00")), null, "Bonjour Kofi,\n\nTable pour 4 à 20 h."));

        Assert.True(result.IsAccepted);
        Assert.Equal("wamid.HBgM", result.MessageId!.Value);
        Assert.Equal("https://graph.facebook.com/v23.0/1234567890/messages", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer EAAG-token", handler.Request.Headers.Authorization!.ToString());
        using var payload = JsonDocument.Parse(handler.Body!);
        Assert.Equal("22997000000", payload.RootElement.GetProperty("to").GetString());
        var template = payload.RootElement.GetProperty("template");
        Assert.Equal("notification_generique", template.GetProperty("name").GetString());
        Assert.Equal("fr", template.GetProperty("language").GetProperty("code").GetString());
        Assert.Equal(
            "Bonjour Kofi, · Table pour 4 à 20 h.",
            template.GetProperty("components")[0].GetProperty("parameters")[0].GetProperty("text").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, 131026, "INVALID_RECIPIENT", true)]
    [InlineData(HttpStatusCode.BadRequest, 132001, "TEMPLATE_INVALID", true)]
    [InlineData(HttpStatusCode.TooManyRequests, 130429, "RATE_LIMITED", false)]
    [InlineData(HttpStatusCode.Unauthorized, 190, "PROVIDER_AUTH_FAILED", false)]
    [InlineData(HttpStatusCode.InternalServerError, 1, "PROVIDER_UNAVAILABLE", false)]
    [InlineData(HttpStatusCode.BadRequest, 100, "WA_100", true)]
    public async Task WhatsApp_errors_are_translated_from_their_code_only(HttpStatusCode status, int code, string expected, bool permanent)
    {
        var (sender, _) = Meta(status, $$$"""{"error":{"code":{{{code}}},"message":"Recipient +22997000000 is not valid"}}""");

        var result = await sender.SendAsync(Request(NotificationRecipient.ForWhatsApp(PhoneNumber.Create("+22997000000")), null, "Bonjour"));

        Assert.False(result.IsAccepted);
        Assert.Equal((expected, permanent, (string?)null), (result.Failure!.Code, result.Failure.IsPermanent, result.Failure.Reason));
    }

    [Fact]
    public async Task A_message_too_long_for_a_template_parameter_is_refused_before_calling_meta()
    {
        var (sender, handler) = Meta(HttpStatusCode.OK, "{}");

        var result = await sender.SendAsync(Request(NotificationRecipient.ForWhatsApp(PhoneNumber.Create("+22997000000")), null, new string('a', 1100)));

        Assert.Equal("CONTENT_TOO_LONG", result.Failure!.Code);
        Assert.Null(handler.Request);
    }

    [Fact]
    public void WhatsApp_webhooks_are_read_only_when_signed_with_the_app_secret()
    {
        var reader = new WhatsAppMetaWebhookReader();
        var body = Encoding.UTF8.GetBytes("""
            {"object":"whatsapp_business_account","entry":[{"changes":[{"value":{"statuses":[
              {"id":"wamid.1","status":"delivered","recipient_id":"22997000000"},
              {"id":"wamid.2","status":"failed","errors":[{"code":131026,"title":"Message undeliverable"}]},
              {"id":"wamid.3","status":"sent"}
            ]}}]}]}
            """);
        var signed = new Dictionary<string, string> { ["X-Hub-Signature-256"] = WhatsAppMetaWebhookReader.Sign("app-secret", body) };

        var events = reader.Read(signed, body, MetaCredentials);

        Assert.NotNull(events);
        Assert.Equal(
            new (string, WebhookEventKind, string?, bool)[]
            {
                ("wamid.1:delivered", WebhookEventKind.Delivered, null, false),
                ("wamid.2:failed", WebhookEventKind.Failed, "INVALID_RECIPIENT", true),
                ("wamid.3:sent", WebhookEventKind.Ignored, null, false),
            },
            events.Select(item => (item.EventId, item.Kind, item.FailureCode, item.IsPermanentFailure)).ToArray());
        Assert.Null(reader.Read(new Dictionary<string, string> { ["X-Hub-Signature-256"] = WhatsAppMetaWebhookReader.Sign("other", body) }, body, MetaCredentials));
    }

    [Fact]
    public void The_subscription_challenge_is_answered_only_with_the_right_verify_token()
    {
        var reader = new WhatsAppMetaWebhookReader();
        var query = new Dictionary<string, string> { ["hub.mode"] = "subscribe", ["hub.verify_token"] = "verify-me", ["hub.challenge"] = "1158201444" };

        Assert.Equal("1158201444", reader.Answer(query, MetaCredentials));
        Assert.Null(reader.Answer(new Dictionary<string, string>(query) { ["hub.verify_token"] = "guess" }, MetaCredentials));
    }

    // --- Mailjet (API Send v3.1).

    private static readonly Dictionary<string, string> MailjetCredentials = new()
    {
        ["api_key"] = "mj-public",
        ["secret_key"] = "mj-secret",
        ["from_email"] = "bonjour@chezawa.example",
        ["from_name"] = "Chez Awa",
        ["webhook_password"] = "hook-pass",
    };

    private static (INotificationSender Sender, StubHandler Handler) Mailjet(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        return (new MailjetSenderFactory(new HttpClient(handler)).Create(NotificationChannel.Email, MailjetCredentials), handler);
    }

    [Fact]
    public async Task Mailjet_sends_with_the_account_keys_and_returns_its_message_id()
    {
        var (sender, handler) = Mailjet(HttpStatusCode.OK,
            """{"Messages":[{"Status":"success","To":[{"Email":"kofi@example.com","MessageID":288230376151711744}]}]}""");
        var request = Request(NotificationRecipient.ForEmail(Email.Create("kofi@example.com")), "Réservation confirmée", "<p>Bonjour Kofi</p>");

        var result = await sender.SendAsync(request);

        Assert.True(result.IsAccepted);
        Assert.Equal("288230376151711744", result.MessageId!.Value);
        Assert.Equal(MailjetSenderFactory.SendUrl, handler.Request!.RequestUri!.AbsoluteUri);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("mj-public:mj-secret")), handler.Request.Headers.Authorization!.Parameter);
        using var sent = JsonDocument.Parse(handler.Body!);
        var message = sent.RootElement.GetProperty("Messages")[0];
        Assert.Equal("bonjour@chezawa.example", message.GetProperty("From").GetProperty("Email").GetString());
        Assert.Equal("<p>Bonjour Kofi</p>", message.GetProperty("HTMLPart").GetString());
        Assert.Equal(request.DeliveryId.Value.ToString("N"), message.GetProperty("CustomID").GetString());
    }

    [Fact]
    public async Task A_Mailjet_refusal_keeps_only_its_code_never_the_address()
    {
        var (invalid, _) = Mailjet(HttpStatusCode.BadRequest,
            """{"Messages":[{"Status":"error","Errors":[{"ErrorCode":"mj-0013","ErrorMessage":"kofi@ is an invalid email address.","ErrorRelatedTo":["To[0].Email"]}]}]}""");
        var (denied, _) = Mailjet(HttpStatusCode.Unauthorized, "{}");
        var request = Request(NotificationRecipient.ForEmail(Email.Create("kofi@example.com")), "Sujet", "Texte");

        var refused = await invalid.SendAsync(request);
        var unauthorized = await denied.SendAsync(request);

        Assert.Equal((NotificationFailure.InvalidEmail, true), (refused.Failure!.Code, refused.Failure.IsPermanent));
        Assert.Equal(("PROVIDER_AUTH_FAILED", false), (unauthorized.Failure!.Code, unauthorized.Failure.IsPermanent));
    }

    [Fact]
    public void Mailjet_events_need_the_webhook_password_then_map_sent_and_bounces()
    {
        var reader = new MailjetWebhookReader();
        var body = Encoding.UTF8.GetBytes(
            """[{"event":"sent","time":1760184000,"MessageID":288230376151711744},{"event":"bounce","time":1760184001,"MessageID":288230376151711745,"hard_bounce":true},{"event":"open","time":1760184002,"MessageID":288230376151711744}]""");
        Dictionary<string, string> Auth(string password) =>
            new() { ["Authorization"] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("mailjet:" + password)) };

        var events = reader.Read(Auth("hook-pass"), body, MailjetCredentials);

        Assert.NotNull(events);
        Assert.Equal(
            [WebhookEventKind.Delivered, WebhookEventKind.Failed, WebhookEventKind.Ignored],
            events.Select(item => item.Kind).ToArray());
        Assert.Equal(("288230376151711745", NotificationFailure.InvalidEmail, true), (events[1].ProviderMessageId, events[1].FailureCode, events[1].IsPermanentFailure));
        Assert.Null(reader.Read(Auth("guess"), body, MailjetCredentials));
        Assert.Null(reader.Read(new Dictionary<string, string>(), body, MailjetCredentials));
    }
}
