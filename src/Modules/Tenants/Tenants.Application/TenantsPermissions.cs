namespace Tenants.Application;

/// <summary>Codes de permission déclarés par le module Tenants (accordés via les rôles Staff).</summary>
public static class TenantsPermissions
{
    /// <summary>Modifier les paramètres de son restaurant : nom, identité visuelle, fuseau, devise, mode de prix.</summary>
    public const string Configure = "tenant.configure";

    public static IReadOnlyList<string> All { get; } = [Configure];
}
