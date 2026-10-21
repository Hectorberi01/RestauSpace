namespace Notifications.Domain;

/// <summary>Étape 5 : les variables d'un contenu, contrôlées, jamais exécutées.</summary>
public class TemplateVariablesTests
{
    [Fact]
    public void Variables_are_found_in_order_once_each()
    {
        var names = TemplateVariables.Extract("{{a.b}} puis {{ c }} puis {{a.b}}");

        Assert.Equal(new[] { "a.b", "c" }, names.ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Aucune variable { ni } accolade simple")]
    public void A_content_without_variables_has_none(string? content) =>
        Assert.Empty(TemplateVariables.Extract(content));

    [Fact]
    public void The_number_and_length_of_variables_are_bounded()
    {
        var many = string.Concat(Enumerable.Range(0, TemplateVariables.MaxVariablesPerContent + 1).Select(_ => "{{a}}"));

        DomainAssert.Throws(TemplateErrors.TooManyVariables, () => TemplateVariables.Extract(many));
        DomainAssert.Throws(
            TemplateErrors.VariableSyntaxInvalid,
            () => TemplateVariables.Extract("{{" + new string('a', TemplateVariables.MaxNameLength + 1) + "}}"));
    }

    [Fact]
    public void The_variables_a_type_does_not_provide_are_reported()
    {
        var notAllowed = TemplateVariables.NotAllowed(
            NotificationTypes.OrderReady, "Commande {{order.number}}", "Pour {{customer.firstName}} au {{customer.phone}}");

        Assert.Equal(new[] { "customer.phone" }, notAllowed.ToArray());
    }
}
