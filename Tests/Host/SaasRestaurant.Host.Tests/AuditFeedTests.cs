using System.Reflection;
using Audit.Domain;
using SaasRestaurant.BuildingBlocks.Application;

namespace SaasRestaurant.Host.Tests;

/// <summary>
/// La traduction des événements en écritures d'audit (sans base) : règle, tenant, auteur, liste blanche des champs,
/// origine idempotente. Le contenu des messages reproduit le JSON de l'outbox (ids à plat, enums en texte).
/// </summary>
public sealed class AuditFeedTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private static OutboxEnvelope Message(string module, string type, string json, long id = 7) => new(module, id, type, json, At);

    [Fact]
    public void An_unaudited_event_produces_nothing()
    {
        Assert.Null(AuditFeed.Translate(Message("orders", "OrderPlaced", "{}")));
    }

    [Fact]
    public void A_staff_event_becomes_an_entry_of_its_restaurant_with_only_the_whitelisted_fields()
    {
        var tenant = Guid.NewGuid();
        var role = Guid.NewGuid();

        var command = AuditFeed.Translate(Message(
            "staff", "RolePermissionGranted",
            $$"""{"TenantId":"{{tenant}}","RoleId":"{{role}}","Permission":"orders.view","Secret":"x","OccurredAtUtc":"2026-10-10T12:00:00+00:00"}"""));

        Assert.NotNull(command);
        Assert.Equal((tenant, "staff.role.permission_granted", "role", role.ToString()), (command.TenantId, command.Action, command.TargetType, command.TargetId));
        var only = Assert.Single(command.Metadata!);
        Assert.Equal(("permission", "orders.view"), (only.Key, only.Value));
        Assert.Equal(("outbox.staff.7", At), (command.CorrelationId, command.OccurredAtUtc));
        Assert.Null(command.ActorKind);
    }

    [Fact]
    public void An_identity_event_is_a_platform_entry_without_any_email()
    {
        var user = Guid.NewGuid();

        var command = AuditFeed.Translate(Message(
            "identity", "UserEmailChanged", $$"""{"UserId":"{{user}}","PreviousEmail":"a@example.com","NewEmail":"b@example.com"}"""));

        Assert.NotNull(command);
        Assert.Null(command.TenantId);
        Assert.Equal((ActorType.User, user.ToString()), (command.ActorKind, command.ActorValue));
        Assert.Empty(command.Metadata!);
    }

    [Fact]
    public void The_author_carried_by_an_integrations_event_is_kept()
    {
        var credential = Guid.NewGuid();

        var command = AuditFeed.Translate(Message(
            "integrations", "ApiCredentialRevoked",
            $$$"""{"TenantId":"{{{Guid.NewGuid()}}}","CredentialId":"{{{credential}}}","Actor":{"Kind":"ApiCredential","Id":"{{{credential}}}","Name":null}}"""));
        var auto = AuditFeed.Translate(Message(
            "integrations", "WebhookEndpointAutoDisabled",
            $$"""{"TenantId":"{{Guid.NewGuid()}}","EndpointId":"{{Guid.NewGuid()}}","RedactedUrl":"https://hooks.example.com/***","ConsecutiveDeadLetters":5}"""));

        Assert.Equal((ActorType.ApiCredential, credential.ToString()), (command!.ActorKind, command.ActorValue));
        Assert.Equal((ActorType.System, "webhook-delivery-job"), (auto!.ActorKind, auto.ActorValue));
        Assert.Equal(("https://hooks.example.com/***", "5"), (auto.Metadata!["url"], auto.Metadata["consecutive_dead_letters"]));
    }

    [Fact]
    public void An_export_is_audited_with_the_user_or_the_api_key_that_requested_it()
    {
        var user = Guid.NewGuid();
        var credential = Guid.NewGuid();

        var byUser = AuditFeed.Translate(Message(
            "reporting", "ExportRequested",
            $$$"""{"TenantId":"{{{Guid.NewGuid()}}}","ExportJobId":"{{{Guid.NewGuid()}}}","ExportType":"report.csv","Actor":{"Kind":"User","Id":"{{{user}}}","User":"{{{user}}}"},"ReportDefinitionId":null}"""));
        var byKey = AuditFeed.Translate(Message(
            "reporting", "ExportRequested",
            $$$"""{"TenantId":"{{{Guid.NewGuid()}}}","ExportJobId":"{{{Guid.NewGuid()}}}","ExportType":"report.csv","Actor":{"Kind":"ApiCredential","Id":"{{{credential}}}","User":null},"ReportDefinitionId":null}"""));

        Assert.Equal((ActorType.User, user.ToString()), (byUser!.ActorKind, byUser.ActorValue));
        Assert.Equal((ActorType.ApiCredential, credential.ToString()), (byKey!.ActorKind, byKey.ActorValue));
        Assert.Equal("report.csv", byKey.Metadata!["export_type"]);
    }

    [Fact]
    public void Reviewing_a_feedback_is_audited_with_its_reviewer_and_never_the_comment()
    {
        var reviewer = Guid.NewGuid();
        var feedback = Guid.NewGuid();

        var reviewed = AuditFeed.Translate(Message(
            "reviews", "FeedbackReviewed",
            $$"""{"TenantId":"{{Guid.NewGuid()}}","FeedbackId":"{{feedback}}","ReviewedBy":"{{reviewer}}","Comment":"x"}"""));
        var detached = AuditFeed.Translate(Message(
            "reviews", "FeedbackCustomerDetached", $$"""{"TenantId":"{{Guid.NewGuid()}}","FeedbackId":"{{feedback}}"}"""));

        Assert.Equal(("reviews.feedback.reviewed", "feedback", feedback.ToString()), (reviewed!.Action, reviewed.TargetType, reviewed.TargetId));
        Assert.Equal((ActorType.User, reviewer.ToString()), (reviewed.ActorKind, reviewed.ActorValue));
        Assert.Empty(reviewed.Metadata!);
        Assert.Equal((ActorType.System, "customer-anonymization"), (detached!.ActorKind, detached.ActorValue));
        // Un avis soumis n'est pas une action sensible : il n'est pas audité.
        Assert.Null(AuditFeed.Translate(Message("reviews", "FeedbackSubmitted", "{}")));
    }

    [Fact]
    public void Amounts_lists_and_periods_are_written_as_plain_text()
    {
        var command = AuditFeed.Translate(Message(
            "payments", "RefundSucceeded",
            $$$"""{"TenantId":"{{{Guid.NewGuid()}}}","PaymentId":"p-1","RefundId":"f-1","Amount":{"Amount":12.5,"Currency":{"Code":"EUR"}},"TotalRefunded":{"Amount":12.5,"Currency":"EUR"},"IsFullRefund":true}"""));

        Assert.Equal(
            ("12.5 EUR", "12.5 EUR", "true"),
            (command!.Metadata!["amount"], command.Metadata["total_refunded"], command.Metadata["is_full_refund"]));
    }

    [Fact]
    public void Every_rule_has_a_valid_action_and_target_type_and_safe_metadata_keys()
    {
        foreach (var (key, rule) in AuditFeed.Rules)
        {
            Assert.Equal(rule.Action, AuditAction.Create(rule.Action).Value);
            Assert.Equal(rule.TargetType, AuditTarget.Create(rule.TargetType, "x").Type);
            var keys = rule.Metadata.Select(pair => pair[(pair.IndexOf('=', StringComparison.Ordinal) + 1)..]).ToList();
            // Une clé jugée sensible serait expurgée : la règle perdrait l'information sans le dire.
            Assert.All(keys, metadataKey => Assert.False(AuditMetadata.IsSensitiveKey(metadataKey), $"{key}: {metadataKey}"));
            Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    [Fact]
    public void Every_audit_rule_reads_fields_its_domain_event_carries()
    {
        var missing = new List<string>();
        foreach (var ((module, type), rule) in AuditFeed.Rules)
        {
            var assemblyName = char.ToUpperInvariant(module[0]) + module[1..] + ".Domain";
            var domainEvent = Assembly.Load(new AssemblyName(assemblyName)).GetTypes()
                .SingleOrDefault(candidate => candidate.Name == type && typeof(SaasRestaurant.BuildingBlocks.Domain.IDomainEvent).IsAssignableFrom(candidate));
            if (domainEvent is null)
            {
                missing.Add($"{module}.{type}");
                continue;
            }

            var fields = rule.Metadata.Select(pair => pair[..pair.IndexOf('=', StringComparison.Ordinal)]).Append(rule.TargetField);
            if (rule.UserActorField is { } userField)
            {
                fields = fields.Append(userField);
            }

            missing.AddRange(fields.Where(field => domainEvent.GetProperty(field) is null).Select(field => $"{module}.{type}.{field}"));
        }

        Assert.Empty(missing);
    }
}
