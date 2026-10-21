using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Notifications.Domain;

namespace Notifications.Infrastructure.Persistence.Configurations;

/// <summary>
/// Conversions entre les types du domaine et les colonnes. Toute valeur relue repasse par la fabrique du domaine
/// (<c>Create</c>, <c>From</c>, <c>Of</c>, <c>Parse</c>) : une ligne modifiée à la main qui viole une règle est refusée
/// au chargement au lieu de produire un agrégat invalide.
/// </summary>
internal static class NotificationsConverters
{
    public static readonly ValueConverter<TenantId, Guid> TenantKey = new(id => id.Value, value => TenantId.From(value));
    public static readonly ValueConverter<LocationId, Guid> LocationKey = new(id => id.Value, value => LocationId.From(value));
    public static readonly ValueConverter<UserId, Guid> UserKey = new(id => id.Value, value => UserId.From(value));
    public static readonly ValueConverter<NotificationProviderId, Guid> ProviderKey = new(id => id.Value, value => NotificationProviderId.From(value));
    public static readonly ValueConverter<RestaurantNotificationConfigurationId, Guid> ConfigurationKey =
        new(id => id.Value, value => RestaurantNotificationConfigurationId.From(value));
    public static readonly ValueConverter<NotificationChannelAssignmentId, Guid> AssignmentKey =
        new(id => id.Value, value => NotificationChannelAssignmentId.From(value));
    public static readonly ValueConverter<TemplateId, Guid> TemplateIdKey = new(id => id.Value, value => TemplateId.From(value));
    public static readonly ValueConverter<NotificationPreferenceId, Guid> PreferenceKey = new(id => id.Value, value => NotificationPreferenceId.From(value));
    public static readonly ValueConverter<NotificationId, Guid> NotificationKey = new(id => id.Value, value => NotificationId.From(value));
    public static readonly ValueConverter<NotificationDeliveryId, Guid> DeliveryKey = new(id => id.Value, value => NotificationDeliveryId.From(value));
    public static readonly ValueConverter<DeviceRegistrationId, Guid> DeviceKey = new(id => id.Value, value => DeviceRegistrationId.From(value));
    public static readonly ValueConverter<OtpChallengeId, Guid> OtpKey = new(id => id.Value, value => OtpChallengeId.From(value));

    public static readonly ValueConverter<NotificationProviderCode, string> ProviderCode =
        new(code => code.Value, value => NotificationProviderCode.Create(value));
    public static readonly ValueConverter<TemplateKey, string> TypeKey = new(key => key.Value, value => TemplateKey.Create(value));
    public static readonly ValueConverter<NotificationLanguage, string> Language = new(language => language.Value, value => NotificationLanguage.Create(value));
    public static readonly ValueConverter<PreferenceSource, string> Source = new(source => source.Value, value => PreferenceSource.Create(value));
    public static readonly ValueConverter<OtpPurpose, string> Purpose = new(purpose => purpose.Value, value => OtpPurpose.Create(value));
    public static readonly ValueConverter<OtpHash, string> Hash = new(hash => hash.Value, value => OtpHash.Create(value));

    public static readonly ValueConverter<List<string>, string> Strings =
        new(values => JsonSerializer.Serialize(values, (JsonSerializerOptions?)null), json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>());

    public static readonly ValueComparer<List<string>> StringsComparer = new(
        (left, right) => left!.SequenceEqual(right!), values => values.Aggregate(0, (hash, value) => HashCode.Combine(hash, value)), values => values.ToList());

    /// <summary>Null n'est jamais converti : la colonne reste NULL.</summary>
    public static readonly ValueConverter<NotificationCredentialReference?, string> SecretReference = new(
        reference => reference!.Value, value => NotificationCredentialReference.Parse(value));

    public static readonly ValueConverter<ProviderMessageId?, string> MessageId = new(
        id => id!.Value, value => ProviderMessageId.Create(value));

    /// <summary>
    /// Qui reçoit : « customer:{id} » ou « user:{id} » (id sans tirets). Une colonne texte indexable, relue par
    /// <see cref="RecipientId.Of"/>.
    /// </summary>
    public static readonly ValueConverter<RecipientId, string> Recipient = new(id => WriteRecipient(id), value => ReadRecipient(value));

    /// <inheritdoc cref="Recipient"/>
    public static readonly ValueConverter<RecipientId?, string> OptionalRecipient = new(id => WriteRecipient(id!), value => ReadRecipient(value));

