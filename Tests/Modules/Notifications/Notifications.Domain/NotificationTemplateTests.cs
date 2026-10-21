using Notifications.Domain;

namespace Notifications.Domain;

public class NotificationTemplateTests
{
    private static readonly TemplateKey Key = TemplateKey.Create("reservation.confirmed");
    private static readonly NotificationLanguage French = NotificationLanguage.Create("fr");

    private readonly FakeClock _clock = new();
    private readonly TenantId _tenantId = TenantId.New();

    private NotificationTemplate Draft(
        NotificationChannel channel = NotificationChannel.Email,
        string? subject = "Your reservation",
        string? body = "Hello {{customer.firstName}}",
        bool isKeyUnique = true) =>
        NotificationTemplate.Create(_tenantId, Key, channel, French, subject, body, isKeyUnique, _clock);

    private NotificationTemplate PlatformDraft(bool isKeyUnique = true) =>
        NotificationTemplate.CreatePlatformTemplate(
            Key, NotificationChannel.Email, French, "Your reservation", "Hello {{customer.firstName}}", isKeyUnique, _clock);

    private NotificationTemplate Published()
    {
        var template = Draft();
        template.Publish(_clock);
        template.ClearDomainEvents();
        return template;
    }

    private NotificationTemplate Archived()
    {
        var template = Published();
        template.Archive(_clock);
        template.ClearDomainEvents();
        return template;
    }

    [Fact]
    public void Create_builds_version_one_as_a_draft()
    {
        var template = Draft(subject: "  Your reservation ");

        Assert.NotEqual(default, template.Id);
        Assert.Equal(_tenantId, template.TenantId);
        Assert.False(template.IsPlatformLevel);
        Assert.Equal(Key, template.Key);
        Assert.Equal(NotificationChannel.Email, template.Channel);
        Assert.Equal(French, template.Language);
        Assert.Equal("Your reservation", template.Subject);
        Assert.Equal("Hello {{customer.firstName}}", template.Body);
        Assert.Equal(1, template.Version);
        Assert.Equal(TemplateStatus.Draft, template.Status);
        Assert.Equal(_clock.UtcNow, template.CreatedAtUtc);
        Assert.Null(template.PublishedAtUtc);
        DomainAssert.NoEvents(template);
    }

    [Fact]
    public void Create_rejects_a_key_already_used_for_the_channel_and_language() =>
        DomainAssert.Throws(TemplateErrors.KeyAlreadyUsed, () => Draft(isKeyUnique: false));

    // ---- Modèles au niveau de la plateforme

    [Fact]
    public void CreatePlatformTemplate_builds_version_one_as_a_draft_without_tenant()
    {
        var template = PlatformDraft();

        Assert.Null(template.TenantId);
        Assert.True(template.IsPlatformLevel);
        Assert.Equal(Key, template.Key);
        Assert.Equal(NotificationChannel.Email, template.Channel);
        Assert.Equal(French, template.Language);
        Assert.Equal(1, template.Version);
        Assert.Equal(TemplateStatus.Draft, template.Status);
        DomainAssert.NoEvents(template);
    }

    [Fact]
    public void CreatePlatformTemplate_applies_the_same_rules_as_a_tenant_template()
    {
        DomainAssert.Throws(TemplateErrors.KeyAlreadyUsed, () => PlatformDraft(isKeyUnique: false));
        DomainAssert.Throws(
            TemplateErrors.SubjectRequired,
            () => NotificationTemplate.CreatePlatformTemplate(Key, NotificationChannel.Email, French, null, "b", true, _clock));
        DomainAssert.Throws(
            TemplateErrors.BodyEmpty,
            () => NotificationTemplate.CreatePlatformTemplate(Key, NotificationChannel.Sms, French, null, " ", true, _clock));
        DomainAssert.Throws(
            TemplateErrors.ChannelInvalid,
            () => NotificationTemplate.CreatePlatformTemplate(Key, (NotificationChannel)42, French, null, "b", true, _clock));
    }

