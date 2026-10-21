using System.Globalization;
using System.Text.Json;
using Integrations.Domain;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Integrations.Infrastructure.Persistence;

/// <summary>Conversions des ids et objets-valeurs : toute valeur relue repasse par la fabrique du domaine.</summary>
internal static class IntegrationsConverters
{
    public const string OwnerProperty = "OwnerId";

    public static readonly ValueConverter<TenantId, Guid> TenantKey = new(id => id.Value, value => TenantId.From(value));
    public static readonly ValueConverter<WebhookEndpointId, Guid> EndpointKey = new(id => id.Value, value => WebhookEndpointId.From(value));
    public static readonly ValueConverter<DeliveryId, Guid> DeliveryKey = new(id => id.Value, value => DeliveryId.From(value));
    public static readonly ValueConverter<CredentialId, Guid> CredentialKey = new(id => id.Value, value => CredentialId.From(value));
    public static readonly ValueConverter<EventId, Guid> EventKey = new(id => id.Value, value => EventId.From(value));
    public static readonly ValueConverter<EventTypeCode, string> EventType = new(code => code.Value, value => EventTypeCode.Create(value));

    public static readonly ValueConverter<HashSet<EventTypeCode>, string> EventTypes =
        new(codes => WriteEventTypes(codes), json => ReadEventTypes(json));

    /// <summary>Le domaine modifie l'ensemble sur place : instantané par copie, comparaison ensembliste.</summary>
    public static readonly ValueComparer<HashSet<EventTypeCode>> EventTypesComparer = new(
        (left, right) => left!.SetEquals(right!),
        codes => codes.Aggregate(0, (hash, code) => hash ^ code.GetHashCode()),
        codes => new HashSet<EventTypeCode>(codes));

    public static string WriteEventTypes(HashSet<EventTypeCode> codes) =>
        JsonSerializer.Serialize(codes.Select(code => code.Value).OrderBy(code => code, StringComparer.Ordinal).ToArray());

    public static HashSet<EventTypeCode> ReadEventTypes(string json) =>
        (JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>()).Select(EventTypeCode.Create).ToHashSet();

    /// <summary>« status IN (1, 2...) », généré depuis l'enum : ajouter une valeur change le modèle, donc la migration.</summary>
    public static string In<TEnum>(string column)
        where TEnum : struct, Enum =>
        column + " IN (" + string.Join(", ", Enum.GetValues<TEnum>().Select(value => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture))) + ")";

    public static string Value<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Agrégat <see cref="WebhookEndpoint"/> : <c>webhook_endpoints</c>, événements souscrits dans la colonne <c>events</c>.
/// La référence du secret est stockée, jamais le secret.
/// </summary>
internal sealed class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<WebhookEndpoint> builder)
    {
        builder.ToTable("webhook_endpoints", table =>
        {
            table.HasCheckConstraint("ck_webhook_endpoints_status", IntegrationsConverters.In<WebhookEndpointStatus>("status"));
            table.HasCheckConstraint(
                "ck_webhook_endpoints_previous_secret", "(previous_secret_reference IS NULL) = (previous_secret_expires_at_utc IS NULL)");
            table.HasCheckConstraint("ck_webhook_endpoints_consecutive_dead_letters", "consecutive_dead_letters >= 0");
        });

        builder.HasKey(endpoint => endpoint.Id);
        builder.Property(endpoint => endpoint.Id).HasColumnName("id").HasConversion(IntegrationsConverters.EndpointKey).ValueGeneratedNever();
        builder.Property(endpoint => endpoint.TenantId).HasColumnName("tenant_id").HasConversion(IntegrationsConverters.TenantKey);
        builder.Property(endpoint => endpoint.Url)
            .HasColumnName("url")
            .HasConversion(url => url.Value, value => WebhookUrl.Create(value))
            .HasMaxLength(WebhookUrl.MaxLength);
        builder.Property(endpoint => endpoint.SecretReference)
            .HasColumnName("secret_reference")
            .HasConversion(reference => reference.Value, value => SecretReference.Create(value))
            .HasMaxLength(SecretReference.MaxLength);
        builder.Property(endpoint => endpoint.Status).HasColumnName("status");
        builder.Property(endpoint => endpoint.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(endpoint => endpoint.SecretRotatedAtUtc).HasColumnName("secret_rotated_at_utc");
        builder.Property(endpoint => endpoint.PreviousSecretReference)
            .HasColumnName("previous_secret_reference")
            .HasConversion(reference => reference!.Value, value => SecretReference.Create(value))
            .HasMaxLength(SecretReference.MaxLength);
        builder.Property(endpoint => endpoint.PreviousSecretExpiresAtUtc).HasColumnName("previous_secret_expires_at_utc");
        builder.Property(endpoint => endpoint.ConsecutiveDeadLetters).HasColumnName("consecutive_dead_letters");
        builder.Property(endpoint => endpoint.AutoDisabledAtUtc).HasColumnName("auto_disabled_at_utc");

        // Les codes souscrits : un tableau JSON trié dans la ligne (une liste courte, toujours lue avec l'endpoint).
        builder.Property<HashSet<EventTypeCode>>("_subscribedEvents")
            .HasColumnName("events")
            .HasColumnType("jsonb")
            .HasConversion(IntegrationsConverters.EventTypes, IntegrationsConverters.EventTypesComparer);
        builder.Ignore(endpoint => endpoint.SubscribedEvents);

        builder.HasIndex(endpoint => new { endpoint.TenantId, endpoint.Status }).HasDatabaseName("ix_webhook_endpoints_tenant_id_status");
        // File du job : anciens secrets dont la période de grâce se termine.
        builder.HasIndex(endpoint => endpoint.PreviousSecretExpiresAtUtc)
            .HasFilter("previous_secret_expires_at_utc IS NOT NULL")
            .HasDatabaseName("ix_webhook_endpoints_previous_secret_expires_at_utc");

        // Concurrence optimiste sur la colonne système xmin de PostgreSQL.
        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(endpoint => endpoint.DomainEvents);
    }
}

