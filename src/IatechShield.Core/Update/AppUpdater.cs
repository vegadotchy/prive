using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace IatechShield.Update;

/// <summary>Informations sur une version publiée sur GitHub Releases.</summary>
public sealed record ReleaseInfo(
    string Tag,
    Version Version,
    string Name,
    string Notes,
    string? InstallerUrl,
    string? ZipUrl,
    string HtmlUrl);

/// <summary>Résultat d'une vérification de mise à jour.</summary>
public sealed record UpdateCheck(bool UpdateAvailable, Version Current, ReleaseInfo? Latest, string? Error);

/// <summary>
/// Vérifie et télécharge les mises à jour de l'application depuis les
/// « Releases » GitHub du dépôt. Ne dépend d'aucun serveur tiers : l'API
/// publique GitHub suffit (https://api.github.com/repos/owner/repo/releases/latest).
/// </summary>
public static class AppUpdater
{
    public const string Owner = "vegadotchy";
    public const string Repo = "prive";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IATECH-SHIELD", "1.0"));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    /// <summary>Interroge GitHub et compare la dernière version publiée à la version courante.</summary>
    public static async Task<UpdateCheck> CheckAsync(Version current, CancellationToken cancel = default)
    {
        try
        {
            var latest = await FetchLatestAsync(cancel);
            if (latest is null)
                return new UpdateCheck(false, current, null, "aucune version publiée");

            bool newer = latest.Version > current;
            return new UpdateCheck(newer, current, latest, null);
        }
        catch (Exception ex)
        {
            return new UpdateCheck(false, current, null, ex.Message);
        }
    }

    private static async Task<ReleaseInfo?> FetchLatestAsync(CancellationToken cancel)
    {
        string url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
        using var resp = await Http.GetAsync(url, cancel);
        if (!resp.IsSuccessStatusCode)
            return null;

        await using var stream = await resp.Content.ReadAsStreamAsync(cancel);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancel);
        var root = doc.RootElement;

        string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        string name = root.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag;
        string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        string htmlUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";

        string? installer = null, zip = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string an = asset.TryGetProperty("name", out var anv) ? anv.GetString() ?? "" : "";
                string adl = asset.TryGetProperty("browser_download_url", out var adv) ? adv.GetString() ?? "" : "";
                if (adl.Length == 0) continue;
                if (an.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) installer = adl;
                else if (an.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) zip = adl;
            }
        }

        return new ReleaseInfo(tag, ParseTag(tag), name, notes, installer, zip, htmlUrl);
    }

    /// <summary>Convertit un tag « v0.2.1 » en <see cref="Version"/> (0.2.1).</summary>
    public static Version ParseTag(string tag)
    {
        string s = tag.TrimStart('v', 'V').Trim();
        // Conserve uniquement la partie numérique x.y.z(.w).
        int cut = s.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut > 0) s = s[..cut];
        return Version.TryParse(s, out var v) ? v : new Version(0, 0, 0);
    }

    /// <summary>Télécharge un fichier (installateur) dans le dossier temporaire et renvoie son chemin.</summary>
    public static async Task<string> DownloadAsync(string url, string fileName,
        IProgress<int>? progress = null, CancellationToken cancel = default)
    {
        string dest = Path.Combine(Path.GetTempPath(), fileName);

        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
        resp.EnsureSuccessStatusCode();
        long? total = resp.Content.Headers.ContentLength;

        await using var src = await resp.Content.ReadAsStreamAsync(cancel);
        await using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, cancel)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, n), cancel);
            read += n;
            if (total is > 0)
                progress?.Report((int)(read * 100L / total.Value));
        }
        return dest;
    }
}
