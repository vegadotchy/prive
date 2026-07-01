using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using IatechShield.Tools;

namespace IatechShield.Gui;

/// <summary>
/// Authentification par carte d'identité (eID). La carte remplace tous les mots de passe
/// et codes PIN. Modèle multi-cartes : toute carte d'identité insérée déverrouille et
/// obtient les mêmes fonctionnalités ; chaque nouvelle carte est automatiquement ajoutée
/// à la liste des cartes autorisées. Une fois la carte retirée, tout redevient
/// inaccessible. Chaque tentative (réussie ou refusée) est tracée dans l'historique.
/// </summary>
public static class CardAuth
{
    private const string Vault = "cardauth";
    private const string Prefix = "card_";   // clés « card_<empreinte> » = nom du porteur

    /// <summary>Vrai si au moins une carte est enregistrée.</summary>
    public static bool IsEnrolled => SecretVault.Load(Vault).Keys.Any(k => k.StartsWith(Prefix));

    /// <summary>Nom de la première carte enregistrée (pour affichage), ou chaîne vide.</summary>
    public static string OwnerName
    {
        get
        {
            var cfg = SecretVault.Load(Vault);
            var first = cfg.FirstOrDefault(kv => kv.Key.StartsWith(Prefix));
            return first.Value ?? "";
        }
    }

    /// <summary>Nombre de cartes autorisées.</summary>
    public static int AuthorizedCount => SecretVault.Load(Vault).Keys.Count(k => k.StartsWith(Prefix));

    /// <summary>Empreinte stable d'une carte (numéro national haché en SHA-256).</summary>
    public static string Fingerprint(CardInfo card)
    {
        string basis = (card.NationalNumber ?? "").Trim();
        if (basis.Length == 0) basis = (card.Name + "|" + card.FirstNames + "|" + card.BirthDate).Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("IATECH-eID:" + basis)));
    }

    /// <summary>Vrai si cette carte fait déjà partie des cartes autorisées.</summary>
    public static bool IsAuthorized(CardInfo card) =>
        SecretVault.Load(Vault).ContainsKey(Prefix + Fingerprint(card));

    /// <summary>Ajoute une carte à la liste des cartes autorisées (si nouvelle).</summary>
    public static bool Register(CardInfo card, out bool isNew)
    {
        isNew = false;
        if (!card.IsBelgianEid) return false;
        var cfg = SecretVault.Load(Vault);
        string key = Prefix + Fingerprint(card);
        if (!cfg.ContainsKey(key))
        {
            cfg[key] = card.DisplayIdentity;
            SecretVault.Save(Vault, cfg);
            isNew = true;
        }
        return true;
    }

    /// <summary>Supprime toutes les cartes enregistrées.</summary>
    public static void Reset() => SecretVault.Delete(Vault);

    /// <summary>
    /// Porte d'authentification : demande d'insérer une carte, la lit et l'accepte.
    /// Toute carte d'identité (eID) valide déverrouille ; une nouvelle carte est ajoutée
    /// automatiquement à la liste des cartes autorisées (mêmes fonctionnalités).
    /// </summary>
    public static bool Gate(Window? owner, string action, out string identity)
    {
        identity = "";
        while (true)
        {
            var ask = MessageBox.Show(owner!,
                $"🪪 Insérez une carte d'identité dans le lecteur pour :\n\n« {action} »\n\nPuis cliquez sur OK.",
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
            Register(card, out bool isNew);
            AccessLog.Record(isNew ? "Carte ajoutée (autorisée)" : "Autorisation par carte",
                "Carte d'identité (eID)", identity, action);
            if (isNew)
                MessageBox.Show(owner!,
                    $"Nouvelle carte autorisée : {identity}.\nElle a désormais les mêmes fonctionnalités.",
                    "Carte ajoutée", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
    }
}
