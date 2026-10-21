using System.Globalization;
using System.Text.Json;
using Audit.Application.Commands;
using Audit.Domain;
using SaasRestaurant.BuildingBlocks.Application;

namespace SaasRestaurant.Host;

// Le journal d'audit est alimenté ICI : les événements sensibles des autres modules (par l'outbox, une fois par message)
// deviennent des écritures d'Audit. Aucun module n'en référence un autre : seul le host les relie.

/// <summary>
/// Traduit les événements sensibles en écritures du journal. Chaque événement retenu a sa RÈGLE : code d'action, cible,
/// auteur, et une LISTE BLANCHE de champs recopiés en métadonnées (ids, codes, statuts, montants, dates). Jamais un
/// e-mail, un nom de personne, un motif saisi librement par un client, un secret ni une référence de secret ; le domaine
/// d'Audit expurge en plus ce qui ressemble à un secret. Le tenant est celui de l'événement (absent : action de la
/// plateforme). L'origine <c>outbox.{module}.{id}</c> rend l'écriture idempotente (rejeu). Un refus métier part en file
/// morte ; les autres erreurs sont retentées.
/// </summary>
internal sealed class AuditFeed(IServiceScopeFactory scopes) : IOutboxConsumer
{
    private const int MaxValueLength = 1000;

    /// <param name="Metadata">Paires « ChampDeLÉvénement=clé_de_métadonnée ».</param>
    /// <param name="UserActorField">Champ qui désigne l'utilisateur auteur de l'action (l'utilisateur lui-même).</param>
    /// <param name="SystemActor">Nom du traitement auteur quand c'est le système.</param>
    internal sealed record Rule(string Action, string TargetType, string TargetField, string[] Metadata, string? UserActorField = null, string? SystemActor = null);

    private static Rule R(string action, string targetType, string targetField, params string[] metadata) => new(action, targetType, targetField, metadata);

    private static Rule ByUser(string action, string targetType, string targetField, string userField, params string[] metadata) =>
        new(action, targetType, targetField, metadata, UserActorField: userField);

