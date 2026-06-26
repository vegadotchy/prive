using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Authentication;
using Microsoft.Maui.Storage;

namespace IatechShield.Mobile.Services;

public sealed record CloudUser(string Id, string Email);

public sealed record CloudLicense(string LicenseKey, string Tier, DateTimeOffset? ExpiresAt);

/// <summary>
/// Compte client en ligne (Supabase) pour l'app mobile : connexion par e-mail ou
/// Google, et récupération de la licence liée au compte (le même casier que la
/// version Windows). La session est stockée de façon chiffrée (SecureStorage).
/// </summary>
public sealed class CloudAccount
{
    // Mêmes constantes que la version Windows (clé publique : sans danger côté client).
    public const string SupabaseUrl = "https://vkfbjeudivczvldhvcsz.supabase.co";
    public const string AnonKey = "sb_publishable_jqwrWsH615rTVPQkGtUn9g_egRuAdMr";

    // Schéma de redirection OAuth (déclaré dans le manifeste Android).
    public const string CallbackUrl = "iatechshield://callback";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private string? _accessToken;
    private string? _refreshToken;

    public CloudUser? User { get; private set; }
    public bool IsSignedIn => User is not null && !string.IsNullOrEmpty(_accessToken);

    // ----------------------------------------------------------- e-mail ------

    public Task<string?> SignUpEmailAsync(string email, string password)
        => AuthPasswordAsync("/auth/v1/signup", email, password);

    public Task<string?> SignInEmailAsync(string email, string password)
        => AuthPasswordAsync("/auth/v1/token?grant_type=password", email, password);

    private async Task<string?> AuthPasswordAsync(string path, string email, string password)
    {
        try
        {
            string body = JsonSerializer.Serialize(new { email = email.Trim(), password });
            using var req = NewRequest(HttpMethod.Post, path, bearer: AnonKey);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var resp = await Http.SendAsync(req);
            string json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return FriendlyError(json);
            return await StoreFromTokenJsonAsync(json)
                ? null
                : "Compte créé. Vérifiez votre e-mail pour confirmer, puis connectez-vous.";
        }
        catch (Exception ex)
        {
            return "Connexion au serveur impossible : " + ex.Message;
        }
    }

    // ----------------------------------------------------------- Google ------

    public async Task<string?> SignInWithGoogleAsync()
    {
        try
        {
            string authUrl = $"{SupabaseUrl}/auth/v1/authorize?provider=google" +
                             $"&redirect_to={Uri.EscapeDataString(CallbackUrl)}";

            var result = await WebAuthenticator.Default.AuthenticateAsync(
                new Uri(authUrl), new Uri(CallbackUrl));

            string? access = Get(result, "access_token");
            string? refresh = Get(result, "refresh_token");
            if (string.IsNullOrEmpty(access))
                return "Connexion Google annulée.";

            _accessToken = access;
            _refreshToken = refresh;
            await FetchAndStoreUserAsync();
            await PersistAsync();
            return null;
        }
        catch (TaskCanceledException)
        {
            return "Connexion Google annulée.";
        }
        catch (Exception ex)
        {
            return "Échec de la connexion Google : " + ex.Message;
        }
    }

    private static string? Get(WebAuthenticatorResult r, string key)
        => r.Properties.TryGetValue(key, out var v) ? v : null;

    // ------------------------------------------------- session persistante ---

    public async Task<bool> RestoreAsync()
    {
        string? refresh = await SecureStorage.Default.GetAsync("cloud_refresh");
        if (string.IsNullOrWhiteSpace(refresh)) return false;
        try
        {
            string body = JsonSerializer.Serialize(new { refresh_token = refresh });
            using var req = NewRequest(HttpMethod.Post, "/auth/v1/token?grant_type=refresh_token", bearer: AnonKey);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var resp = await Http.SendAsync(req);
            string json = await resp.Content.ReadAsStringAsync();
            return resp.IsSuccessStatusCode && await StoreFromTokenJsonAsync(json);
        }
        catch
        {
            return false;
        }
    }

    public void SignOut()
    {
        User = null;
        _accessToken = _refreshToken = null;
        try { SecureStorage.Default.Remove("cloud_refresh"); } catch { }
    }

    // ------------------------------------------------- casier de licence -----

    public async Task<CloudLicense?> FetchLicenseAsync()
    {
        if (!IsSignedIn) return null;
        try
        {
            using var req = NewRequest(HttpMethod.Get,
                "/rest/v1/shield_licenses?select=license_key,tier,expires_at", bearer: _accessToken!);
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return null;
            var row = doc.RootElement[0];
            DateTimeOffset? exp = row.TryGetProperty("expires_at", out var e) && e.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(e.GetString(), out var d) ? d : null;
            return new CloudLicense(
                row.GetProperty("license_key").GetString() ?? "",
                row.TryGetProperty("tier", out var t) ? t.GetString() ?? "" : "",
                exp);
        }
        catch
        {
            return null;
        }
    }

    // --------------------------------------------------------------- utils ---

    private HttpRequestMessage NewRequest(HttpMethod method, string path, string bearer)
    {
        var req = new HttpRequestMessage(method, SupabaseUrl + path);
        req.Headers.TryAddWithoutValidation("apikey", AnonKey);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return req;
    }

    private async Task<bool> StoreFromTokenJsonAsync(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("access_token", out var at) || at.ValueKind != JsonValueKind.String)
            return false;
        _accessToken = at.GetString();
        _refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;

        if (root.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object)
        {
            string id = user.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
            string email = user.TryGetProperty("email", out var em) ? em.GetString() ?? "" : "";
            User = new CloudUser(id, email);
        }
        else
        {
            await FetchAndStoreUserAsync();
        }
        await PersistAsync();
        return true;
    }

    private async Task FetchAndStoreUserAsync()
    {
        try
        {
            using var req = NewRequest(HttpMethod.Get, "/auth/v1/user", bearer: _accessToken!);
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            string id = root.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
            string email = root.TryGetProperty("email", out var em) ? em.GetString() ?? "" : "";
            User = new CloudUser(id, email);
        }
        catch { /* identité non critique */ }
    }

    private async Task PersistAsync()
    {
        if (!string.IsNullOrEmpty(_refreshToken))
            await SecureStorage.Default.SetAsync("cloud_refresh", _refreshToken!);
    }

    private static string FriendlyError(string json)
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
            if (low.Contains("already registered")) return "Un compte existe déjà avec cet e-mail.";
            if (low.Contains("password")) return "Mot de passe trop court (6 caractères minimum).";
            if (low.Contains("not confirmed")) return "E-mail non confirmé : cliquez sur le lien reçu par mail.";
            return string.IsNullOrWhiteSpace(msg) ? "Erreur d'authentification." : msg;
        }
        catch
        {
            return "Erreur d'authentification.";
        }
    }
}