/// <summary>
/// Agrégat <see cref="WebhookDelivery"/> : <c>webhook_deliveries</c>. Le corps figé et la politique de retry sont des
/// types complexes (colonnes de la ligne ; une même instance peut servir à plusieurs livraisons). Une seule livraison
/// non abandonnée par (endpoint, événement) : l'index unique filtré le garantit, un renvoi reste possible.
/// </summary>
internal sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public const string LiveDeliveryIndex = "ix_webhook_deliveries_endpoint_id_event_id_live";

    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        var deadLetter = IntegrationsConverters.Value(WebhookDeliveryStatus.DeadLetter);
        var succeeded = IntegrationsConverters.Value(WebhookDeliveryStatus.Succeeded);
        builder.ToTable("webhook_deliveries", table =>
        {
            table.HasCheckConstraint("ck_webhook_deliveries_status", IntegrationsConverters.In<WebhookDeliveryStatus>("status"));
            table.HasCheckConstraint("ck_webhook_deliveries_attempt_count", "attempt_count >= 0 AND attempt_count <= retry_max_attempts");
            table.HasCheckConstraint("ck_webhook_deliveries_succeeded_at", $"(status = {succeeded}) = (succeeded_at_utc IS NOT NULL)");
            table.HasCheckConstraint("ck_webhook_deliveries_dead_lettered_at", $"(status = {deadLetter}) = (dead_lettered_at_utc IS NOT NULL)");
        });

        builder.HasKey(delivery => delivery.Id);
        builder.Property(delivery => delivery.Id).HasColumnName("id").HasConversion(IntegrationsConverters.DeliveryKey).ValueGeneratedNever();
        builder.Property(delivery => delivery.TenantId).HasColumnName("tenant_id").HasConversion(IntegrationsConverters.TenantKey);
        builder.Property(delivery => delivery.EndpointId).HasColumnName("endpoint_id").HasConversion(IntegrationsConverters.EndpointKey);
        builder.Property(delivery => delivery.EventId).HasColumnName("event_id").HasConversion(IntegrationsConverters.EventKey);

        builder.ComplexProperty(delivery => delivery.Payload, payload =>
        {
            payload.Property(p => p.EventType).HasColumnName("event_type").HasConversion(IntegrationsConverters.EventType).HasMaxLength(EventTypeCode.MaxLength);
            payload.Property(p => p.SchemaVersion).HasColumnName("schema_version");
            payload.Property(p => p.Payload).HasColumnName("payload").HasMaxLength(WebhookPayloadSnapshot.MaxPayloadLength);
        });
        builder.ComplexProperty(delivery => delivery.RetryPolicy, policy =>
        {
            policy.Property(p => p.MaxAttempts).HasColumnName("retry_max_attempts");
            policy.Property(p => p.InitialDelay).HasColumnName("retry_initial_delay");
            policy.Property(p => p.MaxDelay).HasColumnName("retry_max_delay");
        });

        builder.Property(delivery => delivery.Status).HasColumnName("status");
        builder.Property(delivery => delivery.AttemptCount).HasColumnName("attempt_count");
        builder.Property(delivery => delivery.NextAttemptAtUtc).HasColumnName("next_attempt_at_utc");
        builder.Property(delivery => delivery.CurrentAttemptStartedAtUtc).HasColumnName("current_attempt_started_at_utc");
        builder.Property(delivery => delivery.LastFailureReason).HasColumnName("last_failure_reason").HasMaxLength(WebhookDelivery.MaxFailureReasonLength);
        builder.Property(delivery => delivery.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(delivery => delivery.SucceededAtUtc).HasColumnName("succeeded_at_utc");
        builder.Property(delivery => delivery.DeadLetteredAtUtc).HasColumnName("dead_lettered_at_utc");
        builder.Ignore(delivery => delivery.IsAttemptInProgress);
        builder.Ignore(delivery => delivery.IsTerminal);

        builder.HasIndex(delivery => new { delivery.EndpointId, delivery.EventId })
            .IsUnique()
            .HasFilter($"status <> {deadLetter}")
            .HasDatabaseName(LiveDeliveryIndex);
        // Historique d'un endpoint ; file du job (dues, bloquées) ; purge des livraisons terminées.
        builder.HasIndex(delivery => new { delivery.TenantId, delivery.EndpointId, delivery.CreatedAtUtc })
            .HasDatabaseName("ix_webhook_deliveries_tenant_id_endpoint_id_created_at_utc");
        builder.HasIndex(delivery => delivery.NextAttemptAtUtc)
            .HasFilter("next_attempt_at_utc IS NOT NULL")
            .HasDatabaseName("ix_webhook_deliveries_next_attempt_at_utc");
        builder.HasIndex(delivery => delivery.CurrentAttemptStartedAtUtc)
            .HasFilter("current_attempt_started_at_utc IS NOT NULL")
            .HasDatabaseName("ix_webhook_deliveries_current_attempt_started_at_utc");
        builder.HasIndex(delivery => new { delivery.Status, delivery.CreatedAtUtc }).HasDatabaseName("ix_webhook_deliveries_status_created_at_utc");

        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(delivery => delivery.DomainEvents);
    }
}

