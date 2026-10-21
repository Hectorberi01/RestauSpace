namespace Identity.Api;

/// <summary>
/// Navigateur et système déduits de l'en-tête User-Agent (« Chrome · macOS »), pour reconnaître ses appareils dans la
/// liste des sessions. Seul ce libellé est gardé, jamais l'en-tête brut.
/// </summary>
public static class DeviceLabels
{
    public static string? From(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        var browser = Find(userAgent, ("Edg/", "Edge"), ("OPR/", "Opera"), ("Firefox/", "Firefox"), ("Chrome/", "Chrome"), ("Safari/", "Safari"), ("Dart/", "Application mobile"));
        var system = Find(userAgent, ("iPhone", "iPhone"), ("iPad", "iPad"), ("Android", "Android"), ("Mac OS X", "macOS"), ("Macintosh", "macOS"), ("Windows", "Windows"), ("CrOS", "ChromeOS"), ("Linux", "Linux"));
        return (browser, system) switch
        {
            (null, null) => null,
            (null, { } os) => os,
            ({ } name, null) => name,
            ({ } name, { } os) => $"{name} · {os}",
        };
    }

    private static string? Find(string userAgent, params (string Token, string Label)[] candidates) =>
        candidates.FirstOrDefault(candidate => userAgent.Contains(candidate.Token, StringComparison.Ordinal)).Label;
}
