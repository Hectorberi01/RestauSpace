namespace Catalog.Domain;

public static class MenuErrors
{
    public static readonly DomainError ScheduleInvalid = new("catalog.menu.schedule_invalid", "A time slot has a day and distinct start and end times.");
    public static readonly DomainError ScheduleTooLong = new("catalog.menu.schedule_too_long", "A menu has at most 28 time slots.");
    public static readonly DomainError Archived = new("catalog.menu.archived", "An archived menu can no longer be modified.");
    public static readonly DomainError PublishedIsReadOnly = new("catalog.menu.published_is_read_only", "A published menu must be unpublished before its content is modified.");
    public static readonly DomainError NotDraft = new("catalog.menu.not_draft", "Only a draft menu can be published.");
    public static readonly DomainError NotPublished = new("catalog.menu.not_published", "Only a published menu can be unpublished.");
    public static readonly DomainError NoSection = new("catalog.menu.no_section", "A menu needs at least one section to be published.");
    public static readonly DomainError EmptySection = new("catalog.menu.empty_section", "A menu cannot be published with an empty section.");
    public static readonly DomainError SectionNameDuplicate = new("catalog.menu.section_name_duplicate", "A section with the same name already exists in this menu.");
    public static readonly DomainError SectionNotFound = new("catalog.menu.section_not_found", "The section does not belong to this menu.");
    public static readonly DomainError ProductAlreadyInSection = new("catalog.menu.product_already_in_section", "The product is already listed in this section.");
    public static readonly DomainError ProductNotInSection = new("catalog.menu.product_not_in_section", "The product is not listed in this section.");
    public static readonly DomainError ProductArchived = new("catalog.menu.product_archived", "An archived product cannot be listed in a menu.");
    public static readonly DomainError ProductReferenceMissing = new("catalog.menu.product_reference_missing", "Publishing requires the current reference of every product the menu lists.");
    public static readonly DomainError TooManySections = new("catalog.menu.too_many_sections", "A menu cannot have more than 50 sections.");
    public static readonly DomainError TooManyProductsInSection = new("catalog.menu.too_many_products_in_section", "A menu section cannot list more than 200 products.");
    public static readonly DomainError PositionOutOfRange = new("catalog.menu.position_out_of_range", "The target position is outside the list.");
}