/// <summary>
/// Agrégat <see cref="ApiCredential"/> : <c>api_credentials</c>, scopes dans <c>api_credential_scopes</c>. Seule
/// l'empreinte de la clé est stockée ; elle est unique (c'est par elle qu'une requête est authentifiée).
/// </summary>
internal sealed class ApiCredentialConfiguration : IEntityTypeConfiguration<ApiCredential>
{
    public const string KeyHashIndex = "ix_api_credentials_key_hash";

    public void Configure(EntityTypeBuilder<ApiCredential> builder)
    {
        var revoked = IntegrationsConverters.Value(ApiCredentialStatus.Revoked);
        builder.ToTable("api_credentials", table =>
        {
            table.HasCheckConstraint("ck_api_credentials_status", IntegrationsConverters.In<ApiCredentialStatus>("status"));
            table.HasCheckConstraint("ck_api_credentials_revoked_at", $"(status = {revoked}) = (revoked_at_utc IS NOT NULL)");
        });

        builder.HasKey(credential => credential.Id);
        builder.Property(credential => credential.Id).HasColumnName("id").HasConversion(IntegrationsConverters.CredentialKey).ValueGeneratedNever();
        builder.Property(credential => credential.TenantId).HasColumnName("tenant_id").HasConversion(IntegrationsConverters.TenantKey);
        builder.Property(credential => credential.Name).HasColumnName("name").HasMaxLength(ApiCredential.MaxNameLength);
        builder.Property(credential => credential.KeyHash)
            .HasColumnName("key_hash")
            .HasConversion(hash => hash.Value, value => ApiKeyHash.Create(value))
            .HasMaxLength(ApiKeyHash.MaxLength);
        builder.Property(credential => credential.Status).HasColumnName("status");
        builder.Property(credential => credential.ExpiresAtUtc).HasColumnName("expires_at_utc");
        builder.Property(credential => credential.IssuedAtUtc).HasColumnName("issued_at_utc");
        builder.Property(credential => credential.RotatedAtUtc).HasColumnName("rotated_at_utc");
        builder.Property(credential => credential.RevokedAtUtc).HasColumnName("revoked_at_utc");
        builder.Property(credential => credential.LastUsedAtUtc).HasColumnName("last_used_at_utc");

        builder.OwnsMany(credential => credential.Scopes, scope =>
        {
            scope.ToTable("api_credential_scopes");
            scope.WithOwner().HasForeignKey(IntegrationsConverters.OwnerProperty);
            scope.Property<CredentialId>(IntegrationsConverters.OwnerProperty)
                .HasColumnName("credential_id")
                .HasConversion(IntegrationsConverters.CredentialKey);
            scope.Property(s => s.Value).HasColumnName("scope").HasMaxLength(ApiScope.MaxLength);
            scope.HasKey(IntegrationsConverters.OwnerProperty, nameof(ApiScope.Value));
        });
        builder.Navigation(credential => credential.Scopes).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(credential => credential.KeyHash).IsUnique().HasDatabaseName(KeyHashIndex);
        builder.HasIndex(credential => credential.TenantId).HasDatabaseName("ix_api_credentials_tenant_id");

        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(credential => credential.DomainEvents);
    }
}

/// <summary>Coffre des secrets de signature : <c>webhook_secrets</c>, valeurs CHIFFRÉES.</summary>
internal sealed class WebhookSecretConfiguration : IEntityTypeConfiguration<WebhookSecretRecord>
{
    public void Configure(EntityTypeBuilder<WebhookSecretRecord> builder)
    {
        builder.ToTable("webhook_secrets");
        builder.HasKey(secret => secret.Reference);
        builder.Property(secret => secret.Reference).HasColumnName("reference").HasMaxLength(SecretReference.MaxLength);
        builder.Property(secret => secret.TenantId).HasColumnName("tenant_id").HasConversion(IntegrationsConverters.TenantKey);
        builder.Property(secret => secret.CipherText).HasColumnName("cipher_text").HasMaxLength(512);
        builder.Property(secret => secret.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.HasIndex(secret => secret.TenantId).HasDatabaseName("ix_webhook_secrets_tenant_id");
    }
}
