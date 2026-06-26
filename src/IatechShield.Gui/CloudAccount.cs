using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using IatechShield.Tools;

namespace IatechShield.Gui;

/// <summary>Session d'un compte client connecté (jetons + identité).</summary>
public sealed class CloudSession
{
    public string AccessToken { get; init; } = "";
    public string RefreshToken { get; init; } = "";
    public string UserId { get; init; } = "";
    public string Email { get; init; } = "";
}

/// <summary>Licence retrouvée dans le casier en ligne du compte.</summary>
public sealed class CloudLicense
{
    public string LicenseKey { get; init; } = "";
    public string Tier { get; init; } = "";
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>
/// Gère le compte client en ligne (Supabase) : inscription / connexion par
/// e-mail ou Google, et le « casier de licence » qui permet de retrouver sa
/// licence après réinstallation, sans ressaisir la clé.
///
/// La clé de licence reste signée hors-ligne : le cloud sert uniquement à la
/// mémoriser par compte. La clé publique embarquée vérifie toujours la signature.
/// </summary>
public sealed class CloudAccount
{
    // Projet Supabase « portail control » (clé publique : sans danger côté client).
    public const string SupabaseUrl = "https://vkfbjeudivczvldhvcsz.supabase.co";
    public const string AnonKey = "sb_publishable_jqwrWsH615rTVPQkGtUn9g_egRuAdMr";

    // Port de redirection local pour la connexion Google (boucle locale).
    public const int OAuthPort = 38219;
    public static string RedirectUri => $"http://127.0.0.1:{OAuthPort}/auth/callback";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public CloudSession? Session { get; private set; }
    public bool IsSignedIn => Session is not null;

    // ----------------------------------------------------------- e-mail ------

    /// <summary>Crée un compte par e-mail + mot de passe. Renvoie null si OK, sinon le message d'erreur.</summary>
    public Task<string?> SignUpEmailAsync(string email, string password)
        => AuthPasswordAsync("/auth/v1/signup", email, password);

    /// <summary>Connexion par e-mail + mot de passe. Renvoie null si OK, sinon le message d'erreur.</summary>
    public Task<string?> SignInEmailAsync(string email, string password)
        => AuthPasswordAsync("/auth/v1/token?grant_type=password", email, password);

    private async Task<string?> AuthPasswordAsync(string path, string email, string password)
    {
        try
        {
            string body = JsonSerializer.Serialize(new { email = email.Trim(), password });
            using var req = NewRequest(HttpMethod.Post, path, useUserToken: false);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var resp = await Http.SendAsync(req);
            string json = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return FriendlyAuthError(json);

            // L'inscription peut exiger une confirmation par e-mail (selon réglages projet).
            if (!TryStoreSession(json))
                return "Compte créé. Vérifiez votre boîte mail pour confirmer l'adresse, puis connectez-vous.";

            return null;
        }
        catch (Exception ex)
        {
            return "Connexion au serveur impossible : " + ex.Message;
        }
    }

    // ----------------------------------------------------------- Google ------

    private string _pkceVerifier = "";

