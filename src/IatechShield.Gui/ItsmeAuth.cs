using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using IatechShield.Tools;

namespace IatechShield.Gui;

/// <summary>Résultat d'une authentification itsme.</summary>
public sealed class ItsmeResult
{
    public bool Success { get; init; }
    public string Error { get; init; } = "";
    public string Subject { get; init; } = "";       // identifiant itsme stable
    public string Name { get; init; } = "";           // nom de famille
    public string FirstName { get; init; } = "";       // prénom
    public string FullName => $"{FirstName} {Name}".Trim();
    public string Phone { get; init; } = "";
}

/// <summary>
/// Client itsme réel (OpenID Connect). Quand l'utilisateur lance la connexion,
/// itsme envoie une vraie notification push sur son smartphone ; après confirmation
/// dans l'app itsme, le PC se déverrouille.
///
/// ⚠️ itsme exige un contrat partenaire : il faut renseigner dans les Réglages le
/// client_id, le client_secret et le code de service fournis par itsme (et l'URL de
/// redirection http://127.0.0.1:38220/itsme/callback doit être déclarée chez itsme).
/// Sans ces identifiants, le flux ne peut pas être lancé (itsme n'autorise aucun
/// envoi de push anonyme).
/// </summary>
public sealed class ItsmeAuth
{
    public const int CallbackPort = 38220;
    public static string RedirectUri => $"http://127.0.0.1:{CallbackPort}/itsme/callback";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public string ClientId { get; }
    public string ClientSecret { get; }
    public string ServiceCode { get; }
    public string Environment { get; }   // "prd" (production) ou "e2e" (sandbox)

    public ItsmeAuth(string clientId, string clientSecret, string serviceCode, string environment = "prd")
    {
        ClientId = clientId.Trim();
        ClientSecret = clientSecret.Trim();
        ServiceCode = serviceCode.Trim();
        Environment = string.IsNullOrWhiteSpace(environment) ? "prd" : environment.Trim();
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ServiceCode);

    private string Issuer => $"https://idp.{Environment}.itsme.services/v2";

    /// <summary>Charge la configuration itsme enregistrée (chiffrée).</summary>
    public static ItsmeAuth? FromStore()
    {
        var cfg = SecretVault.Load("itsme");
        string id = cfg.GetValueOrDefault("clientId") ?? "";
        if (string.IsNullOrWhiteSpace(id)) return null;
        return new ItsmeAuth(id, cfg.GetValueOrDefault("clientSecret") ?? "",
            cfg.GetValueOrDefault("serviceCode") ?? "", cfg.GetValueOrDefault("env") ?? "prd");
    }

    public static void SaveConfig(string clientId, string clientSecret, string serviceCode, string env)
        => SecretVault.Save("itsme", new Dictionary<string, string>
        {
            ["clientId"] = clientId.Trim(),
            ["clientSecret"] = clientSecret.Trim(),
            ["serviceCode"] = serviceCode.Trim(),
            ["env"] = string.IsNullOrWhiteSpace(env) ? "prd" : env.Trim()
        });

