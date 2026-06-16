#!/usr/bin/env python3
"""
Télécharge les factures et notes de crédit Amazon (par défaut année 2026).

À EXÉCUTER SUR VOTRE PROPRE MACHINE — ce script a besoin d'accéder à votre
session Amazon via un vrai navigateur. Il ne fonctionnera pas dans un
environnement cloud sans interface graphique.

Fonctionnement :
  1. Ouvre Chromium avec un profil persistant (./.amazon_profile).
  2. Au premier lancement, vous vous connectez À LA MAIN à Amazon
     (e-mail + mot de passe + éventuelle validation 2FA). La session est
     ensuite mémorisée pour les fois suivantes.
  3. Le script ouvre l'historique des commandes, filtre sur l'année demandée,
     ouvre la fenêtre « Facture » de chaque commande et télécharge tous les
     PDF (factures + notes de crédit) dans ./factures_<année>/.

Usage :
    python download_amazon_invoices.py                 # année 2026, amazon.com.be
    python download_amazon_invoices.py --year 2025
    python download_amazon_invoices.py --domain amazon.fr
    python download_amazon_invoices.py --headless      # une fois connecté

Prérequis :
    pip install playwright
    playwright install chromium
"""

import argparse
import re
import sys
import time
from pathlib import Path

try:
    from playwright.sync_api import sync_playwright, TimeoutError as PWTimeout
except ImportError:
    sys.exit(
        "Playwright n'est pas installé.\n"
        "  pip install playwright\n"
        "  playwright install chromium"
    )


def parse_args():
    p = argparse.ArgumentParser(description="Télécharge les factures Amazon d'une année.")
    p.add_argument("--year", type=int, default=2026, help="Année à récupérer (défaut: 2026)")
    p.add_argument("--domain", default="amazon.com.be",
                   help="Domaine Amazon (ex: amazon.com.be, amazon.fr, amazon.de)")
    p.add_argument("--out", default=None, help="Dossier de sortie (défaut: factures_<année>)")
    p.add_argument("--profile", default=".amazon_profile",
                   help="Dossier du profil navigateur persistant")
    p.add_argument("--headless", action="store_true",
                   help="Navigateur invisible (à utiliser seulement une fois connecté)")
    return p.parse_args()


def sanitize(name: str) -> str:
    return re.sub(r"[^\w\-.]+", "_", name).strip("_")[:120]


def wait_for_login(page, domain):
    """Attend que l'utilisateur soit connecté (présence du menu compte)."""
    print("\n>>> Si la page de connexion s'affiche, connectez-vous manuellement.")
    print(">>> Le script attend que vous soyez connecté...\n")
    for _ in range(600):  # ~10 minutes max
        try:
            if page.locator("#nav-link-accountList").count() > 0:
                # Heuristique : connecté si le lien ne propose plus "S'identifier" seul
                txt = page.locator("#nav-link-accountList").inner_text(timeout=2000).lower()
                if "identifie" not in txt and "sign in" not in txt and "hello" in txt.lower() or "bonjour" in txt:
                    return True
                # Fallback : présence d'un greeting personnalisé
                if page.locator("#nav-link-accountList-nav-line-1").count() > 0:
                    line = page.locator("#nav-link-accountList-nav-line-1").inner_text(timeout=2000)
                    if line and "identifi" not in line.lower() and "sign in" not in line.lower():
                        return True
        except Exception:
            pass
        time.sleep(1)
    return False


def get_order_history_url(domain, year):
    # orderFilter=year-XXXX filtre directement sur l'année
    return f"https://www.{domain}/gp/css/order-history?orderFilter=year-{year}&ref_=ppx_yo2ov_dt_b_filter_all_y{year}"


