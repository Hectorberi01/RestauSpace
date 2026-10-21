namespace Notifications.Domain;

public static class TemplateErrors
{
    public static readonly DomainError KeyInvalid = new("notifications.template_key.invalid", "Template key must be a lowercase dotted code such as 'reservation.confirmed'.");

    public static readonly DomainError LanguageInvalid = new("notifications.language.invalid", "Language must be a primary language subtag of 2 or 3 letters such as 'fr'.");

    public static readonly DomainError ChannelInvalid = new("notifications.template.channel_invalid", "Channel is not supported.");
    public static readonly DomainError KeyAlreadyUsed = new("notifications.template.key_already_used", "A template already exists for this key, channel and language; create a new version instead.");
    public static readonly DomainError VersionAlreadyExists = new("notifications.template.version_already_exists", "The next version of this template already exists.", DomainErrorKind.Conflict);
    public static readonly DomainError SubjectRequired = new("notifications.template.subject_required", "An email template needs a subject.");
    public static readonly DomainError SubjectTooLong = new("notifications.template.subject_too_long", "Subject is limited to 200 characters.");
    public static readonly DomainError SubjectNotSupported = new("notifications.template.subject_not_supported", "SMS and WhatsApp templates have no subject (email: subject; push and in-app: title).");
    public static readonly DomainError BodyEmpty = new("notifications.template.body_empty", "Template body is required.");
    public static readonly DomainError BodyTooLong = new("notifications.template.body_too_long", "Template body is limited to 20000 characters.");
    public static readonly DomainError PublishedImmutable = new("notifications.template.published_immutable", "A published template is immutable; create a new version to change it.");
    public static readonly DomainError Archived = new("notifications.template.archived", "An archived template cannot be changed.");
    public static readonly DomainError TypeUnknown = new("notifications.template.type_unknown", "This notification type does not exist.");
    public static readonly DomainError VariableSyntaxInvalid = new("notifications.template.variable_syntax_invalid", "Variables are written {{name.path}}: letters and digits separated by dots, nothing else between the braces.");
    public static readonly DomainError TooManyVariables = new("notifications.template.too_many_variables", "A template content uses at most 100 variables.");
    public static readonly DomainError VariableNotAllowed = new("notifications.template.variable_not_allowed", "This template uses a variable that its notification type does not provide.");
    public static readonly DomainError UnsubscribeLinkRequired = new("notifications.template.unsubscribe_link_required", "A marketing email must contain the {{unsubscribe.url}} link.");
    public static readonly DomainError AlreadyEnabled = new("notifications.template.already_enabled", "The template is already enabled.");
    public static readonly DomainError AlreadyDisabled = new("notifications.template.already_disabled", "The template is already disabled.");
    public static readonly DomainError NotUsable = new("notifications.template.not_usable", "Only a published and enabled template can be rendered.");
    public static readonly DomainError VariableMissing = new("notifications.template.variable_missing", "A variable used by the template has no value.");
    public static readonly DomainError DraftCannotBeVersioned = new("notifications.template.draft_cannot_be_versioned", "A draft is still editable; a new version can only follow a published or archived one.");
}
