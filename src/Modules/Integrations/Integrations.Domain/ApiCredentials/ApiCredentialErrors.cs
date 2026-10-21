namespace Integrations.Domain;

public static class ApiCredentialErrors
{
    public static readonly DomainError KeyHashInvalid = new("integrations.api_key_hash.invalid", "Key hash is required, without whitespace, between 32 and 512 characters.");
    public static readonly DomainError ScopeInvalid = new("integrations.api_scope.invalid", "Scope must look like 'orders.read' (at least two lowercase segments separated by dots).");
    public static readonly DomainError ScopeCatalogEmpty = new("integrations.api_scope_catalog.empty", "A scope catalogue must contain at least one scope.");

    public static readonly DomainError NameEmpty = new("integrations.api_credential.name_empty", "Credential name is required.");
    public static readonly DomainError NameTooLong = new("integrations.api_credential.name_too_long", "Credential name is limited to 100 characters.");
    public static readonly DomainError ScopesRequired = new("integrations.api_credential.scopes_required", "A credential needs at least one scope.");
    public static readonly DomainError ScopeUnknown = new("integrations.api_credential.scope_unknown", "This scope is not part of the known scope catalogue.");
    public static readonly DomainError TooManyScopes = new("integrations.api_credential.too_many_scopes", "A credential is limited to 50 scopes.");
    public static readonly DomainError ExpiryNotInFuture = new("integrations.api_credential.expiry_not_in_future", "Expiry must be in the future.");
    public static readonly DomainError Revoked = new("integrations.api_credential.revoked", "The credential is revoked.");
    public static readonly DomainError Expired = new("integrations.api_credential.expired", "The credential is expired.");
    public static readonly DomainError ScopeNotGranted = new("integrations.api_credential.scope_not_granted", "The credential does not grant this scope.");
}