def main():
    args = parse_args()
    out_dir = Path(args.out or f"factures_{args.year}")
    out_dir.mkdir(parents=True, exist_ok=True)
    profile_dir = Path(args.profile).resolve()

    print(f"Domaine      : {args.domain}")
    print(f"Année        : {args.year}")
    print(f"Sortie       : {out_dir.resolve()}")
    print(f"Profil navig.: {profile_dir}")

    with sync_playwright() as pw:
        ctx = pw.chromium.launch_persistent_context(
            user_data_dir=str(profile_dir),
            headless=args.headless,
            accept_downloads=True,
            viewport={"width": 1280, "height": 900},
        )
        page = ctx.pages[0] if ctx.pages else ctx.new_page()

        # 1) Connexion
        page.goto(f"https://www.{args.domain}/", wait_until="domcontentloaded")
        if not args.headless:
            if not wait_for_login(page, args.domain):
                print("Connexion non détectée — fermeture.")
                ctx.close()
                return

        # 2) Historique des commandes filtré sur l'année
        page.goto(get_order_history_url(args.domain, args.year),
                  wait_until="domcontentloaded")
        time.sleep(2)

        downloaded = 0
        seen_pages = 0
        while True:
            seen_pages += 1
            # Sélecteurs des cartes de commande (Amazon en a plusieurs variantes)
            cards = page.locator(".order-card, .order, .js-order-card")
            n = cards.count()
            print(f"\nPage {seen_pages} : {n} commande(s) détectée(s).")

            # Cherche tous les liens "Facture / Invoice" de la page
            invoice_triggers = page.locator(
                "a:has-text('Facture'), a:has-text('Invoice'), "
                "span:has-text('Facture'), a:has-text('Note de crédit'), "
                "a:has-text('Credit note')"
            )

            # On collecte les liens PDF directement présents dans la page après
            # avoir ouvert chaque popover de facture.
            triggers_count = invoice_triggers.count()
            for i in range(triggers_count):
                try:
                    trig = invoice_triggers.nth(i)
                    trig.scroll_into_view_if_needed(timeout=3000)
                    trig.click(timeout=3000)
                    time.sleep(1)
                except Exception:
                    continue

                # Liens PDF apparus (popover ou nouvelle section)
                pdf_links = page.locator(
                    "a[href*='invoice'], a[href*='Facture'], a[href$='.pdf'], "
                    "a[href*='generated_invoices'], a[href*='creditNote']"
                )
                for j in range(pdf_links.count()):
                    href = pdf_links.nth(j).get_attribute("href") or ""
                    if not href:
                        continue
                    if not href.startswith("http"):
                        href = f"https://www.{args.domain}{href}"
                    try:
                        with page.expect_download(timeout=15000) as dl_info:
                            page.evaluate("(u)=>window.location.assign(u)", href)
                        dl = dl_info.value
                        fname = sanitize(dl.suggested_filename or f"facture_{downloaded}.pdf")
                        if not fname.lower().endswith(".pdf"):
                            fname += ".pdf"
                        dl.save_as(str(out_dir / fname))
                        downloaded += 1
                        print(f"  ✓ {fname}")
                    except PWTimeout:
                        # Pas un téléchargement direct : ouvre dans un onglet et imprime
                        try:
                            inv_page = ctx.new_page()
                            inv_page.goto(href, wait_until="networkidle", timeout=20000)
                            pdf_path = out_dir / f"facture_{args.year}_{downloaded:03d}.pdf"
                            inv_page.pdf(path=str(pdf_path))
                            downloaded += 1
                            print(f"  ✓ {pdf_path.name} (rendu HTML→PDF)")
                            inv_page.close()
                        except Exception as e:
                            print(f"  ✗ échec sur {href[:80]} : {e}")
                    except Exception as e:
                        print(f"  ✗ échec sur {href[:80]} : {e}")

            # Pagination : bouton "Suivant" / "Next"
            nxt = page.locator("li.a-last a, a:has-text('Suivant'), a:has-text('Next')")
            if nxt.count() > 0 and nxt.first.is_enabled():
                try:
                    nxt.first.click()
                    page.wait_for_load_state("domcontentloaded")
                    time.sleep(2)
                    continue
                except Exception:
                    break
            break

        print(f"\nTerminé : {downloaded} fichier(s) téléchargé(s) dans {out_dir.resolve()}")
        if not args.headless:
            input("Appuyez sur Entrée pour fermer le navigateur...")
        ctx.close()


if __name__ == "__main__":
    main()