    [Fact]
    public void A_platform_template_follows_the_same_lifecycle_and_its_events_carry_no_tenant()
    {
        var v1 = PlatformDraft();
        v1.Publish(_clock);
        var v2 = v1.CreateNextVersion(true, _clock);
        v1.Archive(_clock);

        Assert.Null(DomainAssert.Raised<TemplatePublished>(v1).TenantId);
        Assert.Null(DomainAssert.Raised<TemplateArchived>(v1).TenantId);
        Assert.True(v2.IsPlatformLevel);
        Assert.Equal(2, v2.Version);
        Assert.Equal(French, v2.Language);
    }

    [Fact]
    public void A_tenant_call_cannot_silently_become_platform_level()
    {
        var tenantParameter = typeof(NotificationTemplate).GetMethod(nameof(NotificationTemplate.Create))!.GetParameters()[0];

        Assert.Equal(typeof(TenantId), tenantParameter.ParameterType);
        Assert.DoesNotContain(
            typeof(NotificationTemplate).GetMethod(nameof(NotificationTemplate.CreatePlatformTemplate))!.GetParameters(),
            parameter => parameter.ParameterType == typeof(TenantId) || parameter.ParameterType == typeof(TenantId?));
    }

    [Fact]
    public void Templates_of_different_languages_share_a_key_and_a_channel()
    {
        var english = NotificationTemplate.Create(
            _tenantId, Key, NotificationChannel.Email, NotificationLanguage.Create("en"), "Your booking", "Hello", true, _clock);

        Assert.Equal(Draft().Key, english.Key);
        Assert.Equal("en", english.Language.Value);
        Assert.NotEqual(Draft().Language, english.Language);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_a_subject_for_email(string? subject) =>
        DomainAssert.Throws(TemplateErrors.SubjectRequired, () => Draft(subject: subject));

    [Fact]
    public void Create_bounds_the_subject_length()
    {
        Assert.Equal(
            NotificationTemplate.MaxSubjectLength,
            Draft(subject: new string('s', NotificationTemplate.MaxSubjectLength)).Subject!.Length);
        DomainAssert.Throws(
            TemplateErrors.SubjectTooLong,
            () => Draft(subject: new string('s', NotificationTemplate.MaxSubjectLength + 1)));
    }

    [Theory]
    [InlineData(NotificationChannel.Sms)]
    [InlineData(NotificationChannel.WhatsApp)]
    public void Create_forbids_a_subject_on_sms_and_whatsapp(NotificationChannel channel)
    {
        DomainAssert.Throws(TemplateErrors.SubjectNotSupported, () => Draft(channel, subject: "Hello"));

        Assert.Null(Draft(channel, subject: null).Subject);
        Assert.Null(Draft(channel, subject: "  ").Subject);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_a_body(string? body) =>
        DomainAssert.Throws(TemplateErrors.BodyEmpty, () => Draft(body: body));

    [Fact]
    public void Create_bounds_the_body_length()
    {
        Assert.Equal(
            NotificationTemplate.MaxBodyLength,
            Draft(body: new string('b', NotificationTemplate.MaxBodyLength)).Body.Length);
        DomainAssert.Throws(TemplateErrors.BodyTooLong, () => Draft(body: new string('b', NotificationTemplate.MaxBodyLength + 1)));
    }

    [Fact]
    public void Create_rejects_an_unknown_channel() =>
        DomainAssert.Throws(TemplateErrors.ChannelInvalid, () => Draft((NotificationChannel)42));

    [Fact]
    public void Create_rejects_a_default_tenant() =>
        DomainAssert.Throws(
            CommonErrors.EmptyId,
            () => NotificationTemplate.Create(default, Key, NotificationChannel.Email, French, "s", "b", true, _clock));

    [Fact]
    public void UpdateContent_changes_the_content_of_a_draft()
    {
        var template = Draft();
        _clock.Advance(TimeSpan.FromMinutes(5));

        template.UpdateContent("New subject", "New body", _clock);

        Assert.Equal("New subject", template.Subject);
        Assert.Equal("New body", template.Body);
        Assert.Equal(1, template.Version);
        Assert.Equal(TemplateStatus.Draft, template.Status);
        Assert.Equal(_clock.UtcNow, template.UpdatedAtUtc);
        DomainAssert.NoEvents(template);
    }

    [Fact]
    public void UpdateContent_with_the_current_content_is_a_silent_no_op()
    {
        var template = Draft();
        var updatedAt = template.UpdatedAtUtc;
        _clock.Advance(TimeSpan.FromMinutes(5));

        template.UpdateContent("  Your reservation ", "Hello {{customer.firstName}}", _clock);

        Assert.Equal("Your reservation", template.Subject);
        Assert.Equal(updatedAt, template.UpdatedAtUtc);
        DomainAssert.NoEvents(template);
    }

    [Fact]
    public void UpdateContent_applies_the_same_content_rules_and_keeps_the_previous_content()
    {
        var email = Draft();
        DomainAssert.Throws(TemplateErrors.SubjectRequired, () => email.UpdateContent(null, "New body", _clock));
        DomainAssert.Throws(TemplateErrors.BodyEmpty, () => email.UpdateContent("New subject", " ", _clock));
        Assert.Equal("Your reservation", email.Subject);
        Assert.Equal("Hello {{customer.firstName}}", email.Body);

        var sms = Draft(NotificationChannel.Sms, subject: null);
        DomainAssert.Throws(TemplateErrors.SubjectNotSupported, () => sms.UpdateContent("Subject", "New body", _clock));
    }

    [Fact]
    public void Publish_freezes_the_draft_and_raises_TemplatePublished()
    {
        var template = Draft();
        _clock.Advance(TimeSpan.FromMinutes(5));

        template.Publish(_clock);

        Assert.Equal(TemplateStatus.Published, template.Status);
        Assert.Equal(_clock.UtcNow, template.PublishedAtUtc);
        var published = DomainAssert.Raised<TemplatePublished>(template);
        Assert.Equal(_tenantId, published.TenantId);
        Assert.Equal(template.Id, published.TemplateId);
        Assert.Equal("reservation.confirmed", published.Key);
        Assert.Equal(NotificationChannel.Email, published.Channel);
        Assert.Equal("fr", published.Language);
        Assert.Equal(1, published.Version);
    }

    [Fact]
    public void A_published_template_is_immutable()
    {
        var template = Published();

        DomainAssert.Throws(TemplateErrors.PublishedImmutable, () => template.UpdateContent("Changed", "Changed", _clock));
        DomainAssert.Throws(TemplateErrors.PublishedImmutable, () => template.Publish(_clock));

        Assert.Equal("Your reservation", template.Subject);
        Assert.Equal("Hello {{customer.firstName}}", template.Body);
        Assert.Equal(TemplateStatus.Published, template.Status);
        DomainAssert.NoEvents(template);
    }

    [Fact]
    public void Versioning_a_published_template_is_changed_through_a_new_draft_version()
    {
        var v1 = Published();
        _clock.Advance(TimeSpan.FromDays(1));

        var v2 = v1.CreateNextVersion(isNextVersionUnique: true, _clock);
        v2.UpdateContent("Updated subject", "Updated body", _clock);
        v2.Publish(_clock);

        Assert.NotEqual(v1.Id, v2.Id);
        Assert.Equal(2, v2.Version);
        Assert.Equal(v1.TenantId, v2.TenantId);
        Assert.Equal(v1.Key, v2.Key);
        Assert.Equal(v1.Channel, v2.Channel);
        Assert.Equal(v1.Language, v2.Language);
        Assert.Equal(TemplateStatus.Published, v2.Status);
        Assert.Equal("Updated subject", v2.Subject);
        Assert.Equal("Updated body", v2.Body);
        Assert.Equal(2, DomainAssert.Raised<TemplatePublished>(v2).Version);

        // La version 1 reste intacte.
        Assert.Equal(1, v1.Version);
        Assert.Equal(TemplateStatus.Published, v1.Status);
        Assert.Equal("Your reservation", v1.Subject);
        Assert.Equal("Hello {{customer.firstName}}", v1.Body);
        DomainAssert.NoEvents(v1);
    }

    [Fact]
    public void CreateNextVersion_starts_as_a_draft_copy_of_the_source()
    {
        var v1 = Published();
        _clock.Advance(TimeSpan.FromDays(1));

        var v2 = v1.CreateNextVersion(true, _clock);

        Assert.Equal(TemplateStatus.Draft, v2.Status);
        Assert.Equal(v1.Subject, v2.Subject);
        Assert.Equal(v1.Body, v2.Body);
        Assert.Equal(_clock.UtcNow, v2.CreatedAtUtc);
        Assert.Null(v2.PublishedAtUtc);
        DomainAssert.NoEvents(v2);
    }

    [Fact]
    public void CreateNextVersion_increments_from_the_source_version()
    {
        var v2 = Published().CreateNextVersion(true, _clock);
        v2.Publish(_clock);

        var v3 = v2.CreateNextVersion(true, _clock);

        Assert.Equal(3, v3.Version);
    }

    [Fact]
    public void CreateNextVersion_is_rejected_from_a_draft() =>
        DomainAssert.Throws(TemplateErrors.DraftCannotBeVersioned, () => Draft().CreateNextVersion(true, _clock));

    [Fact]
    public void CreateNextVersion_is_rejected_when_that_version_already_exists() =>
        DomainAssert.Throws(TemplateErrors.VersionAlreadyExists, () => Published().CreateNextVersion(false, _clock));

    [Fact]
    public void CreateNextVersion_is_allowed_from_an_archived_version()
    {
        var next = Archived().CreateNextVersion(true, _clock);

        Assert.Equal(2, next.Version);
        Assert.Equal(TemplateStatus.Draft, next.Status);
    }

    [Fact]
    public void Archive_retires_a_published_template_and_raises_TemplateArchived()
    {
        var template = Published();
        _clock.Advance(TimeSpan.FromDays(3));

        template.Archive(_clock);

        Assert.Equal(TemplateStatus.Archived, template.Status);
        Assert.Equal(_clock.UtcNow, template.ArchivedAtUtc);
        var archived = DomainAssert.Raised<TemplateArchived>(template);
        Assert.Equal(template.Id, archived.TemplateId);
        Assert.Equal(_tenantId, archived.TenantId);
        Assert.Equal("fr", archived.Language);
        Assert.Equal(1, archived.Version);
    }

    [Fact]
    public void Archive_can_discard_a_draft()
    {
        var template = Draft();

        template.Archive(_clock);

        Assert.Equal(TemplateStatus.Archived, template.Status);
        DomainAssert.NotRaised<TemplatePublished>(template);
    }

    [Fact]
    public void Archived_is_terminal()
    {
        var template = Archived();

        DomainAssert.Throws(TemplateErrors.Archived, () => template.UpdateContent("s", "b", _clock));
        DomainAssert.Throws(TemplateErrors.Archived, () => template.Publish(_clock));
        DomainAssert.Throws(TemplateErrors.Archived, () => template.Archive(_clock));
        Assert.Equal(TemplateStatus.Archived, template.Status);
        DomainAssert.NoEvents(template);
    }

    [Fact]
    public void Aggregate_exposes_no_public_setter() =>
        Assert.All(
            typeof(NotificationTemplate).GetProperties(),
            property => Assert.True(property.SetMethod is null || !property.SetMethod.IsPublic, property.Name));

    [Fact]
    public void Publish_and_Archive_stamp_the_state_and_the_event_with_the_same_instant()
    {
        var ticking = new TickingClock(_clock.UtcNow);
        var template = Draft();

        template.Publish(ticking);
        var published = DomainAssert.Raised<TemplatePublished>(template);
        Assert.Equal(template.PublishedAtUtc, published.OccurredAtUtc);
        Assert.Equal(template.UpdatedAtUtc, published.OccurredAtUtc);

        template.Archive(ticking);
        var archived = DomainAssert.Raised<TemplateArchived>(template);
        Assert.Equal(template.ArchivedAtUtc, archived.OccurredAtUtc);
        Assert.Equal(template.UpdatedAtUtc, archived.OccurredAtUtc);
    }

    // ---- Étape 5 : titre, type, variables, activation

    [Theory]
    [InlineData(NotificationChannel.Push)]
    [InlineData(NotificationChannel.InApp)]
    public void Push_and_in_app_templates_take_an_optional_title(NotificationChannel channel)
    {
        Assert.Equal("Table ready", Draft(channel, subject: " Table ready ").Subject);
        Assert.Null(Draft(channel, subject: " ").Subject);
        DomainAssert.Throws(TemplateErrors.SubjectTooLong, () => Draft(channel, subject: new string('t', NotificationTemplate.MaxSubjectLength + 1)));
    }

    [Fact]
    public void A_template_exists_only_for_a_known_notification_type()
    {
        DomainAssert.Throws(
            TemplateErrors.TypeUnknown,
            () => NotificationTemplate.Create(_tenantId, TemplateKey.Create("reservation.unknown"), NotificationChannel.Email, French, "s", "b", true, _clock));

        Assert.Equal(NotificationTypes.ReservationConfirmed, Draft().Type);
    }

    [Fact]
    public void A_template_only_uses_the_variables_of_its_type()
    {
        var template = Draft(body: "Bonjour {{ customer.firstName }}, table pour {{reservation.guestCount}} le {{reservation.date}} à {{reservation.time}} chez {{restaurant.name}}.");

        Assert.Equal(
            new[] { "customer.firstName", "reservation.guestCount", "reservation.date", "reservation.time", "restaurant.name" },
            template.Variables.ToArray());
        DomainAssert.Throws(TemplateErrors.VariableNotAllowed, () => Draft(body: "Total : {{order.total}}"));
        DomainAssert.Throws(TemplateErrors.VariableNotAllowed, () => Draft(subject: "Pour {{customer.email}}"));
        DomainAssert.Throws(TemplateErrors.VariableNotAllowed, () => template.UpdateContent("s", "{{otp.code}}", _clock));
        Assert.Contains("reservation.date", template.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Bonjour {{ customer.firstName | upper }}")]
    [InlineData("Bonjour {{customer.firstName}")]
    [InlineData("Bonjour {{ }}")]
    [InlineData("{{#if customer.firstName}}Bonjour{{/if}}")]
    [InlineData("Code {{customer..firstName}}")]
    public void A_template_cannot_hold_expressions_or_broken_variables(string body) =>
        DomainAssert.Throws(TemplateErrors.VariableSyntaxInvalid, () => Draft(body: body));

    [Fact]
    public void A_marketing_email_carries_an_unsubscribe_link()
    {
        var key = NotificationTypes.NewMenu.Key;

        DomainAssert.Throws(
            TemplateErrors.UnsubscribeLinkRequired,
            () => NotificationTemplate.Create(_tenantId, key, NotificationChannel.Email, French, "Nouvelle carte", "Découvrez {{campaign.url}}", true, _clock));
        var email = NotificationTemplate.Create(
            _tenantId, key, NotificationChannel.Email, French, "Nouvelle carte", "Découvrez {{campaign.url}} - {{unsubscribe.url}}", true, _clock);
        // Sur SMS, la désinscription passe par le mot-clé STOP du prestataire, pas par un lien.
        var sms = NotificationTemplate.Create(_tenantId, key, NotificationChannel.Sms, French, null, "Nouvelle carte : {{campaign.url}}", true, _clock);

        Assert.True(email.Type.IsMarketing && sms.Type.IsMarketing);
    }

    [Fact]
    public void A_template_is_enabled_at_creation_and_can_be_switched_off_and_on()
    {
        var template = Published();

        Assert.True(template.IsEnabled && template.IsUsable);
        template.Disable(_clock);
        DomainAssert.Throws(TemplateErrors.AlreadyDisabled, () => template.Disable(_clock));
        Assert.False(template.IsUsable);
        var next = template.CreateNextVersion(true, _clock);
        template.Enable(_clock);
        DomainAssert.Throws(TemplateErrors.AlreadyEnabled, () => template.Enable(_clock));

        Assert.True(template.IsUsable);
        Assert.False(next.IsEnabled);
        Assert.Equal("reservation.confirmed", DomainAssert.Raised<TemplateDisabled>(template).Key);
        Assert.Equal(1, DomainAssert.Raised<TemplateEnabled>(template).Version);
        Assert.False(Draft().IsUsable);
    }

    [Fact]
    public void An_archived_template_is_never_switched_back_on()
    {
        var template = Archived();

        DomainAssert.Throws(TemplateErrors.Archived, () => template.Enable(_clock));
        DomainAssert.Throws(TemplateErrors.Archived, () => template.Disable(_clock));
    }

    /// <summary>Avance à chaque lecture : prouve qu'une opération ne lit l'horloge qu'une fois.</summary>
    private sealed class TickingClock(DateTimeOffset start) : IClock
    {
        private DateTimeOffset _now = start;

        public DateTimeOffset UtcNow => _now = _now.AddSeconds(1);
    }
}
