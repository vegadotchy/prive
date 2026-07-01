using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IatechShield.Gui;

/// <summary>Jetons OAuth Gmail (jeton d'accès + jeton de rafraîchissement).</summary>
public sealed record GmailTokens(string AccessToken, string? RefreshToken);

/// <summary>
/// Client Gmail minimaliste : authentification OAuth 2.0 « application de bureau »
/// (boucle locale 127.0.0.1 + PKCE), rafraîchissement du jeton, et lecture des
/// e-mails récents au format brut (RFC 822) pour analyse par le <see cref="MailShield"/>.
/// Portée demandée : lecture seule (gmail.readonly). Le mot de passe de l'utilisateur
/// n'est jamais vu par l'application.
/// </summary>
public static class GmailClient
{
    private const string Scope = "https://www.googleapis.com/auth/gmail.readonly";
    private static readonly HttpClient Http = new();

    /// <summary>Lance le consentement Google dans le navigateur et récupère les jetons.</summary>
    public static async Task<GmailTokens> AuthorizeAsync(string clientId, string clientSecret, CancellationToken ct)
    {
        // PKCE : vérifieur + défi.
        string verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string state = Base64Url(RandomNumberGenerator.GetBytes(16));

        int port = FreePort();
        string redirect = $"http://127.0.0.1:{port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect);
        listener.Start();

        string authUrl =
            "https://accounts.google.com/o/oauth2/v2/auth" +
            "?client_id=" + Uri.EscapeDataString(clientId) +
            "&redirect_uri=" + Uri.EscapeDataString(redirect) +
            "&response_type=code" +
            "&scope=" + Uri.EscapeDataString(Scope) +
            "&access_type=offline&prompt=consent" +
            "&code_challenge=" + challenge + "&code_challenge_method=S256" +
            "&state=" + state;

        Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

        // Attend la redirection de Google (avec le code d'autorisation).
        var ctxTask = listener.GetContextAsync();
        var done = await Task.WhenAny(ctxTask, Task.Delay(TimeSpan.FromMinutes(3), ct));
        if (done != ctxTask)
        {
            listener.Stop();
            throw new TimeoutException("Aucune réponse d'autorisation reçue (délai dépassé).");
        }

        var ctx = await ctxTask;
        string? code = ctx.Request.QueryString["code"];
        string? returnedState = ctx.Request.QueryString["state"];
        string? error = ctx.Request.QueryString["error"];

        // Petite page de confirmation dans le navigateur.
        byte[] html = Encoding.UTF8.GetBytes(
            "<html><body style='font-family:sans-serif;background:#0A1726;color:#e5e7eb;text-align:center;padding-top:80px'>" +
            "<h2>IATECH-SHIELD PRO</h2><p>Connexion Gmail autorisee. Vous pouvez fermer cette fenetre.</p></body></html>");
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = html.Length;
        await ctx.Response.OutputStream.WriteAsync(html, ct);
        ctx.Response.Close();
        listener.Stop();

        if (!string.IsNullOrEmpty(error)) throw new Exception("Autorisation refusée : " + error);
        if (string.IsNullOrEmpty(code)) throw new Exception("Aucun code d'autorisation reçu.");
        if (returnedState != state) throw new Exception("État OAuth invalide (sécurité).");

        // Échange le code contre les jetons.
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code!,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirect,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = verifier,
        });
        using var resp = await Http.PostAsync("https://oauth2.googleapis.com/token", form, ct);
        string json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new Exception("Échec de l'échange de jeton : " + json);

        using var doc = JsonDocument.Parse(json);
        string access = doc.RootElement.GetProperty("access_token").GetString()!;
        string? refresh = doc.RootElement.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        return new GmailTokens(access, refresh);
    }

    /// <summary>Obtient un nouveau jeton d'accès à partir du jeton de rafraîchissement.</summary>
    public static async Task<string> RefreshAsync(string clientId, string clientSecret, string refreshToken, CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        });
        using var resp = await Http.PostAsync("https://oauth2.googleapis.com/token", form, ct);
        string json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new Exception("Échec du rafraîchissement : " + json);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    /// <summary>Récupère les e-mails récents au format brut (RFC 822) pour analyse.</summary>
    public static async Task<List<(string Id, string Raw)>> ListRecentRawAsync(string accessToken, int max, CancellationToken ct)
    {
        var result = new List<(string, string)>();
        using var listReq = new HttpRequestMessage(HttpMethod.Get,
            $"https://gmail.googleapis.com/gmail/v1/users/me/messages?maxResults={max}&q=newer_than:14d");
        listReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var listResp = await Http.SendAsync(listReq, ct);
        string listJson = await listResp.Content.ReadAsStringAsync(ct);
        if (!listResp.IsSuccessStatusCode) throw new Exception("Lecture de la boîte impossible : " + listJson);

        using var doc = JsonDocument.Parse(listJson);
        if (!doc.RootElement.TryGetProperty("messages", out var msgs)) return result;

        foreach (var m in msgs.EnumerateArray())
        {
            if (ct.IsCancellationRequested) break;
            string id = m.GetProperty("id").GetString()!;
            using var rawReq = new HttpRequestMessage(HttpMethod.Get,
                $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{id}?format=raw");
            rawReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var rawResp = await Http.SendAsync(rawReq, ct);
            if (!rawResp.IsSuccessStatusCode) continue;
            string rawJson = await rawResp.Content.ReadAsStringAsync(ct);
            using var rd = JsonDocument.Parse(rawJson);
            if (!rd.RootElement.TryGetProperty("raw", out var rawEl)) continue;
            string b64 = rawEl.GetString() ?? "";
            try
            {
                string raw = Encoding.UTF8.GetString(Convert.FromBase64String(b64.Replace('-', '+').Replace('_', '/').PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=')));
                result.Add((id, raw));
            }
            catch { }
        }
        return result;
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
