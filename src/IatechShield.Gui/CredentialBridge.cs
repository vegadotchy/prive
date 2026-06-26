using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace IatechShield.Gui;

/// <summary>
/// Petit serveur local (127.0.0.1 uniquement) qui reçoit de l'extension de
/// navigateur les identifiants saisis dans une page, afin de proposer à
/// l'utilisateur de les enregistrer dans le coffre-fort. Aucune donnée ne sort
/// du PC : le canal est strictement local.
/// </summary>
public sealed class CredentialBridge
{
    public const int Port = 38217;

    /// <summary>(url, identifiant, mot de passe) — déclenché sur le thread UI.</summary>
    public event Action<string, string, string>? CredentialReceived;

    private readonly Dispatcher _dispatcher;
    private HttpListener? _listener;

    public CredentialBridge(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public bool Start()
    {
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _ = Task.Run(LoopAsync);
            return true;
        }
        catch
        {
            _listener = null;
            return false;
        }
    }

    public void Stop()
    {
        try { _listener?.Stop(); _listener?.Close(); } catch { }
        _listener = null;
    }

    private async Task LoopAsync()
    {
        while (_listener is { IsListening: true })
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { break; }
            try { Handle(ctx); } catch { /* requête malformée ignorée */ }
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        var res = ctx.Response;
        res.AddHeader("Access-Control-Allow-Origin", "*");
        res.AddHeader("Access-Control-Allow-Methods", "POST, OPTIONS");
        res.AddHeader("Access-Control-Allow-Headers", "Content-Type");

        if (ctx.Request.HttpMethod == "OPTIONS") { res.StatusCode = 204; res.Close(); return; }

        if (ctx.Request.HttpMethod == "POST" && ctx.Request.Url?.AbsolutePath == "/save")
        {
            string body;
            using (var r = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                body = r.ReadToEnd();
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                string Get(string k) => root.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";
                string url = Get("url"), user = Get("username"), pass = Get("password");
                if (!string.IsNullOrEmpty(pass))
                    _dispatcher.BeginInvoke(() => CredentialReceived?.Invoke(url, user, pass));
            }
            catch { /* JSON invalide */ }

            byte[] ok = Encoding.UTF8.GetBytes("{\"ok\":true}");
            res.ContentType = "application/json";
            res.OutputStream.Write(ok, 0, ok.Length);
        }
        else
        {
            res.StatusCode = 404;
        }
        res.Close();
    }
}
