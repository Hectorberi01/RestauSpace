namespace Notifications.Domain;

/// <summary>
/// Produit le contenu RENDU d'une notification : chaque <c>{{variable}}</c> du template retenu remplacée par sa valeur.
/// Seul un template publié et actif sert ; une variable sans valeur est refusée (jamais de « {{customer.firstName}} »
/// envoyé à un client). Les valeurs ne peuvent pas réintroduire de variable (<see cref="NotificationPayload"/>) et le
/// résultat est revalidé par <see cref="NotificationContent.Create"/> (sujet selon le canal, longueur).
/// </summary>
public static class TemplateRenderer
{
    public static NotificationContent Render(NotificationTemplate template, NotificationPayload payload)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(payload);
        DomainException.ThrowIf(!template.IsUsable, TemplateErrors.NotUsable);
        DomainException.ThrowIf(
            template.Variables.Any(variable => payload.ValueOf(variable) is null), TemplateErrors.VariableMissing);

        string ValueOf(string variable) => payload.ValueOf(variable)!;
        return NotificationContent.Create(
            template.Channel, TemplateVariables.Replace(template.Subject, ValueOf), TemplateVariables.Replace(template.Body, ValueOf));
    }
}
