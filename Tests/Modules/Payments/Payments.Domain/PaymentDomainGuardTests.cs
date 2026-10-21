namespace Payments.Domain;

/// <summary>
/// Étape 5 : ce que le domaine ne doit JAMAIS contenir. Aucun prestataire réel n'y est nommé (en ajouter un ne touche
/// pas au domaine : capacités + intégration dans l'Infrastructure), et aucun événement ne transporte de secret ni de
/// donnée bancaire.
/// </summary>
public class PaymentDomainGuardTests
{
    private static readonly string[] ProviderNames = ["stripe", "paypal", "mtn", "moov", "celtis", "fedapay", "kkiapay", "cinetpay", "sandbox"];

    private static readonly string[] SensitiveWords =
        ["secret", "credential", "password", "token", "apikey", "cardnumber", "pan", "cvv", "cvc", "iban", "expiry"];

    private static readonly Type[] DomainTypes = typeof(Payment).Assembly.GetTypes()
        .Where(type => type.Namespace == "Payments.Domain" && !type.Name.StartsWith('<'))
        .ToArray();

    [Fact]
    public void The_domain_names_no_real_provider()
    {
        // PaymentMethodType nomme des MOYENS (dont le portefeuille PayPal), pas des prestataires : il est écarté.
        var names = DomainTypes
            .Where(type => type != typeof(PaymentMethodType))
            .SelectMany(type => type.GetMembers().Select(member => $"{type.Name}.{member.Name}").Prepend(type.Name))
            .Where(name => ProviderNames.Any(provider => name.Contains(provider, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.Empty(names);
    }

    [Fact]
    public void The_only_provider_code_known_by_the_domain_is_cash()
    {
        var codes = typeof(PaymentProviderCode)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.FieldType == typeof(PaymentProviderCode))
            .Select(field => ((PaymentProviderCode)field.GetValue(null)!).Value)
            .ToList();

        Assert.Equal(new[] { "cash" }, codes);
    }

    [Fact]
    public void No_domain_event_carries_a_secret_or_bank_data()
    {
        var events = DomainTypes.Where(type => typeof(IDomainEvent).IsAssignableFrom(type) && !type.IsAbstract).ToList();
        var leaks = events
            .SelectMany(type => type.GetProperties().Select(property => (Type: type, Property: property)))
            .Where(pair => SensitiveWords.Any(word => IsWord(pair.Property.Name, word)))
            .Select(pair => $"{pair.Type.Name}.{pair.Property.Name}")
            .ToList();

        Assert.NotEmpty(events);
        Assert.Empty(leaks);
    }

    [Fact]
    public void A_payment_stores_no_bank_data()
    {
        var properties = new[] { typeof(Payment), typeof(PaymentAttempt), typeof(Refund) }
            .SelectMany(type => type.GetProperties().Select(property => $"{type.Name}.{property.Name}"))
            .Where(name => SensitiveWords.Any(word => IsWord(name.Split('.')[1], word)))
            .ToList();

        Assert.Empty(properties);
    }

    /// <summary>Le mot est un des segments du nom en PascalCase (« Pan » dans « CardPan », pas dans « Company »).</summary>
    private static bool IsWord(string pascalName, string word)
    {
        var segments = System.Text.RegularExpressions.Regex.Split(pascalName, "(?<!^)(?=[A-Z])")
            .Select(segment => segment.ToLowerInvariant())
            .ToList();
        // Mots composés (« ApiKey », « CardNumber ») : deux segments consécutifs.
        var pairs = segments.Zip(segments.Skip(1), (first, second) => first + second);
        return segments.Contains(word) || pairs.Contains(word);
    }
}