    /// <summary>
    /// Lance le flux itsme : ouvre la page itsme dans le navigateur (déclenche la
    /// notification push sur le téléphone), attend la confirmation et le retour sur
    /// la boucle locale, puis récupère l'identité.
    /// </summary>
    public async Task<ItsmeResult> AuthenticateAsync()
    {
        if (!IsConfigured)
            return new ItsmeResult { Error = "itsme non configuré : renseignez vos identifiants partenaire dans Réglages." };

        string authEndpoint, tokenEndpoint;
        try
        {
            using var discDoc = JsonDocument.Parse(
                await Http.GetStringAsync($"{Issuer}/.well-known/openid-configuration"));
            authEndpoint = discDoc.RootElement.GetProperty("authorization_endpoint").GetString()!;
            tokenEndpoint = discDoc.RootElement.GetProperty("token_endpoint").GetString()!;
        }
        catch (Exception ex)
        {
            return new ItsmeResult { Error = "Service itsme injoignable : " + ex.Message };
        }

        string state = Guid.NewGuid().ToString("N");
        string nonce = Guid.NewGuid().ToString("N");
        string scope = Uri.EscapeDataString($"openid profile service:{ServiceCode}");
        string url = $"{authEndpoint}?client_id={Uri.EscapeDataString(ClientId)}" +
                     $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
                     $"&response_type=code&scope={scope}&state={state}&nonce={nonce}";

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{CallbackPort}/");
        try { listener.Start(); }
        catch (Exception ex) { return new ItsmeResult { Error = "Canal local indisponible : " + ex.Message }; }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

            var ctxTask = listener.GetContextAsync();
            // L'utilisateur doit confirmer sur son téléphone : on laisse 2 minutes.
            if (await Task.WhenAny(ctxTask, Task.Delay(TimeSpan.FromSeconds(120))) != ctxTask)
                return new ItsmeResult { Error = "Délai dépassé : la confirmation itsme n'a pas été reçue." };

            var ctx = await ctxTask;
            string? code = ctx.Request.QueryString["code"];
            string? retState = ctx.Request.QueryString["state"];
            string? error = ctx.Request.QueryString["error_description"] ?? ctx.Request.QueryString["error"];
            WriteClosePage(ctx.Response, error is null && !string.IsNullOrEmpty(code));

            if (!string.IsNullOrEmpty(error)) return new ItsmeResult { Error = "itsme : " + error };
            if (string.IsNullOrEmpty(code)) return new ItsmeResult { Error = "Aucun code reçu d'itsme." };
            if (retState != state) return new ItsmeResult { Error = "Réponse itsme invalide (state)." };

            return await ExchangeAsync(tokenEndpoint, code);
        }
        catch (Exception ex)
        {
            return new ItsmeResult { Error = "Échec itsme : " + ex.Message };
        }
        finally { try { listener.Stop(); } catch { } }
    }

    private async Task<ItsmeResult> ExchangeAsync(string tokenEndpoint, string code)
    {
        try
        {
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["client_id"] = ClientId,
            };
            if (!string.IsNullOrWhiteSpace(ClientSecret)) form["client_secret"] = ClientSecret;

            using var req = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
            { Content = new FormUrlEncodedContent(form) };
            using var resp = await Http.SendAsync(req);
            string json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                return new ItsmeResult { Error = "itsme (jeton) : " + json };

            using var doc = JsonDocument.Parse(json);
            string idToken = doc.RootElement.TryGetProperty("id_token", out var t) ? t.GetString() ?? "" : "";
            var claims = DecodeJwtPayload(idToken);

            return new ItsmeResult
            {
                Success = true,
                Subject = claims.GetValueOrDefault("sub") ?? "",
                Name = claims.GetValueOrDefault("family_name") ?? claims.GetValueOrDefault("name") ?? "Utilisateur itsme",
                FirstName = claims.GetValueOrDefault("given_name") ?? "",
                Phone = claims.GetValueOrDefault("phone_number") ?? ""
            };
        }
        catch (Exception ex)
        {
            return new ItsmeResult { Error = "Échange du jeton itsme impossible : " + ex.Message };
        }
    }

    /// <summary>Décode (sans vérifier la signature) la charge utile d'un JWT id_token.</summary>
    private static Dictionary<string, string> DecodeJwtPayload(string jwt)
    {
        var map = new Dictionary<string, string>();
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return map;
            string payload = parts[1].Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4) { case 2: payload += "=="; break; case 3: payload += "="; break; }
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String)
                    map[p.Name] = p.Value.GetString() ?? "";
        }
        catch { }
        return map;
    }

    private static void WriteClosePage(HttpListenerResponse res, bool ok)
    {
        string html = ok
            ? "<html><body style='font-family:sans-serif;background:#0A1726;color:#fff;text-align:center;padding-top:80px'><h2>✓ itsme confirmé</h2><p>Vous pouvez fermer cette fenêtre et revenir à IATECH-SHIELD.</p></body></html>"
            : "<html><body style='font-family:sans-serif;background:#0A1726;color:#fff;text-align:center;padding-top:80px'><h2>itsme annulé</h2><p>Vous pouvez fermer cette fenêtre.</p></body></html>";
        try
        {
            byte[] buf = Encoding.UTF8.GetBytes(html);
            res.ContentType = "text/html; charset=utf-8";
            res.ContentLength64 = buf.Length;
            res.OutputStream.Write(buf, 0, buf.Length);
            res.OutputStream.Close();
        }
        catch { }
    }
}
