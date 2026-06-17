#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Téléchargement des factures ENGIE Belgique (espace client Pro) — année 2025.

Ce script ouvre un vrai navigateur Chromium avec un profil PERSISTANT.
=> Tu te connectes UNE SEULE FOIS à la main (2FA / captcha inclus).
Ensuite, il parcourt chaque adresse / contrat, filtre 2025, et télécharge
les PDF dans engie_pdf/<adresse>/.

------------------------------------------------------------------------------
PRÉREQUIS (à faire une fois sur ta machine) :

    pip install playwright
    playwright install chromium

LANCEMENT :

    python download_engie_factures.py

------------------------------------------------------------------------------
IMPORTANT — sélecteurs :
Le site ENGIE est derrière un login, je n'ai pas pu inspecter son HTML exact.
Les sélecteurs ci-dessous (section CONFIG) sont des "meilleures suppositions".
Si le script ne trouve pas les bons boutons, lance-le en MODE_MANUEL = True :
il fera tout SAUF cliquer automatiquement — tu navigues toi-même vers la liste
des factures et il interceptera/téléchargera chaque PDF que la page ouvre.
Tu peux aussi corriger les sélecteurs après inspection (clic droit > Inspecter).
------------------------------------------------------------------------------
"""

import re
import time
from pathlib import Path
from playwright.sync_api import sync_playwright, TimeoutError as PWTimeout

# ============================== CONFIG ========================================

START_URL = "https://www.engie.be/fr/professionals/espace-client"
ANNEE = "2025"
OUTPUT_DIR = Path(__file__).parent / "engie_pdf"
PROFILE_DIR = Path(__file__).parent / ".chrome_profile"  # garde ta session

# Mets True pour la 1re exécution si tu n'es pas sûr des sélecteurs.
# Le script attend que TU navigues vers la liste des factures, puis intercepte
# tous les PDF (téléchargements et nouveaux onglets).
MODE_MANUEL = False

# --- Sélecteurs (à ajuster après inspection si besoin) ---
# Sélecteur du menu déroulant / liste qui permet de changer d'adresse-contrat.
SEL_SELECTEUR_ADRESSE = "select[name*='contract'], select[id*='contract'], [data-testid*='address-selector']"

# Lien / onglet vers la page des factures.
SEL_LIEN_FACTURES = "a:has-text('Factures'), a:has-text('Mes factures'), [href*='invoice'], [href*='facture']"

# Filtre d'année (optionnel).
SEL_FILTRE_ANNEE = "select[name*='year'], select[id*='year'], [data-testid*='year']"

# Boutons / liens de téléchargement PDF d'une facture (un par ligne).
SEL_BTN_PDF = (
    "a[href$='.pdf'], a[href*='pdf'], "
    "button:has-text('PDF'), a:has-text('PDF'), "
    "a:has-text('Télécharger'), button:has-text('Télécharger'), "
    "[data-testid*='download'], [aria-label*='télécharger' i]"
)

# =============================================================================


def slugify(text: str) -> str:
    text = (text or "adresse").strip().lower()
    text = re.sub(r"[^a-z0-9]+", "_", text)
    return text.strip("_")[:60] or "adresse"


def attente_login(page):
    print("\n" + "=" * 70)
    print(" Connecte-toi à ENGIE dans la fenêtre du navigateur qui vient")
    print(" de s'ouvrir (login + 2FA éventuel).")
    print(" Quand tu es sur ton espace client, reviens ici et appuie sur Entrée.")
    print("=" * 70)
    input(" >>> Appuie sur Entrée une fois connecté... ")


def setup_interception_pdf(context, dossier: Path):
    """Capture les PDF ouverts dans un nouvel onglet (au lieu d'un download)."""
    def on_page(new_page):
        try:
            new_page.wait_for_load_state("domcontentloaded", timeout=15000)
            url = new_page.url
            if "pdf" in url.lower():
                resp = context.request.get(url)
                nom = url.split("/")[-1].split("?")[0] or f"facture_{int(time.time())}.pdf"
                if not nom.lower().endswith(".pdf"):
                    nom += ".pdf"
                (dossier / nom).write_bytes(resp.body())
                print(f"   [onglet→pdf] {nom}")
                new_page.close()
        except Exception as e:
            print(f"   (interception onglet ignorée: {e})")
    context.on("page", on_page)


def telecharger_pdfs_page(page, dossier: Path):
    """Clique chaque bouton PDF de la page courante et sauve le download."""
    dossier.mkdir(parents=True, exist_ok=True)
    boutons = page.locator(SEL_BTN_PDF)
    n = boutons.count()
    print(f"   {n} bouton(s) PDF détecté(s) sur la page.")
    count = 0
    for i in range(n):
        btn = boutons.nth(i)
        try:
            with page.expect_download(timeout=20000) as dl_info:
                btn.click()
            dl = dl_info.value
            nom = dl.suggested_filename or f"facture_{i+1}.pdf"
            dl.save_as(str(dossier / nom))
            print(f"   [ok] {nom}")
            count += 1
        except PWTimeout:
            # Le clic a peut-être ouvert un onglet (géré par l'interception).
            print(f"   [info] bouton {i+1}: pas de download direct (peut-être onglet).")
        except Exception as e:
            print(f"   [warn] bouton {i+1}: {e}")
    return count


def main():
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    with sync_playwright() as p:
        context = p.chromium.launch_persistent_context(
            user_data_dir=str(PROFILE_DIR),
            headless=False,
            accept_downloads=True,
            viewport={"width": 1400, "height": 900},
        )
        page = context.pages[0] if context.pages else context.new_page()
        page.goto(START_URL, wait_until="domcontentloaded")

        attente_login(page)
        setup_interception_pdf(context, OUTPUT_DIR / "_divers")

        # --- MODE MANUEL : tu navigues, le script intercepte les PDF ---
        if MODE_MANUEL:
            print("\nMODE MANUEL activé.")
            print("Navigue vers tes factures et ouvre/télécharge chaque PDF.")
            print("Le script sauve automatiquement dans:", OUTPUT_DIR)
            input(" >>> Appuie sur Entrée quand tu as terminé pour fermer... ")
            context.close()
            return

        # --- MODE AUTO ---
        # 1) Aller sur la page Factures
        try:
            page.locator(SEL_LIEN_FACTURES).first.click(timeout=8000)
            page.wait_for_load_state("networkidle", timeout=20000)
        except Exception:
            print("Lien 'Factures' non trouvé automatiquement.")
            print("Navigue manuellement vers la page des factures, puis Entrée.")
            input(" >>> Entrée pour continuer... ")

        # 2) Récupérer la liste des adresses/contrats
        adresses = []
        try:
            sel = page.locator(SEL_SELECTEUR_ADRESSE).first
            if sel.count() > 0:
                options = sel.locator("option")
                for i in range(options.count()):
                    val = options.nth(i).get_attribute("value")
                    label = (options.nth(i).inner_text() or "").strip()
                    if val:
                        adresses.append((val, label))
        except Exception as e:
            print("Sélecteur d'adresse non détecté:", e)

        if not adresses:
            print("\nUne seule adresse (ou sélecteur non détecté).")
            print("Traitement de l'adresse actuellement affichée.")
            adresses = [(None, "adresse_courante")]

        print(f"\n{len(adresses)} adresse(s) à traiter.")

        # 3) Pour chaque adresse : sélectionner, filtrer 2025, télécharger
        total = 0
        for idx, (val, label) in enumerate(adresses, 1):
            print(f"\n--- Adresse {idx}/{len(adresses)} : {label} ---")
            dossier = OUTPUT_DIR / f"{idx:02d}_{slugify(label)}"

            if val is not None:
                try:
                    page.locator(SEL_SELECTEUR_ADRESSE).first.select_option(val)
                    page.wait_for_load_state("networkidle", timeout=15000)
                except Exception as e:
                    print("   (changement d'adresse échoué:", e, ")")

            # Filtre année si présent
            try:
                fy = page.locator(SEL_FILTRE_ANNEE).first
                if fy.count() > 0:
                    fy.select_option(label=ANNEE)
                    page.wait_for_load_state("networkidle", timeout=15000)
                    print(f"   Filtre {ANNEE} appliqué.")
            except Exception:
                print(f"   (pas de filtre année — penser à ne garder que {ANNEE})")

            setup_interception_pdf(context, dossier)
            total += telecharger_pdfs_page(page, dossier)

        print(f"\n=== Terminé. {total} PDF téléchargé(s) dans {OUTPUT_DIR} ===")
        input(" >>> Entrée pour fermer le navigateur... ")
        context.close()


if __name__ == "__main__":
    main()