    /// <summary>
    /// Échec d'une tentative, en jsonb relu par <see cref="NotificationFailure.Create"/>. Noms des champs FIXÉS : renommer
    /// une propriété C# ne rend pas illisibles les lignes déjà écrites. Le message du prestataire peut y figurer jusqu'à
    /// l'expurgation (puis il est retiré).
    /// </summary>
    public static readonly ValueConverter<NotificationFailure?, string> Failure = new(
        failure => JsonSerializer.Serialize(new FailureRow(failure!.Code, failure.Kind, failure.Reason), (JsonSerializerOptions?)null),
        json => ReadFailure(json));

    /// <summary>Capacités d'un prestataire en une valeur jsonb (décision commune à Payments : pas de table à part).</summary>
    public static readonly ValueConverter<NotificationProviderCapability, string> Capability = new(
        capability => WriteCapability(capability), json => ReadCapability(json));

    public const string RecipientPattern = "^(customer|user):[0-9a-f]{32}$";

    internal static string WriteRecipient(RecipientId id) =>
        $"{id.Kind.ToString().ToLowerInvariant()}:{id.Id:N}";

    internal static RecipientId ReadRecipient(string value)
    {
        var separator = value.IndexOf(':', StringComparison.Ordinal);
        var kind = value[..separator] switch
        {
            "customer" => RecipientKind.Customer,
            "user" => RecipientKind.User,
            _ => throw new InvalidOperationException("Unknown recipient kind."),
        };
        return RecipientId.Of(kind, Guid.ParseExact(value[(separator + 1)..], "N"));
    }

    internal static string WriteCapability(NotificationProviderCapability capability) =>
        JsonSerializer.Serialize(new CapabilityRow(
            capability.Channel,
            capability.Features.ToList(),
            capability.SupportedCountries.Select(country => country.Value).ToList(),
            capability.MaxContentLength));

    internal static NotificationProviderCapability ReadCapability(string json)
    {
        var row = JsonSerializer.Deserialize<CapabilityRow>(json) ?? throw new InvalidOperationException("Empty provider capability.");
        return NotificationProviderCapability.Create(
            row.Channel,
            row.Features ?? new List<NotificationFeature>(),
            (row.Countries ?? new List<string>()).Select(CountryCode.Create),
            row.MaxContentLength);
    }

    private static NotificationFailure ReadFailure(string json)
    {
        var row = JsonSerializer.Deserialize<FailureRow>(json) ?? throw new InvalidOperationException("Empty failure.");
        return NotificationFailure.Create(row.Code, row.Kind, row.Reason);
    }

    internal sealed record CapabilityRow(
        [property: JsonPropertyName("channel")] NotificationChannel Channel,
        [property: JsonPropertyName("features")] List<NotificationFeature>? Features,
        [property: JsonPropertyName("countries")] List<string>? Countries,
        [property: JsonPropertyName("maxContentLength")] int? MaxContentLength);

    internal sealed record FailureRow(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("kind")] NotificationFailureKind Kind,
        [property: JsonPropertyName("reason")] string? Reason);
}

/// <summary>
/// Contraintes CHECK du schéma : garde-fous en base DERRIÈRE les règles du domaine, pour une ligne écrite hors de
/// l'application (script, correction manuelle) ou par un bug de mapping.
/// </summary>
internal static class Checks
{
    /// <summary>Une colonne d'enum ne prend que les valeurs déclarées (liste GÉNÉRÉE depuis l'enum).</summary>
    public static string In<TEnum>(string column)
        where TEnum : struct, Enum =>
        column + " IN (" + string.Join(", ", Values<TEnum>()) + ")";

    public static string NullOrIn<TEnum>(string column)
        where TEnum : struct, Enum =>
        column + " IS NULL OR " + In<TEnum>(column);

    public static IEnumerable<string> Values<TEnum>()
        where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().Select(value => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
}

/// <summary>
/// Colonne <c>tenant_id</c> des tables ENFANTS : propriété cachée, recopiée de la racine par l'unit of work. Facultative
/// pour les enfants d'une racine qui peut appartenir à la plateforme (tentatives d'une notification de la plateforme).
/// </summary>
internal static class ChildTenant
{
    public const string Property = "TenantId";

    public static void Map<T>(EntityTypeBuilder<T> builder)
        where T : class =>
        builder.Property<TenantId>(Property).HasColumnName("tenant_id").HasConversion(NotificationsConverters.TenantKey);

    public static void MapOptional<T>(EntityTypeBuilder<T> builder)
        where T : class =>
        builder.Property<TenantId?>(Property).HasColumnName("tenant_id").HasConversion(NotificationsConverters.TenantKey);
}
