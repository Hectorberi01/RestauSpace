namespace Notifications.Domain;

/// <summary>
/// Ce que le domaine du catalogue ne doit JAMAIS contenir : aucun prestataire réel nommé (en ajouter un ne touche pas au
/// domaine), aucun secret dans un événement.
/// </summary>
public class NotificationProviderGuardTests
{
    private static readonly string[] ProviderNames = ["resend", "sendgrid", "smtp", "twilio", "meta", "firebase", "fcm", "apns", "vonage", "brevo"];

    private static readonly string[] SensitiveWords = ["secret", "password", "token", "apikey", "key", "credential", "credentials"];

    private static readonly Type[] DomainTypes = typeof(NotificationProvider).Assembly.GetTypes()
        .Where(type => type.Namespace == "Notifications.Domain" && !type.Name.StartsWith('<'))
        .ToArray();

    [Fact]
    public void The_domain_names_no_real_provider()
    {
        var names = DomainTypes
            .SelectMany(type => type.GetMembers().Select(member => $"{type.Name}.{member.Name}").Prepend(type.Name))
            .Where(name => ProviderNames.Any(provider => name.Contains(provider, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.Empty(names);
    }

    [Fact]
    public void The_only_provider_code_known_by_the_domain_is_in_app()
    {
        var codes = typeof(NotificationProviderCode)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.FieldType == typeof(NotificationProviderCode))
            .Select(field => ((NotificationProviderCode)field.GetValue(null)!).Value)
            .ToList();

        Assert.Equal(new[] { "IN_APP" }, codes);
    }

    [Fact]
    public void No_provider_event_carries_a_secret()
    {
        var events = DomainTypes.Where(type => type.Name.StartsWith("NotificationProvider", StringComparison.Ordinal)
            && typeof(IDomainEvent).IsAssignableFrom(type)).ToList();
        var leaks = events
            .SelectMany(type => type.GetProperties().Select(property => (Type: type, Property: property)))
            .Where(pair => SensitiveWords.Any(word => System.Text.RegularExpressions.Regex.Split(pair.Property.Name, "(?<!^)(?=[A-Z])")
                .Any(segment => segment.Equals(word, StringComparison.OrdinalIgnoreCase))))
            .Select(pair => $"{pair.Type.Name}.{pair.Property.Name}")
            .ToList();

        Assert.Equal(6, events.Count);
        Assert.Empty(leaks);
    }
}
