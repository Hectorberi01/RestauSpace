using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Customers.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Customers.Infrastructure.Persistence.Configurations;

/// <summary>
/// Conversions entre les types du domaine et les colonnes. Toute valeur relue repasse par la fabrique du domaine : une
/// ligne modifiée à la main qui viole une règle est refusée au chargement.
/// </summary>
internal static class CustomersConverters
{
    public static readonly ValueConverter<TenantId, Guid> TenantKey = new(id => id.Value, value => TenantId.From(value));
    public static readonly ValueConverter<CustomerId, Guid> CustomerKey = new(id => id.Value, value => CustomerId.From(value));
    public static readonly ValueConverter<ConsentId, Guid> ConsentKey = new(id => id.Value, value => ConsentId.From(value));

    /// <summary>Null (pas d'email) n'est jamais converti : la colonne reste NULL.</summary>
    public static readonly ValueConverter<Email?, string> EmailAddress = new(email => email!.Value, value => Email.Create(value));

    public static readonly ValueConverter<PhoneNumber?, string> Phone = new(phone => phone!.Value, value => PhoneNumber.Create(value));

    public static readonly ValueConverter<ContactRequirementPolicy, int> ContactPolicy =
        new(policy => (int)policy.Requirement, value => ContactRequirementPolicy.Of((ContactRequirement)value));

    public static readonly ValueConverter<ConsentSource, string> Source = new(source => source.Value, value => ConsentSource.Create(value));

    public static readonly ValueConverter<ConsentSource?, string> OptionalSource =
        new(source => source!.Value, value => ConsentSource.Create(value));

    /// <summary>
    /// Préférences : <c>{"language":"fr-FR","marketingChannels":[1,2]}</c>, relues par <see cref="CustomerPreference.Create"/>
    /// (langue et canaux revérifiés). Une valeur jsonb : remplacée en bloc, jamais interrogée. Null n'est jamais converti.
    /// </summary>
    public static readonly ValueConverter<CustomerPreference?, string> Preferences = new(
        preferences => WritePreferences(preferences!), json => ReadPreferences(json));

    private static string WritePreferences(CustomerPreference preferences) =>
        JsonSerializer.Serialize(new PreferencesRow(preferences.Language, preferences.MarketingChannels.ToList()));

    private static CustomerPreference ReadPreferences(string json)
    {
        var row = JsonSerializer.Deserialize<PreferencesRow>(json) ?? throw new InvalidOperationException("Empty customer preferences.");
        return CustomerPreference.Create(row.Language, row.MarketingChannels);
    }

    internal sealed record PreferencesRow(
        [property: JsonPropertyName("language")] string Language,
        [property: JsonPropertyName("marketingChannels")] List<CommunicationChannel>? MarketingChannels);

