using Android.App;
using Android.Content;
using Android.Content.PM;

namespace IatechShield.Mobile;

/// <summary>
/// Réceptionne la redirection OAuth (Google) vers iatechshield://callback et
/// la transmet à WebAuthenticator pour finaliser la connexion.
/// </summary>
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "iatechshield")]
public class WebAuthenticatorCallbackActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity
{
}