    internal static readonly IReadOnlyDictionary<(string Module, string Type), Rule> Rules = new Dictionary<(string, string), Rule>
    {
        // --- Identity : comptes et sessions (niveau plateforme, sans restaurant). Jamais d'adresse e-mail.
        [("identity", "UserRegistered")] = ByUser("identity.user.registered", "user", "UserId", "UserId"),
        [("identity", "UserInvited")] = R("identity.user.invited", "user", "UserId"),
        [("identity", "UserInvitationAccepted")] = ByUser("identity.user.invitation_accepted", "user", "UserId", "UserId"),
        [("identity", "EmailVerified")] = ByUser("identity.user.email_verified", "user", "UserId", "UserId"),
        [("identity", "UserEmailChanged")] = ByUser("identity.user.email_changed", "user", "UserId", "UserId"),
        [("identity", "PasswordChanged")] = ByUser("identity.user.password_changed", "user", "UserId", "UserId"),
        [("identity", "UserLocked")] = R("identity.user.locked", "user", "UserId"),
        [("identity", "UserUnlocked")] = R("identity.user.unlocked", "user", "UserId", "RestoredStatus=restored_status"),
        [("identity", "UserDisabled")] = R("identity.user.disabled", "user", "UserId"),
        [("identity", "UserErased")] = R("identity.user.erased", "user", "UserId"),
        [("identity", "SessionStarted")] = ByUser("identity.session.started", "session", "SessionId", "UserId", "UserId=user_id", "ExpiresAtUtc=expires_at_utc"),
        [("identity", "SessionRevoked")] = R("identity.session.revoked", "session", "SessionId", "UserId=user_id", "Reason=reason"),
        [("identity", "RefreshTokenReplayDetected")] =
            R("identity.session.replay_detected", "session", "ReplayedSessionId", "UserId=user_id", "RevokedSessionCount=revoked_count"),
        // Profil : le fait seulement, jamais le nom, le téléphone ni la ville.
        [("identity", "UserProfileChanged")] = ByUser("identity.user.profile_changed", "user", "UserId", "UserId"),
        [("identity", "UserPreferencesChanged")] =
            ByUser("identity.user.preferences_changed", "user", "UserId", "UserId", "Language=language", "ProductNewsEmails=product_news_emails"),
        [("identity", "UserAvatarChanged")] = ByUser("identity.user.avatar_changed", "user", "UserId", "UserId", "HasAvatar=has_avatar"),

        // --- Staff : rôles, permissions, membres.
        [("staff", "RoleCreated")] = R("staff.role.created", "role", "RoleId", "Name=name", "IsSystem=is_system", "Permissions=permissions"),
        [("staff", "RoleRenamed")] = R("staff.role.renamed", "role", "RoleId", "PreviousName=previous_name", "Name=name"),
        [("staff", "RolePermissionGranted")] = R("staff.role.permission_granted", "role", "RoleId", "Permission=permission"),
        [("staff", "RolePermissionRevoked")] = R("staff.role.permission_revoked", "role", "RoleId", "Permission=permission"),
        [("staff", "RoleDeleted")] = R("staff.role.deleted", "role", "RoleId", "Name=name"),
        [("staff", "StaffInvitationResent")] = R("staff.member.invitation_resent", "staff_member", "StaffMemberId", "UserId=user_id"),
        [("staff", "StaffInvited")] = R("staff.member.invited", "staff_member", "StaffMemberId", "UserId=user_id"),
        [("staff", "StaffActivated")] = R("staff.member.activated", "staff_member", "StaffMemberId", "UserId=user_id"),
        [("staff", "StaffOwnerCreated")] = R("staff.member.owner_created", "staff_member", "StaffMemberId", "UserId=user_id"),
        [("staff", "StaffSuspended")] = R("staff.member.suspended", "staff_member", "StaffMemberId", "UserId=user_id"),
        [("staff", "StaffReinstated")] = R("staff.member.reinstated", "staff_member", "StaffMemberId", "UserId=user_id"),
        [("staff", "StaffLeft")] = R(
            "staff.member.left", "staff_member", "StaffMemberId", "UserId=user_id", "RemovedLocationIds=removed_location_ids", "HeldTenantWideRole=held_tenant_wide_role"),
        [("staff", "StaffAssignedToLocation")] = R(
            "staff.member.location_assigned", "staff_member", "StaffMemberId", "UserId=user_id", "LocationId=location_id", "RoleId=role_id"),
        [("staff", "StaffRemovedFromLocation")] = R(
            "staff.member.location_removed", "staff_member", "StaffMemberId", "UserId=user_id", "LocationId=location_id", "RoleId=role_id"),
        [("staff", "StaffAssignedTenantWide")] = R("staff.member.tenant_role_assigned", "staff_member", "StaffMemberId", "UserId=user_id", "RoleId=role_id"),
        [("staff", "StaffTenantWideAssignmentRemoved")] =
            R("staff.member.tenant_role_removed", "staff_member", "StaffMemberId", "UserId=user_id", "RoleId=role_id"),

        // --- Tenants : cycle de vie et réglages du restaurant.
        [("tenants", "TenantCreated")] = R(
            "tenants.tenant.created", "tenant", "TenantId", "Name=name", "DefaultCurrencyCode=default_currency", "TimeZoneId=time_zone"),
        [("tenants", "TenantActivated")] = R("tenants.tenant.activated", "tenant", "TenantId"),
        [("tenants", "TenantSuspended")] = R("tenants.tenant.suspended", "tenant", "TenantId", "Reason=reason", "PreviousStatus=previous_status"),
        [("tenants", "TenantReactivated")] = R("tenants.tenant.reactivated", "tenant", "TenantId"),
        [("tenants", "TenantClosed")] = R("tenants.tenant.closed", "tenant", "TenantId", "PreviousStatus=previous_status"),
        [("tenants", "TenantRenamed")] = R("tenants.tenant.renamed", "tenant", "TenantId", "PreviousName=previous_name", "NewName=new_name"),
        [("tenants", "TenantDefaultCurrencyChanged")] = R(
            "tenants.tenant.currency_changed", "tenant", "TenantId", "PreviousCurrencyCode=previous_currency", "NewCurrencyCode=new_currency"),
        [("tenants", "TenantTimeZoneChanged")] = R(
            "tenants.tenant.time_zone_changed", "tenant", "TenantId", "PreviousTimeZoneId=previous_time_zone", "NewTimeZoneId=new_time_zone"),
        [("tenants", "TenantPriceTaxModeChanged")] = R("tenants.tenant.price_tax_mode_changed", "tenant", "TenantId", "PricesIncludeTax=prices_include_tax"),
        [("tenants", "TenantBrandingUpdated")] = R("tenants.tenant.branding_updated", "tenant", "TenantId"),
        [("tenants", "TenantPublicProfileUpdated")] = R("tenants.tenant.public_profile_updated", "tenant", "TenantId"),

        // --- Loyalty : cartes cadeaux et avoirs (jamais le code : ni l'événement ni le journal ne le portent).
        [("loyalty", "StoredValueCardIssued")] = R("loyalty.stored_value.issued", "stored_value_card", "CardId", "Kind=kind", "Amount=amount"),
        [("loyalty", "StoredValueCardCancelled")] = R("loyalty.stored_value.cancelled", "stored_value_card", "CardId", "ForfeitedBalance=forfeited_balance"),

        // --- Payments : remboursements, anomalies, prestataires (plateforme) et configurations (restaurant).
        [("payments", "PaymentCapturedAfterCancellation")] =
            R("payments.payment.captured_after_cancellation", "payment", "PaymentId", "OrderId=order_id", "CapturedAmount=amount"),
        [("payments", "RefundRequested")] = R("payments.refund.requested", "refund", "RefundId", "PaymentId=payment_id", "OrderId=order_id", "Amount=amount"),
        [("payments", "RefundRegistered")] = R(
            "payments.refund.registered", "refund", "RefundId", "PaymentId=payment_id", "Amount=amount", "Outcome=outcome", "PaymentStatus=payment_status"),
        [("payments", "RefundSucceeded")] = R(
            "payments.refund.succeeded", "refund", "RefundId", "PaymentId=payment_id", "Amount=amount", "TotalRefunded=total_refunded", "IsFullRefund=is_full_refund"),
        [("payments", "PaymentProviderRegistered")] = R("payments.provider.registered", "payment_provider", "ProviderId", "Code=code", "Type=type"),
        [("payments", "PaymentProviderRenamed")] = R("payments.provider.renamed", "payment_provider", "ProviderId", "Code=code", "DisplayName=display_name"),
        [("payments", "PaymentProviderCapabilitiesChanged")] = R("payments.provider.capabilities_changed", "payment_provider", "ProviderId", "Code=code"),
        [("payments", "PaymentProviderEnabled")] = R("payments.provider.enabled", "payment_provider", "ProviderId", "Code=code"),
        [("payments", "PaymentProviderDisabled")] = R("payments.provider.disabled", "payment_provider", "ProviderId", "Code=code"),
        [("payments", "PaymentConfigurationCreated")] =
            R("payments.configuration.created", "payment_configuration", "ConfigurationId", "Provider=provider", "Mode=mode", "IsEnabled=is_enabled"),
        // Les identifiants du compte marchand ont changé (jamais leur valeur : seul le fait est journalisé).
        [("payments", "PaymentConfigurationConfigured")] = R("payments.configuration.configured", "payment_configuration", "ConfigurationId", "Provider=provider"),
        [("payments", "PaymentConfigurationModeChanged")] =
            R("payments.configuration.mode_changed", "payment_configuration", "ConfigurationId", "Provider=provider", "Mode=mode", "Status=status"),
        [("payments", "PaymentConfigurationEnabled")] = R("payments.configuration.enabled", "payment_configuration", "ConfigurationId", "Provider=provider"),
        [("payments", "PaymentConfigurationDisabled")] = R("payments.configuration.disabled", "payment_configuration", "ConfigurationId", "Provider=provider"),
        [("payments", "PaymentConfigurationMethodActivated")] =
            R("payments.configuration.method_activated", "payment_configuration", "ConfigurationId", "Provider=provider", "Method=method"),
        [("payments", "PaymentConfigurationMethodDeactivated")] =
            R("payments.configuration.method_deactivated", "payment_configuration", "ConfigurationId", "Provider=provider", "Method=method"),
        [("payments", "PaymentMethodAssignedToLocation")] = R(
            "payments.configuration.location_method_assigned", "payment_configuration", "ConfigurationId", "Provider=provider", "LocationId=location_id", "Method=method"),
        [("payments", "PaymentMethodRemovedFromLocation")] = R(
            "payments.configuration.location_method_removed", "payment_configuration", "ConfigurationId", "Provider=provider", "LocationId=location_id", "Method=method"),

        // --- Billing : plans (plateforme) et abonnement du restaurant.
        [("billing", "PlanCreated")] = R("billing.plan.created", "plan", "PlanId", "Name=name", "Price=price", "BillingInterval=billing_interval"),
        [("billing", "PlanDefinitionChanged")] = R("billing.plan.definition_changed", "plan", "PlanId", "Change=change", "Price=price"),
        [("billing", "PlanArchived")] = R("billing.plan.archived", "plan", "PlanId"),
        [("billing", "SubscriptionTrialStarted")] =
            R("billing.subscription.trial_started", "subscription", "SubscriptionId", "PlanId=plan_id", "TrialPeriod=trial_period"),
        [("billing", "SubscriptionActivated")] = R(
            "billing.subscription.activated", "subscription", "SubscriptionId", "PlanId=plan_id", "PreviousStatus=previous_status", "CurrentPeriod=current_period"),
        [("billing", "SubscriptionPlanChanged")] = R(
            "billing.subscription.plan_changed", "subscription", "SubscriptionId", "PreviousPlanId=previous_plan_id", "NewPlanId=new_plan_id"),
        [("billing", "SubscriptionPastDue")] = R("billing.subscription.past_due", "subscription", "SubscriptionId", "CurrentPeriod=current_period"),
        [("billing", "SubscriptionSuspended")] =
            R("billing.subscription.suspended", "subscription", "SubscriptionId", "PreviousStatus=previous_status", "Reason=reason"),
        [("billing", "SubscriptionCancellationScheduled")] =
            R("billing.subscription.cancellation_scheduled", "subscription", "SubscriptionId", "EffectiveAtUtc=effective_at_utc"),
        [("billing", "SubscriptionCancellationUnscheduled")] = R("billing.subscription.cancellation_unscheduled", "subscription", "SubscriptionId"),
        [("billing", "SubscriptionCancelled")] = R("billing.subscription.cancelled", "subscription", "SubscriptionId", "PlanId=plan_id", "Reason=reason"),
        [("billing", "SubscriptionPaymentRecorded")] = R(
            "billing.subscription.payment_recorded", "subscription", "SubscriptionId", "PaymentId=payment_id", "PlanId=plan_id", "Amount=amount",
            "PaidPeriod=paid_period"),
        [("billing", "SubscriptionCheckoutStarted")] = R(
            "billing.checkout.started", "subscription_checkout", "CheckoutId", "SubscriptionId=subscription_id", "Amount=amount", "Provider=provider"),
        [("billing", "SubscriptionCheckoutPaid")] = R(
            "billing.checkout.paid", "subscription_checkout", "CheckoutId", "SubscriptionId=subscription_id", "Amount=amount", "Provider=provider",
            "PaymentId=payment_id"),
        [("billing", "SubscriptionCheckoutClosed")] = R("billing.checkout.closed", "subscription_checkout", "CheckoutId", "Status=status"),

        // --- Inventory : corrections manuelles du stock (risque de fraude).
        [("inventory", "StockAdjusted")] = R(
            "inventory.stock.adjusted", "inventory_item", "InventoryItemId",
            "LocationId=location_id", "IngredientId=ingredient_id", "QuantityDelta=quantity_delta", "QuantityAfter=quantity_after", "Unit=unit", "Reason=reason"),
        [("inventory", "WasteRecorded")] = R(
            "inventory.stock.waste_recorded", "inventory_item", "InventoryItemId",
            "LocationId=location_id", "IngredientId=ingredient_id", "Quantity=quantity", "QuantityAfter=quantity_after", "Unit=unit", "Reason=reason"),
        [("inventory", "StockCountConfirmed")] = R(
            "inventory.stock.count_confirmed", "inventory_item", "InventoryItemId",
            "LocationId=location_id", "IngredientId=ingredient_id", "CountedQuantity=counted_quantity", "Unit=unit", "Reason=reason"),

        // --- Customers : preuves RGPD (consentements, anonymisation). Jamais de contact.
        [("customers", "CustomerAnonymized")] = R("customers.customer.anonymized", "customer", "CustomerId"),
        [("customers", "CustomerMerged")] = R("customers.customer.merged", "customer", "CustomerId", "MergedIntoId=merged_into_id"),
        [("customers", "ConsentGranted")] = R("customers.consent.granted", "consent", "ConsentId", "CustomerId=customer_id", "Channel=channel", "Source=source"),
        [("customers", "ConsentWithdrawn")] =
            R("customers.consent.withdrawn", "consent", "ConsentId", "CustomerId=customer_id", "Channel=channel", "Source=source", "Reason=reason"),

        // --- Notifications : prestataires (plateforme), configurations du restaurant, effacement d'un contenu.
        [("notifications", "NotificationRedacted")] = R("notifications.notification.redacted", "notification", "NotificationId"),
        [("notifications", "NotificationProviderCreated")] = R(
            "notifications.provider.created", "notification_provider", "ProviderId", "Code=code", "Channel=channel", "ConfigurationType=configuration_type"),
        [("notifications", "NotificationProviderRenamed")] = R("notifications.provider.renamed", "notification_provider", "ProviderId", "Code=code", "Name=name"),
        [("notifications", "NotificationProviderCapabilitiesUpdated")] =
            R("notifications.provider.capabilities_updated", "notification_provider", "ProviderId", "Code=code"),
        [("notifications", "NotificationProviderConfigurationTypeChanged")] = R(
            "notifications.provider.configuration_type_changed", "notification_provider", "ProviderId", "Code=code", "ConfigurationType=configuration_type"),
        [("notifications", "NotificationProviderEnabled")] = R("notifications.provider.enabled", "notification_provider", "ProviderId", "Code=code"),
        [("notifications", "NotificationProviderDisabled")] = R("notifications.provider.disabled", "notification_provider", "ProviderId", "Code=code"),
        [("notifications", "NotificationConfigurationCreated")] = R(
            "notifications.configuration.created", "notification_configuration", "ConfigurationId", "Provider=provider", "Channel=channel", "Mode=mode"),
        // Les identifiants du compte du prestataire ont changé (jamais leur valeur).
        [("notifications", "NotificationConfigurationConfigured")] =
            R("notifications.configuration.configured", "notification_configuration", "ConfigurationId", "Provider=provider"),
        [("notifications", "NotificationConfigurationModeChanged")] = R(
            "notifications.configuration.mode_changed", "notification_configuration", "ConfigurationId", "Provider=provider", "Mode=mode", "Status=status"),
        [("notifications", "NotificationConfigurationProviderChanged")] = R(
            "notifications.configuration.provider_changed", "notification_configuration", "ConfigurationId",
            "PreviousProvider=previous_provider", "Provider=provider", "Status=status"),
        [("notifications", "NotificationConfigurationEnabled")] =
            R("notifications.configuration.enabled", "notification_configuration", "ConfigurationId", "Provider=provider", "Channel=channel"),
        [("notifications", "NotificationConfigurationDisabled")] =
            R("notifications.configuration.disabled", "notification_configuration", "ConfigurationId", "Provider=provider", "Channel=channel"),

        // --- Reporting : sortie de données (export demandé).
        [("reporting", "ExportRequested")] = R(
            "reporting.export.requested", "export_job", "ExportJobId", "ExportType=export_type", "ReportDefinitionId=report_definition_id"),

        // --- Integrations : clés d'API, endpoints et secrets de webhooks (contrat du README d'Integrations §10).
        [("integrations", "ApiCredentialIssued")] =
            R("integrations.api_key.issued", "api_credential", "CredentialId", "Name=name", "Scopes=scopes", "ExpiresAtUtc=expires_at_utc"),
        [("integrations", "ApiCredentialRotated")] = R("integrations.api_key.rotated", "api_credential", "CredentialId"),
        [("integrations", "ApiCredentialRevoked")] = R("integrations.api_key.revoked", "api_credential", "CredentialId"),
        [("integrations", "WebhookEndpointCreated")] = R("integrations.webhook_endpoint.created", "webhook_endpoint", "EndpointId", "RedactedUrl=url"),
        [("integrations", "WebhookSecretReferenceRotated")] =
            R("integrations.webhook_secret.rotated", "webhook_endpoint", "EndpointId", "PreviousValidUntilUtc=previous_valid_until_utc"),
        [("integrations", "WebhookEndpointDisabled")] = R("integrations.webhook_endpoint.disabled", "webhook_endpoint", "EndpointId"),
        [("integrations", "WebhookEndpointEnabled")] = R("integrations.webhook_endpoint.enabled", "webhook_endpoint", "EndpointId"),
        [("integrations", "WebhookEndpointAutoDisabled")] = new(
            "integrations.webhook_endpoint.auto_disabled", "webhook_endpoint", "EndpointId",
            ["RedactedUrl=url", "ConsecutiveDeadLetters=consecutive_dead_letters"], SystemActor: "webhook-delivery-job"),

        // --- Reviews : traitement des avis et questionnaires. Jamais le commentaire ni l'auteur d'un avis.
        [("reviews", "FeedbackReviewed")] = ByUser("reviews.feedback.reviewed", "feedback", "FeedbackId", "ReviewedBy"),
        [("reviews", "FeedbackArchived")] = R("reviews.feedback.archived", "feedback", "FeedbackId"),
        [("reviews", "FeedbackPublished")] = R("reviews.feedback.published", "feedback", "FeedbackId"),
        [("reviews", "FeedbackUnpublished")] = R("reviews.feedback.unpublished", "feedback", "FeedbackId"),
        [("reviews", "FeedbackCustomerDetached")] = new(
            "reviews.feedback.customer_detached", "feedback", "FeedbackId", [], SystemActor: "customer-anonymization"),
        [("reviews", "SurveyPublished")] = R("reviews.survey.published", "survey", "SurveyId", "QuestionCount=question_count"),
        [("reviews", "SurveyClosed")] = R("reviews.survey.closed", "survey", "SurveyId"),
    };

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (Translate(message) is not { } command)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        _ = await scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordAuditEntryCommand, bool>>().HandleAsync(command, cancellationToken);
    }

    /// <summary>L'écriture d'audit d'un message, ou null s'il n'est pas audité.</summary>
    internal static RecordAuditEntryCommand? Translate(OutboxEnvelope message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!Rules.TryGetValue((message.Module, message.Type), out var rule))
        {
            return null;
        }

        using var content = JsonDocument.Parse(message.Content);
        var root = content.RootElement;
        Guid? tenantId = root.TryGetProperty("TenantId", out var tenant) && tenant.ValueKind == JsonValueKind.String ? tenant.GetGuid() : null;
        var target = root.TryGetProperty(rule.TargetField, out var targetValue) ? Text(targetValue) : null;
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in rule.Metadata)
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (root.TryGetProperty(pair[..separator], out var value) && Text(value) is { } text)
            {
                metadata[pair[(separator + 1)..]] = text.Length > MaxValueLength ? text[..MaxValueLength] : text;
            }
        }

        var (actorKind, actorValue) = Actor(root, rule);
        return new RecordAuditEntryCommand(
            tenantId,
            actorKind,
            actorValue,
            rule.Action,
            rule.TargetType,
            target,
            $"outbox.{message.Module}.{message.Id.ToString(CultureInfo.InvariantCulture)}",
            metadata,
            message.OccurredAtUtc);
    }

    /// <summary>
    /// L'auteur : le champ <c>Actor</c> de l'événement s'il en a un (Integrations), sinon l'utilisateur désigné par la règle,
    /// sinon le traitement système nommé par la règle, sinon inconnu.
    /// </summary>
    private static (ActorType? Kind, string? Value) Actor(JsonElement root, Rule rule)
    {
        if (root.TryGetProperty("Actor", out var actor) && actor.ValueKind == JsonValueKind.Object
            && actor.TryGetProperty("Kind", out var kind) && Enum.TryParse<ActorType>(kind.GetString(), out var type))
        {
            return type == ActorType.System
                ? (type, actor.TryGetProperty("Name", out var name) && name.GetString() is { Length: > 0 } system ? system : "system")
                : (type, actor.TryGetProperty("Id", out var id) ? id.GetString() : null);
        }

        if (rule.UserActorField is { } field && root.TryGetProperty(field, out var user) && user.ValueKind == JsonValueKind.String)
        {
            return (ActorType.User, user.GetString());
        }

        return rule.SystemActor is { } component ? (ActorType.System, component) : (null, null);
    }

    /// <summary>Une valeur de l'événement en texte : ids et codes à plat, montants « 12.50 EUR », périodes « début/fin », listes « a,b ».</summary>
    private static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Array => string.Join(",", value.EnumerateArray().Select(Text).Where(item => item is not null)),
        JsonValueKind.Object when value.TryGetProperty("Amount", out var amount) && value.TryGetProperty("Currency", out var currency) =>
            amount.GetRawText() + " " + (Text(currency) ?? string.Empty),
        JsonValueKind.Object when value.TryGetProperty("StartUtc", out var start) && value.TryGetProperty("EndUtc", out var end) =>
            Text(start) + "/" + Text(end),
        JsonValueKind.Object when value.TryGetProperty("Code", out var code) => Text(code),
        JsonValueKind.Object when value.TryGetProperty("Value", out var inner) => Text(inner),
        _ => null,
    };
}