    /// <summary>Construit l'URL d'autorisation Google (avec PKCE) à ouvrir dans le navigateur.</summary>
    public string BuildGoogleAuthorizeUrl()
    {
        _pkceVerifier = RandomUrlToken(48);
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(_pkceVerifier)));
        string redirect = Uri.EscapeDataString(RedirectUri);
        return $"{SupabaseUrl}/auth/v1/authorize?provider=google&redirect_to={redirect}" +
               $"&code_challenge={challenge}&code_challenge_method=s256";
    }

    /// <summary>Ouvre le navigateur, attend le retour Google sur la boucle locale et termine la connexion.</summary>
    public async Task<string?> SignInWithGoogleAsync()
    {
        string url = BuildGoogleAuthorizeUrl();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{OAuthPort}/");
        try { listener.Start(); }
        catch (Exception ex) { return "Impossible d'ouvrir le canal local : " + ex.Message; }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

            // Attend la redirection (90 s max).
            var ctxTask = listener.GetContextAsync();
            if (await Task.WhenAny(ctxTask, Task.Delay(TimeSpan.FromSeconds(90))) != ctxTask)
                return "Délai dépassé : la connexion Google n'a pas abouti.";

            var ctx = await ctxTask;
            string? code = ctx.Request.QueryString["code"];
            string? error = ctx.Request.QueryString["error_description"] ?? ctx.Request.QueryString["error"];
            WriteBrowserClosePage(ctx.Response, error is null);

            if (!string.IsNullOrEmpty(error)) return "Google a refusé la connexion : " + error;
            if (string.IsNullOrEmpty(code)) return "Aucun code reçu de Google.";

            return await ExchangeCodeAsync(code);
        }
        catch (Exception ex)
        {
            return "Échec de la connexion Google : " + ex.Message;
        }
        finally
        {
            try { listener.Stop(); } catch { }
        }
    }

    private async Task<string?> ExchangeCodeAsync(string code)
    {
        string body = JsonSerializer.Serialize(new { auth_code = code, code_verifier = _pkceVerifier });
        using var req = NewRequest(HttpMethod.Post, "/auth/v1/token?grant_type=pkce", useUserToken: false);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(req);
        string json = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) return FriendlyAuthError(json);
        return TryStoreSession(json) ? null : "Réponse inattendue du serveur Google.";
    }

    // ------------------------------------------------- session persistante ---

    /// <summary>Restaure une session enregistrée (jeton de rafraîchissement) au démarrage.</summary>
    public async Task<bool> RestoreSessionAsync()
    {
        var saved = SecretVault.Load("cloud");
        string? refresh = saved.GetValueOrDefault("refresh");
        if (string.IsNullOrWhiteSpace(refresh)) return false;

        try
        {
            string body = JsonSerializer.Serialize(new { refresh_token = refresh });
            using var req = NewRequest(HttpMethod.Post, "/auth/v1/token?grant_type=refresh_token", useUserToken: false);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var resp = await Http.SendAsync(req);
            string json = await resp.Content.ReadAsStringAsync();
            return resp.IsSuccessStatusCode && TryStoreSession(json);
        }
        catch
        {
            return false;
        }
    }

    public void SignOut()
    {
        Session = null;
        SecretVault.Delete("cloud");
    }

    // ------------------------------------------------- casier de licence -----

    /// <summary>Récupère la licence enregistrée pour ce compte (ou null).</summary>
    public async Task<CloudLicense?> FetchLicenseAsync()
    {
        if (Session is null) return null;
        try
        {
            using var req = NewRequest(HttpMethod.Get,
                "/rest/v1/shield_licenses?select=license_key,tier,expires_at", useUserToken: true);
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return null;

            var row = doc.RootElement[0];
            DateTimeOffset? exp = row.TryGetProperty("expires_at", out var e) && e.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(e.GetString(), out var d) ? d : null;
            return new CloudLicense
            {
                LicenseKey = row.GetProperty("license_key").GetString() ?? "",
                Tier = row.TryGetProperty("tier", out var t) ? t.GetString() ?? "" : "",
                ExpiresAt = exp
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Enregistre (ou met à jour) la licence du compte dans le casier en ligne.</summary>
    public async Task<bool> SaveLicenseAsync(string licenseKey, string tier, DateTimeOffset? expiresAt)
    {
        if (Session is null) return false;
        try
        {
            var row = new
            {
                user_id = Session.UserId,
                email = Session.Email,
                license_key = licenseKey,
                tier,
                expires_at = expiresAt?.UtcDateTime
            };
            using var req = NewRequest(HttpMethod.Post, "/rest/v1/shield_licenses", useUserToken: true);
            req.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");
            req.Content = new StringContent(JsonSerializer.Serialize(row), Encoding.UTF8, "application/json");
            using var resp = await Http.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // --------------------------------------------------------------- utils ---

    private HttpRequestMessage NewRequest(HttpMethod method, string path, bool useUserToken)
    {
        var req = new HttpRequestMessage(method, SupabaseUrl + path);
        req.Headers.TryAddWithoutValidation("apikey", AnonKey);
        string bearer = useUserToken && Session is not null ? Session.AccessToken : AnonKey;
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return req;
    }

    private bool TryStoreSession(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("access_token", out var at) || at.ValueKind != JsonValueKind.String)
                return false;

            string access = at.GetString() ?? "";
            string refresh = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? "" : "";
            string uid = "", email = "";
            if (root.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object)
            {
                uid = user.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
                email = user.TryGetProperty("email", out var em) ? em.GetString() ?? "" : "";
            }

            Session = new CloudSession { AccessToken = access, RefreshToken = refresh, UserId = uid, Email = email };
            SecretVault.Save("cloud", new Dictionary<string, string>
            {
                ["refresh"] = refresh,
                ["email"] = email,
                ["uid"] = uid
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string RandomUrlToken(int bytes)
    {
        byte[] buf = RandomNumberGenerator.GetBytes(bytes);
        return Base64Url(buf);
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string FriendlyAuthError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            string msg = r.TryGetProperty("msg", out var m) ? m.GetString() ?? ""
                       : r.TryGetProperty("error_description", out var ed) ? ed.GetString() ?? ""
                       : r.TryGetProperty("message", out var ms) ? ms.GetString() ?? "" : "";
            string low = msg.ToLowerInvariant();
            if (low.Contains("invalid login")) return "E-mail ou mot de passe incorrect.";
            if (low.Contains("already registered") || low.Contains("already been registered"))
                return "Un compte existe déjà avec cet e-mail. Connectez-vous plutôt.";
            if (low.Contains("password")) return "Mot de passe trop court (6 caractères minimum).";
            if (low.Contains("not confirmed")) return "E-mail non confirmé : cliquez sur le lien reçu par mail.";
            return string.IsNullOrWhiteSpace(msg) ? "Erreur d'authentification." : msg;
        }
        catch
        {
            return "Erreur d'authentification.";
        }
    }

    private static void WriteBrowserClosePage(HttpListenerResponse res, bool ok)
    {
        string html = ok
            ? "<html><head><meta charset='utf-8'></head><body style='font-family:Segoe UI;background:#0A1726;color:#fff;text-align:center;padding-top:80px'>" +
              "<h2>✅ Connexion réussie</h2><p>Vous pouvez fermer cet onglet et revenir à IATECH-SHIELD PRO.</p></body></html>"
            : "<html><head><meta charset='utf-8'></head><body style='font-family:Segoe UI;background:#0A1726;color:#fff;text-align:center;padding-top:80px'>" +
              "<h2>❌ Connexion annulée</h2><p>Revenez à IATECH-SHIELD PRO et réessayez.</p></body></html>";
        byte[] buf = Encoding.UTF8.GetBytes(html);
        res.ContentType = "text/html; charset=utf-8";
        try { res.OutputStream.Write(buf, 0, buf.Length); } catch { }
        res.Close();
    }
}
