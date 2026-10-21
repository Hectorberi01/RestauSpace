namespace Customers.Application;

/// <summary>Erreurs des cas d'usage Customers (celles des règles métier vivent dans Customers.Domain).</summary>
public static class CustomersApplicationErrors
{
    public static readonly DomainError ConcurrentUpdate =
        new("customers.concurrent_update", "The resource was changed by someone else; reload it and try again.");

    // Aussi la réponse pour un client d'un autre tenant : on ne révèle jamais qu'il existe.
    public static readonly DomainError ImportSizeInvalid = new("customers.import.size_invalid", "Import 1 to 500 customers at a time.");
    public static readonly DomainError CustomerNotFound = new("customers.customer.not_found", "The customer does not exist.");

    public static readonly DomainError ConsentNotFound =
        new("customers.consent.not_found", "The customer has never given a consent on this channel.");

    public static readonly DomainError TenantNotWritable =
        new("customers.tenant.not_writable", "The restaurant account is suspended or closed: its customers can be read, not changed.");

    public static readonly DomainError PageInvalid =
        new("customers.search.page_invalid", "Skip must be zero or more and take between 1 and 100.");
}