    /// <summary>
    /// CHECK d'une colonne d'enum, GÉNÉRÉ depuis l'enum : ajouter une valeur change le modèle, donc la migration.
    /// </summary>
    public static string In<TEnum>(string column)
        where TEnum : struct, Enum =>
        column + " IN (" + string.Join(
            ", ",
            Enum.GetValues<TEnum>().Select(value => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture))) + ")";
}

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public const string OwnerProperty = "CustomerId";
    public const string NormalizedNameProperty = "NormalizedName";

    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers", table =>
        {
            table.HasCheckConstraint("ck_customers_status", CustomersConverters.In<CustomerStatus>("status"));
            table.HasCheckConstraint("ck_customers_contact_requirement", CustomersConverters.In<ContactRequirement>("contact_requirement"));
            // Garde-fou du droit à l'effacement : une fiche anonymisée ne garde AUCUNE donnée personnelle.
            table.HasCheckConstraint(
                "ck_customers_anonymized_has_no_personal_data",
                "status <> " + (int)CustomerStatus.Anonymized
                + " OR (email IS NULL AND phone IS NULL AND preferences IS NULL AND anonymized_at_utc IS NOT NULL)");
            table.HasCheckConstraint("ck_customers_preferences", "preferences IS NULL OR jsonb_typeof(preferences) = 'object'");
            // Un doublon fusionné ne garde que le lien vers la fiche qui l'a absorbé.
            table.HasCheckConstraint(
                "ck_customers_merged_has_no_personal_data",
                "(status = " + (int)CustomerStatus.Merged + ") = (merged_into_id IS NOT NULL)"
                + " AND (status <> " + (int)CustomerStatus.Merged + " OR (email IS NULL AND phone IS NULL AND preferences IS NULL AND merged_at_utc IS NOT NULL))");
        });

        builder.HasKey(customer => customer.Id);
        builder.Property(customer => customer.Id).HasColumnName("id").HasConversion(CustomersConverters.CustomerKey).ValueGeneratedNever();
        builder.Property(customer => customer.TenantId).HasColumnName("tenant_id").HasConversion(CustomersConverters.TenantKey);
        builder.Property(customer => customer.Name).HasColumnName("name").HasMaxLength(Customer.NameMaxLength);
        // Recherche par nom sans tenir compte de la casse, et tri : une copie en majuscules.
        builder.Property<string>(NormalizedNameProperty)
            .HasColumnName("normalized_name")
            .HasMaxLength(Customer.NameMaxLength)
            .HasComputedColumnSql("upper(name)", stored: true);
        builder.HasIndex(nameof(Customer.TenantId), NormalizedNameProperty).HasDatabaseName("ix_customers_tenant_id_normalized_name");

        builder.Property(customer => customer.Email)
            .HasColumnName("email")
            .HasConversion(CustomersConverters.EmailAddress)
            .HasMaxLength(Email.MaxLength);
        builder.Property(customer => customer.Phone)
            .HasColumnName("phone")
            .HasConversion(CustomersConverters.Phone)
            .HasMaxLength(16);
        // Retrouver une fiche par son email ou son téléphone exacts. PAS uniques : deux fiches peuvent partager un contact
        // (choix du domaine), la recherche sert à éviter les doublons.
        builder.HasIndex(customer => new { customer.TenantId, customer.Email })
            .HasFilter("email IS NOT NULL")
            .HasDatabaseName("ix_customers_tenant_id_email");
        builder.HasIndex(customer => new { customer.TenantId, customer.Phone })
            .HasFilter("phone IS NOT NULL")
            .HasDatabaseName("ix_customers_tenant_id_phone");

        builder.Property(customer => customer.ContactPolicy).HasColumnName("contact_requirement").HasConversion(CustomersConverters.ContactPolicy);
        builder.Property(customer => customer.Status).HasColumnName("status");
        builder.Property(customer => customer.Preferences)
            .HasColumnName("preferences")
            .HasColumnType("jsonb")
            .HasConversion(CustomersConverters.Preferences);
        builder.Property(customer => customer.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(customer => customer.AnonymizedAtUtc).HasColumnName("anonymized_at_utc");
        builder.Property(customer => customer.MergedIntoId).HasColumnName("merged_into_id").HasConversion(CustomersConverters.CustomerKey);
        builder.Property(customer => customer.MergedAtUtc).HasColumnName("merged_at_utc");

        // Concurrence optimiste sur xmin ; l'unit of work réécrit la fiche quand seuls ses tags changent.
        builder.Property<uint>("Version").IsRowVersion();

        // Un tag une fois par fiche : la clé (client, valeur) le garantit en base.
        builder.OwnsMany(customer => customer.Tags, tag =>
        {
            tag.ToTable("customer_tags");
            tag.WithOwner().HasForeignKey(OwnerProperty);
            tag.Property<CustomerId>(OwnerProperty).HasColumnName("customer_id").HasConversion(CustomersConverters.CustomerKey);
            tag.Property(t => t.Value).HasColumnName("value").HasMaxLength(CustomerTag.MaxLength);
            tag.HasKey(OwnerProperty, nameof(CustomerTag.Value));
            // Recherche par tag dans un restaurant passe par la fiche (tenant) puis cet index.
            tag.HasIndex(t => t.Value).HasDatabaseName("ix_customer_tags_value");
        });
        builder.Navigation(customer => customer.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(customer => customer.IsAnonymized);
        builder.Ignore(customer => customer.IsMerged);
        builder.Ignore(customer => customer.DomainEvents);
    }
}

internal sealed class ConsentConfiguration : IEntityTypeConfiguration<Consent>
{
    public const string OwnerProperty = "ConsentId";
    public const string CustomerChannelIndex = "ix_consents_tenant_id_customer_id_channel";

