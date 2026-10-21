using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Notifications.Application;
using Notifications.Application.Abstractions;
using Notifications.Application.Sending;
using Notifications.Domain;
using Notifications.Infrastructure.Persistence;
using Notifications.Infrastructure.Secrets;
using Notifications.Infrastructure.Senders;
using Notifications.Infrastructure.Webhooks;
using Notifications.Infrastructure.Workers;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Notifications.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL propre au module. Aucun autre module ne lit ni ne joint ses tables.</summary>
    public const string Schema = "notifications";

    /// <summary>
    /// Enregistre la persistance du module (<see cref="NotificationsDbContext"/>, repositories, unit of work, outbox,
    /// inbox, consommation), le coffre des identifiants (chiffré avec la clé de l'hôte, refus au démarrage si elle
    /// manque), les intégrations de prestataires (in-app, et le prestataire simulé si activé), les webhooks et le worker.
    /// L'hôte fournit <see cref="ITenantContext"/> (le tenant authentifié de la requête, ou celui du message traité),
    /// <see cref="INotificationTenants"/>, <see cref="INotificationLocations"/>, <see cref="INotificationContacts"/> et
    /// <see cref="INotificationConsents"/> ; il peut remplacer les quotas (<see cref="INotificationEntitlementChecker"/>).
    /// </summary>
    public static IServiceCollection AddNotificationsInfrastructure(
        this IServiceCollection services, string connectionString, Action<NotificationsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new NotificationsOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.TryAddSingleton(new NotificationsSettings());
        services.TryAddSingleton(new NotificationsQuotaOptions());

        // Sans hôte : un tenant d'arrière-plan, fixé par le worker (aucun par défaut : seule la plateforme est visible).
        services.TryAddScoped<BackgroundTenant>();
        services.TryAddScoped<ITenantContext>(provider => new BackgroundTenantContext(provider.GetRequiredService<BackgroundTenant>()));
        services.TryAddScoped<INotificationsCurrentUser, NoCurrentUser>();

        services.AddDbContext<NotificationsDbContext>(builder => builder.UseNotificationsNpgsql(connectionString));
        services.AddScoped<INotificationsUnitOfWork, NotificationsUnitOfWork>();
        services.AddScoped<INotificationProviderRepository, NotificationProviderRepository>();
        services.AddScoped<IRestaurantNotificationConfigurationRepository, RestaurantNotificationConfigurationRepository>();
        services.AddScoped<INotificationTemplateRepository, NotificationTemplateRepository>();
        services.AddScoped<ITemplateUniquenessChecker>(provider => provider.GetRequiredService<INotificationTemplateRepository>());
        services.AddScoped<INotificationPreferenceRepository, NotificationPreferenceRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationDirectory, NotificationDirectory>();
        services.AddScoped<ICampaignRepository, CampaignRepository>();
        services.AddScoped<ICampaignDirectory, CampaignDirectory>();
        services.AddScoped<IMarketingRulesRepository, MarketingRulesRepository>();
        services.AddScoped<IMarketingHistory, MarketingHistory>();
        services.AddScoped<IDeviceRegistrationRepository, DeviceRegistrationRepository>();
        services.AddScoped<IOtpChallengeRepository, OtpChallengeRepository>();
        services.AddScoped<INotificationInbox, NotificationInbox>();
        services.AddScoped<INotificationUsage, NotificationUsageCounter>();
        services.TryAddScoped<INotificationEntitlementChecker, ConfiguredNotificationEntitlements>();
        services.AddOutboxDispatcher<NotificationsDbContext>();

        // Coffre des identifiants et empreintes des OTP : clés de l'hôte, OBLIGATOIRES (vérifiées à la première utilisation).
        services.AddSingleton<SecretCipher>();
        services.AddScoped<INotificationSecretStore, PostgresNotificationSecretStore>();
        services.AddSingleton<IOtpHasher, HmacOtpHasher>();
        services.AddSingleton<IUnsubscribeLinks, HmacUnsubscribeLinks>();

        // Intégrations des prestataires présentes dans le code : une par prestataire (in-app, SMTP, Mailjet, WhatsApp Cloud
        // API de Meta, Brevo, Amazon SES, Resend, Twilio, Infobip, Termii, Africa's Talking, Firebase, OneSignal). Un
        // nouveau prestataire s'ajoute ici, sans toucher au Domain ni à l'Application.
        services.AddSingleton<INotificationSenderFactory, InAppSenderFactory>();
        services.AddSingleton<ISmtpTransport, MailKitSmtpTransport>();
        services.AddSingleton<INotificationSenderFactory, SmtpSenderFactory>();
        services.TryAddSingleton(new WhatsAppMetaOptions());
        services.AddSingleton<INotificationSenderFactory>(provider => new WhatsAppMetaSenderFactory(
            new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) }) { Timeout = TimeSpan.FromSeconds(20) },
            provider.GetRequiredService<WhatsAppMetaOptions>()));
        services.AddSingleton<INotificationSenderFactory>(_ => new MailjetSenderFactory(
            new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) }) { Timeout = TimeSpan.FromSeconds(20) }));
        services.AddSingleton<INotificationWebhookReader, MailjetWebhookReader>();
        services.AddSingleton<INotificationSenderFactory>(_ => new BrevoSenderFactory(ProviderSupport.NewHttpClient()));
        services.AddSingleton<INotificationSenderFactory>(_ => new BrevoSenderFactory(ProviderSupport.NewHttpClient(), NotificationChannel.Sms));
        services.AddSingleton<INotificationWebhookReader>(_ => new BrevoWebhookReader());
        services.AddSingleton<INotificationWebhookReader>(_ => new BrevoWebhookReader(BrevoSenderFactory.SmsCode));
        services.AddSingleton<INotificationSenderFactory>(provider => new AmazonSesSenderFactory(ProviderSupport.NewHttpClient(), provider.GetRequiredService<IClock>()));
        services.AddSingleton<INotificationSenderFactory>(_ => new ResendSenderFactory(ProviderSupport.NewHttpClient()));
        services.AddSingleton<INotificationWebhookReader, ResendWebhookReader>();
        services.AddSingleton<INotificationSenderFactory>(_ => new TwilioSenderFactory(ProviderSupport.NewHttpClient()));
        services.AddSingleton<INotificationSenderFactory>(_ => new TwilioSenderFactory(ProviderSupport.NewHttpClient(), NotificationChannel.WhatsApp));
        services.AddSingleton<INotificationWebhookReader>(_ => new TwilioWebhookReader());
        services.AddSingleton<INotificationWebhookReader>(_ => new TwilioWebhookReader(TwilioSenderFactory.WhatsAppCode));
        services.AddSingleton<INotificationSenderFactory>(_ => new InfobipSenderFactory(ProviderSupport.NewHttpClient()));
        services.AddSingleton<INotificationWebhookReader, InfobipWebhookReader>();
        services.AddSingleton<INotificationSenderFactory>(_ => new TermiiSenderFactory(ProviderSupport.NewHttpClient()));
        services.AddSingleton<INotificationWebhookReader, TermiiWebhookReader>();
        services.AddSingleton<INotificationSenderFactory>(_ => new AfricasTalkingSenderFactory(ProviderSupport.NewHttpClient()));
        services.AddSingleton<INotificationWebhookReader, AfricasTalkingWebhookReader>();
        services.AddSingleton<INotificationSenderFactory>(provider => new FirebaseSenderFactory(ProviderSupport.NewHttpClient(), provider.GetRequiredService<IClock>()));
        services.AddSingleton<INotificationSenderFactory>(_ => new OneSignalSenderFactory(ProviderSupport.NewHttpClient()));
        services.AddSingleton<WhatsAppMetaWebhookReader>();
        services.AddSingleton<INotificationWebhookReader>(provider => provider.GetRequiredService<WhatsAppMetaWebhookReader>());
        services.AddSingleton<INotificationWebhookChallenge>(provider => provider.GetRequiredService<WhatsAppMetaWebhookReader>());
        services.AddSingleton<FakeNotificationOutbox>();
        if (options.EnableFakeProvider)
        {
            services.AddSingleton<INotificationSenderFactory, FakeSenderFactory>();
            services.AddSingleton<INotificationWebhookReader, FakeWebhookReader>();
        }

        services.AddScoped<INotificationSenderResolver, NotificationSenderResolver>();
        services.AddScoped<NotificationWebhookReceiver>();
        services.AddSingleton<NotificationWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<NotificationWorker>());
        return services;
    }

    internal static DbContextOptionsBuilder UseNotificationsNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));

    /// <summary>
    /// Pose ce que la plateforme fournit d'office s'il manque : le prestataire interne IN_APP (actif), Mailjet (proposé aux
    /// restaurants sur leur propre compte) et les templates
    /// de la plateforme des codes à usage unique (email, SMS, WhatsApp ; français et anglais). Idempotent ; ne touche
    /// jamais un élément existant (modifié ou retiré par la plateforme).
    /// </summary>
    public static async Task SeedBuiltInNotificationsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var clock = provider.GetRequiredService<IClock>();
        var providers = provider.GetRequiredService<INotificationProviderRepository>();
        var templates = provider.GetRequiredService<INotificationTemplateRepository>();

        if (await providers.FindByCodeAsync(NotificationProviderCode.InApp, cancellationToken) is null)
        {
            var inApp = NotificationProvider.Create(
                NotificationProviderCode.InApp,
                "In-app",
                NotificationChannel.InApp,
                NotificationProviderConfigurationType.Internal,
                NotificationProviderCapability.Create(
                    NotificationChannel.InApp, [NotificationFeature.Text, NotificationFeature.Title, NotificationFeature.DeepLink]),
                isCodeUnique: true,
                clock);
            inApp.Enable(clock);
            providers.Add(inApp);
        }

        // Mailjet : proposé d'office aux restaurants, sur LEUR compte (la plateforme peut le retirer ou le modifier).
        var mailjet = NotificationProviderCode.Create(MailjetSenderFactory.Code);
        if (await providers.FindByCodeAsync(mailjet, cancellationToken) is null)
        {
            var mailjetProvider = NotificationProvider.Create(
                mailjet,
                "Mailjet",
                NotificationChannel.Email,
                NotificationProviderConfigurationType.TenantOnly,
                NotificationProviderCapability.Create(NotificationChannel.Email, [NotificationFeature.Text, NotificationFeature.Html]),
                isCodeUnique: true,
                clock);
            mailjetProvider.Enable(clock);
            providers.Add(mailjetProvider);
        }

        foreach (var (key, channel, language, subject, body) in BuiltInTemplates)
        {
            var lang = NotificationLanguage.Create(language);
            if (!await templates.IsKeyUniqueAsync(null, key, channel, lang, cancellationToken))
            {
                continue;
            }

            var template = NotificationTemplate.CreatePlatformTemplate(key, channel, lang, subject, body, isKeyUniqueForChannelAndLanguage: true, clock);
            template.Publish(clock);
            templates.Add(template);
        }

        await provider.GetRequiredService<INotificationsUnitOfWork>().SaveChangesAsync(cancellationToken);
    }

    /// <summary>Templates de la plateforme posés au démarrage s'ils manquent : codes à usage unique, rapport prêt, webhook désactivé, planning.</summary>
    private static IEnumerable<(TemplateKey Key, NotificationChannel Channel, string Language, string? Subject, string Body)> BuiltInTemplates =>
        BuiltInOtpTemplates.Select(template => (NotificationTypes.SecurityOtp.Key, template.Channel, template.Language, template.Subject, template.Body))
            .Concat(BuiltInReportTemplates.Select(template => (NotificationTypes.StaffReportReady.Key, template.Channel, template.Language, template.Subject, template.Body)))
            .Concat(BuiltInWebhookTemplates.Select(template => (NotificationTypes.StaffWebhookDisabled.Key, template.Channel, template.Language, template.Subject, template.Body)))
            .Concat(BuiltInScheduleTemplates);

    // Planning de l'équipe : jamais l'horaire d'un collègue, seulement la semaine, le volume et le lien vers « Mon planning ».
    private static readonly (TemplateKey Key, NotificationChannel Channel, string Language, string? Subject, string Body)[] BuiltInScheduleTemplates =
    [
        (NotificationTypes.StaffSchedulePublished.Key, NotificationChannel.InApp, "fr", "Planning publié",
            "Votre planning de la semaine du {{schedule.week}} ({{location.name}}) : {{schedule.shifts}} créneau(x), {{schedule.hours}}."),
        (NotificationTypes.StaffSchedulePublished.Key, NotificationChannel.InApp, "en", "Schedule published",
            "Your schedule for the week of {{schedule.week}} ({{location.name}}): {{schedule.shifts}} shift(s), {{schedule.hours}}."),
        (NotificationTypes.StaffSchedulePublished.Key, NotificationChannel.WhatsApp, "fr", null,
            "{{restaurant.name}} : votre planning de la semaine du {{schedule.week}} est publié ({{schedule.shifts}} créneau(x), {{schedule.hours}}). Détail : {{schedule.url}}"),
        (NotificationTypes.StaffSchedulePublished.Key, NotificationChannel.WhatsApp, "en", null,
            "{{restaurant.name}}: your schedule for the week of {{schedule.week}} is out ({{schedule.shifts}} shift(s), {{schedule.hours}}). Details: {{schedule.url}}"),
        (NotificationTypes.StaffSchedulePublished.Key, NotificationChannel.Sms, "fr", null,
            "{{restaurant.name}} : planning sem. du {{schedule.week}} publié, {{schedule.shifts}} créneau(x), {{schedule.hours}}. {{schedule.url}}"),
        (NotificationTypes.StaffSchedulePublished.Key, NotificationChannel.Sms, "en", null,
            "{{restaurant.name}}: schedule for week of {{schedule.week}} out, {{schedule.shifts}} shift(s), {{schedule.hours}}. {{schedule.url}}"),
        (NotificationTypes.StaffTimeOffRequested.Key, NotificationChannel.InApp, "fr", "Demande d'absence",
            "{{staff.name}} demande une absence : {{timeoff.period}}. À valider dans Planning."),
        (NotificationTypes.StaffTimeOffRequested.Key, NotificationChannel.InApp, "en", "Time off request",
            "{{staff.name}} asks for time off: {{timeoff.period}}. Review it in Schedule."),
        (NotificationTypes.StaffTimeOffDecided.Key, NotificationChannel.InApp, "fr", "Votre absence",
            "Votre absence du {{timeoff.period}} : {{timeoff.status}}."),
        (NotificationTypes.StaffTimeOffDecided.Key, NotificationChannel.InApp, "en", "Your time off",
            "Your time off ({{timeoff.period}}): {{timeoff.status}}."),
        (NotificationTypes.StaffShiftSwap.Key, NotificationChannel.InApp, "fr", "Remplacement",
            "Créneau du {{shift.date}} ({{shift.time}}) avec {{staff.name}} : {{swap.status}}."),
        (NotificationTypes.StaffShiftSwap.Key, NotificationChannel.InApp, "en", "Shift swap",
            "Shift on {{shift.date}} ({{shift.time}}) with {{staff.name}}: {{swap.status}}."),
        (NotificationTypes.StaffSubscriptionEnding.Key, NotificationChannel.InApp, "fr", "Abonnement",
            "Rappel : {{subscription.reason}} {{subscription.plan}} se termine le {{subscription.endDate}}. Réglez la suite depuis Abonnement pour ne pas être suspendu."),
        (NotificationTypes.StaffSubscriptionEnding.Key, NotificationChannel.InApp, "en", "Subscription",
            "Reminder: {{subscription.reason}} {{subscription.plan}} ends on {{subscription.endDate}}. Pay for what comes next in Subscription to avoid suspension."),
        (NotificationTypes.StaffSubscriptionEnding.Key, NotificationChannel.Email, "fr", "{{restaurant.name}} : votre abonnement arrive à échéance",
            "Bonjour, {{subscription.reason}} {{subscription.plan}} de {{restaurant.name}} se termine le {{subscription.endDate}}. Pour continuer sans interruption, réglez la période suivante en ligne : {{subscription.url}} Sans paiement, l'accès sera suspendu après le délai de grâce."),
        (NotificationTypes.StaffSubscriptionEnding.Key, NotificationChannel.Email, "en", "{{restaurant.name}}: your subscription is ending",
            "Hello, {{subscription.reason}} {{subscription.plan}} for {{restaurant.name}} ends on {{subscription.endDate}}. To keep going, pay for the next period online: {{subscription.url}} Without payment, access will be suspended after the grace period."),
    ];

    private static readonly (NotificationChannel Channel, string Language, string? Subject, string Body)[] BuiltInWebhookTemplates =
    [
        (NotificationChannel.InApp, "fr", "Webhook désactivé",
            "L'envoi des webhooks vers {{webhook.url}} est suspendu après {{webhook.failures}} échecs d'affilée. Vérifiez le destinataire, testez-le puis réactivez-le."),
        (NotificationChannel.InApp, "en", "Webhook disabled",
            "Webhooks to {{webhook.url}} are paused after {{webhook.failures}} failures in a row. Check the receiver, test it, then enable it again."),
        (NotificationChannel.Email, "fr", "{{restaurant.name}} : un webhook a été désactivé",
            "L'envoi des webhooks de {{restaurant.name}} vers {{webhook.url}} est suspendu après {{webhook.failures}} échecs d'affilée. Vérifiez le destinataire, testez-le depuis votre espace, puis réactivez-le."),
        (NotificationChannel.Email, "en", "{{restaurant.name}}: a webhook was disabled",
            "Webhooks from {{restaurant.name}} to {{webhook.url}} are paused after {{webhook.failures}} failures in a row. Check the receiver, test it from your account, then enable it again."),
    ];

    private static readonly (NotificationChannel Channel, string Language, string? Subject, string Body)[] BuiltInReportTemplates =
    [
        (NotificationChannel.Email, "fr", "Votre rapport « {{report.name}} » est prêt",
            "Le rapport « {{report.name}} » ({{report.period}}) de {{restaurant.name}} est prêt. Téléchargez-le depuis votre espace : {{report.url}}"),
        (NotificationChannel.Email, "en", "Your report \"{{report.name}}\" is ready",
            "The report \"{{report.name}}\" ({{report.period}}) for {{restaurant.name}} is ready. Download it from your account: {{report.url}}"),
        (NotificationChannel.InApp, "fr", "Rapport prêt", "« {{report.name}} » ({{report.period}}) est prêt au téléchargement."),
        (NotificationChannel.InApp, "en", "Report ready", "\"{{report.name}}\" ({{report.period}}) is ready to download."),
    ];

    private static readonly (NotificationChannel Channel, string Language, string? Subject, string Body)[] BuiltInOtpTemplates =
    [
        (NotificationChannel.Email, "fr", "Votre code de vérification",
            "Votre code est {{otp.code}}. Il expire dans {{otp.expiresInMinutes}} minutes. Ne le communiquez à personne."),
        (NotificationChannel.Email, "en", "Your verification code",
            "Your code is {{otp.code}}. It expires in {{otp.expiresInMinutes}} minutes. Never share it."),
        (NotificationChannel.Sms, "fr", null, "Code : {{otp.code}} (valable {{otp.expiresInMinutes}} min). Ne le partagez pas."),
        (NotificationChannel.Sms, "en", null, "Code: {{otp.code}} (valid {{otp.expiresInMinutes}} min). Never share it."),
        (NotificationChannel.WhatsApp, "fr", null, "Votre code : {{otp.code}} (valable {{otp.expiresInMinutes}} min). Ne le partagez pas."),
        (NotificationChannel.WhatsApp, "en", null, "Your code: {{otp.code}} (valid {{otp.expiresInMinutes}} min). Never share it."),
    ];

    /// <summary>Sans hôte : le tenant fixé par <see cref="BackgroundTenant.ActFor"/>, aucun sinon.</summary>
    private sealed class BackgroundTenantContext(BackgroundTenant background) : ITenantContext
    {
        public TenantId? CurrentTenantId => background.Current;
    }
}
