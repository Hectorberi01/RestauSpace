namespace Fiscal.Infrastructure;

public sealed class FiscalOptions
{
    /// <summary>
    /// Clé AES-256 (32 octets) qui chiffre les jetons e-MECeF au coffre. La changer rend les jetons enregistrés illisibles
    /// (chaque restaurant devra les ressaisir) : la conserver comme un secret.
    /// </summary>
    public byte[]? TokenKey { get; set; }

    /// <summary>API e-MECeF de test de la DGI (se termine par « / »).</summary>
    public Uri SandboxBaseUrl { get; set; } = new("https://developper.impots.bj/sygmef-emcf/api/");

    /// <summary>API e-MECeF réelle de la DGI (se termine par « / ») ; à vérifier auprès de la DGI avant la mise en service.</summary>
    public Uri ProductionBaseUrl { get; set; } = new("https://sygmef.impots.bj/emcf/api/");

    /// <summary>Faux dans les tests et les outils : la file n'est pas traitée en arrière-plan.</summary>
    public bool RunWorkers { get; set; } = true;

    public TimeSpan WorkerInterval { get; set; } = TimeSpan.FromSeconds(20);

    public int BatchSize { get; set; } = 50;
}