    public void Configure(EntityTypeBuilder<Consent> builder)
    {
        builder.ToTable("consents", table =>
        {
            table.HasCheckConstraint("ck_consents_channel", CustomersConverters.In<CommunicationChannel>("channel"));
            table.HasCheckConstraint("ck_consents_status", CustomersConverters.In<ConsentStatus>("status"));
            table.HasCheckConstraint(
                "ck_consents_withdrawal_reason", "withdrawal_reason IS NULL OR " + CustomersConverters.In<WithdrawalReason>("withdrawal_reason"));
            // Un retrait en cours a sa date, sa source et son motif ; un consentement accordé n'en a aucun.
            table.HasCheckConstraint(
                "ck_consents_withdrawal",
                "(status = " + (int)ConsentStatus.Granted + " AND withdrawn_at_utc IS NULL AND withdrawal_source IS NULL AND withdrawal_reason IS NULL)"
                + " OR (status = " + (int)ConsentStatus.Withdrawn
                + " AND withdrawn_at_utc IS NOT NULL AND withdrawal_source IS NOT NULL AND withdrawal_reason IS NOT NULL)");
        });

        builder.HasKey(consent => consent.Id);
        builder.Property(consent => consent.Id).HasColumnName("id").HasConversion(CustomersConverters.ConsentKey).ValueGeneratedNever();
        builder.Property(consent => consent.TenantId).HasColumnName("tenant_id").HasConversion(CustomersConverters.TenantKey);
        // Référence par id vers l'agrégat Customer, sans clé étrangère (règle des agrégats) : la preuve survit à la fiche.
        builder.Property(consent => consent.CustomerId).HasColumnName("customer_id").HasConversion(CustomersConverters.CustomerKey);
        builder.Property(consent => consent.Channel).HasColumnName("channel");
        // Un consentement par (client, canal) : garde-fou derrière IConsentUniquenessChecker (deux octrois simultanés).
        builder.HasIndex(consent => new { consent.TenantId, consent.CustomerId, consent.Channel })
            .IsUnique()
            .HasDatabaseName(CustomerChannelIndex);

        builder.Property(consent => consent.Status).HasColumnName("status");
        builder.Property(consent => consent.GrantedAtUtc).HasColumnName("granted_at_utc");
        builder.Property(consent => consent.Source).HasColumnName("source").HasConversion(CustomersConverters.Source).HasMaxLength(ConsentSource.MaxLength);
        builder.Property(consent => consent.WithdrawnAtUtc).HasColumnName("withdrawn_at_utc");
        builder.Property(consent => consent.WithdrawalSource)
            .HasColumnName("withdrawal_source")
            .HasConversion(CustomersConverters.OptionalSource)
            .HasMaxLength(ConsentSource.MaxLength);
        builder.Property(consent => consent.WithdrawalReason).HasColumnName("withdrawal_reason");

        builder.Property<uint>("Version").IsRowVersion();

        // Historique en ajout seul : une ligne par octroi et par retrait, jamais modifiée.
        builder.OwnsMany(consent => consent.History, change =>
        {
            change.ToTable("consent_changes", table =>
            {
                table.HasCheckConstraint("ck_consent_changes_status", CustomersConverters.In<ConsentStatus>("status"));
                table.HasCheckConstraint("ck_consent_changes_reason", "reason IS NULL OR " + CustomersConverters.In<WithdrawalReason>("reason"));
            });
            change.WithOwner().HasForeignKey(OwnerProperty);
            change.Property<ConsentId>(OwnerProperty).HasColumnName("consent_id").HasConversion(CustomersConverters.ConsentKey);
            // Identité croissante : l'ordre d'écriture des lignes.
            change.Property<long>("Id").HasColumnName("id").UseIdentityAlwaysColumn();
            change.HasKey("Id");
            change.Property(c => c.Status).HasColumnName("status");
            change.Property(c => c.OccurredAtUtc).HasColumnName("occurred_at_utc");
            change.Property(c => c.Source).HasColumnName("source").HasConversion(CustomersConverters.Source).HasMaxLength(ConsentSource.MaxLength);
            change.Property(c => c.Reason).HasColumnName("reason");
            change.HasIndex(OwnerProperty).HasDatabaseName("ix_consent_changes_consent_id");
        });
        builder.Navigation(consent => consent.History).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(consent => consent.IsGranted);
        builder.Ignore(consent => consent.DomainEvents);
    }
}

internal sealed class CustomerSettingsConfiguration : IEntityTypeConfiguration<CustomerSettings>
{
    public void Configure(EntityTypeBuilder<CustomerSettings> builder)
    {
        builder.ToTable("customer_settings", table =>
            table.HasCheckConstraint("ck_customer_settings_contact_requirement", CustomersConverters.In<ContactRequirement>("contact_requirement")));

        builder.HasKey(settings => settings.TenantId);
        builder.Property(settings => settings.TenantId).HasColumnName("tenant_id").HasConversion(CustomersConverters.TenantKey).ValueGeneratedNever();
        builder.Property(settings => settings.ContactRequirement).HasColumnName("contact_requirement");
        builder.Property(settings => settings.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.Property<uint>("Version").IsRowVersion();
    }
}

/// <summary>Notes de l'équipe (<c>customer_notes</c>) : données personnelles, supprimées avec la fiche anonymisée.</summary>
internal sealed class CustomerNoteConfiguration : IEntityTypeConfiguration<CustomerNote>
{
    public void Configure(EntityTypeBuilder<CustomerNote> builder)
    {
        builder.ToTable("customer_notes");
        builder.HasKey(note => note.Id);
        builder.Property(note => note.Id).HasColumnName("id").HasConversion(id => id.Value, value => CustomerNoteId.From(value)).ValueGeneratedNever();
        builder.Property(note => note.TenantId).HasColumnName("tenant_id").HasConversion(CustomersConverters.TenantKey);
        builder.Property(note => note.CustomerId).HasColumnName("customer_id").HasConversion(CustomersConverters.CustomerKey);
        builder.Property(note => note.Text).HasColumnName("text").HasMaxLength(CustomerNote.TextMaxLength);
        builder.Property(note => note.AuthorId).HasColumnName("author_id").HasConversion(id => id!.Value.Value, value => UserId.From(value));
        builder.Property(note => note.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.HasIndex(note => new { note.TenantId, note.CustomerId, note.CreatedAtUtc }).HasDatabaseName("ix_customer_notes_tenant_id_customer_id_created_at_utc");
    }
}
