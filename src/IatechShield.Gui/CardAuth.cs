using System.Security.Cryptography;
using System.Text;
using System.Windows;
using IatechShield.Tools;

namespace IatechShield.Gui;

/// <summary>
/// Authentification unique par carte d'identité (eID). Remplace tous les mots de passe
/// et codes PIN de l'application : la carte devient la clé. À la première utilisation,
/// l'empreinte de la carte (dérivée du numéro national) est mémorisée comme « carte
/// propriétaire ». Ensuite, seule cette carte, physiquement insérée, autorise les
/// actions ; une fois retirée, tout redevient inaccessible. Chaque tentative (réussie
/// ou refusée) est enregistrée dans l'historique des accès.
/// </summary>
public static class CardAuth
{
    private const string Vault = "cardauth";

    /// <summary>Vrai si une carte propriétaire a déjà été enregistrée.</summary>
    public static bool IsEnrolled =>
        !string.IsNullOrEmpty(SecretVault.Load(Vault).GetValueOrDefault("owner"));

    /// <summary>Nom du propriétaire enregistré (pour affichage), ou chaîne vide.</summary>
    public static string OwnerName => SecretVault.Load(Vault).GetValueOrDefault("ownername") ?? "";

    /// <summary>Empreinte stable d'une carte (numéro national haché en SHA-256).</summary>
    public static string Fingerprint(CardInfo card)
    {
        string basis = (card.NationalNumber ?? "").Trim();
        if (basis.Length == 0) basis = (card.Name + "|" + card.FirstNames + "|" + card.BirthDate).Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("IATECH-eID:" + basis)));
    }

    /// <summary>Réinitialise la carte propriétaire (à protéger derrière une vérification).</summary>
    public static void Reset() => SecretVault.Delete(Vault);

    /// <summary>
    /// Porte d'authentification : demande d'insérer la carte, la lit et vérifie qu'il
    /// s'agit bien de la carte propriétaire. Renvoie true si l'action est autorisée.
    /// La première carte valide insérée devient automatiquement la carte propriétaire.
    /// </summary>
    public static bool Gate(Window? owner, string action, out string identity)
    {
        identity = "";
        while (true)
        {
            var ask = MessageBox.Show(owner!,
                $"🪪 Insérez votre carte d'identité dans le lecteur pour :\n\n« {action} »\n\nPuis cliquez sur OK.",
                "IATECH-SHIELD — Carte d'identité requise",
                MessageBoxButton.OKCancel, MessageBoxImage.Information);
            if (ask != MessageBoxResult.OK)
            {
                AccessLog.Record("Accès annulé", "Carte d'identité (eID)", "—", action);
                return false;
            }

            CardInfo card;
            try { card = EidReader.Read(); }
            catch { card = new CardInfo(); }

            if (!card.CardPresent)
            {
                var retry = MessageBox.Show(owner!,
                    "Aucune carte détectée dans le lecteur.\n\nInsérez la carte puis cliquez sur « Oui » pour réessayer.",
                    "Carte d'identité", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (retry == MessageBoxResult.Yes) continue;
                AccessLog.Record("Accès refusé (aucune carte)", "Carte d'identité (eID)", "—", action);
                return false;
            }
            if (!card.IsBelgianEid)
            {
                MessageBox.Show(owner!, "Carte non reconnue : une carte d'identité électronique (eID) est requise.",
                    "Carte d'identité", MessageBoxButton.OK, MessageBoxImage.Warning);
                AccessLog.Record("Accès refusé (carte inconnue)", "Carte d'identité (eID)", card.DisplayIdentity, action);
                return false;
            }

            identity = card.DisplayIdentity;
            string fp = Fingerprint(card);
            var cfg = SecretVault.Load(Vault);
            string ownerFp = cfg.GetValueOrDefault("owner") ?? "";

            if (ownerFp.Length == 0)
            {
                // Première utilisation : cette carte devient la carte propriétaire.
                cfg["owner"] = fp;
                cfg["ownername"] = identity;
                SecretVault.Save(Vault, cfg);
                AccessLog.Record("Carte enregistrée (propriétaire)", "Carte d'identité (eID)", identity, action);
                MessageBox.Show(owner!,
                    $"Cette carte ({identity}) est désormais votre carte propriétaire.\n\n" +
                    "Elle seule pourra déverrouiller IATECH-SHIELD.",
                    "Carte enregistrée", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }

            if (string.Equals(fp, ownerFp, StringComparison.OrdinalIgnoreCase))
            {
                AccessLog.Record("Autorisation par carte", "Carte d'identité (eID)", identity, action);
                return true;
            }

            AccessLog.Record("Accès REFUSÉ (carte non autorisée)", "Carte d'identité (eID)", identity, action);
            MessageBox.Show(owner!,
                "Cette carte n'est pas la carte propriétaire enregistrée. Accès refusé.",
                "Accès refusé", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }
}
