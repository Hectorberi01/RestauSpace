namespace Catalog.Domain;

public static class TaxCategoryErrors
{
    public static readonly DomainError CodeInvalid = new("catalog.tax_category.code_invalid", "A tax category code is 1 to 32 characters among letters, digits, '_' and '-'.");
    public static readonly DomainError CodeNotUnique = new("catalog.tax_category.code_not_unique", "Another tax category of this tenant already uses this code.", DomainErrorKind.Conflict);
    public static readonly DomainError NameEmpty = new("catalog.tax_category.name_empty", "A tax category name is required.");
    public static readonly DomainError NameTooLong = new("catalog.tax_category.name_too_long", "A tax category name cannot exceed 100 characters.");
    public static readonly DomainError RateOutOfRange = new("catalog.tax_category.rate_out_of_range", "A tax rate is a fraction between 0 and 1 inclusive.");
    public static readonly DomainError RateTooPrecise = new("catalog.tax_category.rate_too_precise", "A tax rate cannot have more than 6 decimals.");
    public static readonly DomainError Archived = new("catalog.tax_category.archived", "An archived tax category can no longer be modified.");
}
