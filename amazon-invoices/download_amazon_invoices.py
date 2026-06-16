#!/usr/bin/env python3
"""
Télécharge les factures et notes de crédit Amazon (par défaut année 2026).

À EXÉCUTER SUR VOTRE PROPRE MACHINE — accès à votre session Amazon via un vrai
navigateur requis.

Usage :
    python download_amazon_invoices.py                 # 2026, amazon.com.be
    python download_amazon_invoices.py --year 2025
    python download_amazon_invoices.py --domain amazon.fr
    python download_amazon_invoices.py --include-summaries   # aussi les récap.
    python download_amazon_invoices.py --debug              # logs détaillés

Prérequis :
    pip install playwright
    python -m playwright install chromium
"""

import argparse
import re
import sys
import time
from pathlib import Path

try:
    from playwright.sync_api import sync_playwright
except ImportError:
    sys.exit("Playwright manquant : pip install playwright  puis  python -m playwright install chromium")


def parse_args():
    p = argparse.ArgumentParser(description="Télécharge les factures Amazon d'une année.")
    p.add_argument("--year", type=int, default=2026)
    p.add_argument("--domain", default="amazon.com.be")
    p.add_argument("--out", default=None)
    p.add_argument("--profile", default=".amazon_profile")
    p.add_argument("--headless", action="store_true")
    p.add_argument("--include-summaries", action="store_true",
                   help="Télécharge aussi les récapitulatifs de commande")
    p.add_argument("--debug", action="store_true")
    return p.parse_args()


def sanitize(name: str) -> str:
    name = re.sub(r"\s+", "_", name.strip())
    name = re.sub(r"[^\w\-.]+", "", name)
    return name.strip("_")[:80] or "document"


def is_summary(text: str) -> bool:
    t = text.lower()
    return any(k in t for k in ("récapitulatif", "recapitulatif", "summary", "order summary"))


def is_invoice_like(text: str) -> bool:
    t = text.lower()
    return any(k in t for k in ("facture", "invoice", "note de crédit", "note de credit",
                                "credit note", "avoir"))


def main():
    args = parse_args()
    out_dir = Path(args.out or f"factures_{args.year}")
    out_dir.mkdir(parents=True, exist_ok=True)
    profile_dir = Path(args.profile).resolve()

    def log(*a):
        if args.debug:
            print("   [debug]", *a)

    print(f"Domaine: {args.domain} | Année: {args.year} | Sortie: {out_dir.resolve()}")

    with sync_playwright() as pw:
        ctx = pw.chromium.launch_persistent_context(
            user_data_dir=str(profile_dir),
            headless=args.headless,
            accept_downloads=True,
            viewport={"width": 1280, "height": 950},
        )
        page = ctx.pages[0] if ctx.pages else ctx.new_page()

        # --- Connexion ---
        page.goto(f"https://www.{args.domain}/", wait_until="domcontentloaded")
        if not args.headless:
            print("\n>>> Connectez-vous à Amazon dans la fenêtre du navigateur si besoin.")
            print(">>> Le script démarre dès qu'il détecte la page des commandes.\n")

        # --- Collecte des liens de factures, commande par commande ---
        collected = []   # liste de (text, href)
        seen_href = set()
        page_no = 0

        url = (f"https://www.{args.domain}/gp/css/order-history"
               f"?orderFilter=year-{args.year}")
        page.goto(url, wait_until="domcontentloaded")
        time.sleep(2)

        # Si redirigé vers une page de login, on attend l'utilisateur
        for _ in range(600):
            if "/ap/signin" in page.url or "signin" in page.url:
                time.sleep(1)
                continue
            break
        if "/ap/signin" in page.url:
            page.goto(url, wait_until="domcontentloaded")
            time.sleep(2)

        while True:
            page_no += 1
            cards = page.locator(".order-card, li.order-card, .js-order-card, .order")
            n = cards.count()
            print(f"\nPage {page_no} : {n} commande(s).")

            for ci in range(n):
                card = cards.nth(ci)
                # bouton/lien "Facture" dans cette commande
                trigger = card.locator(
                    "a:has-text('Facture'), span:has-text('Facture'), "
                    "a:has-text('Invoice'), span:has-text('Invoice')"
                ).first
                if trigger.count() == 0:
                    log(f"commande {ci}: pas de bouton Facture")
                    continue
                try:
                    trigger.scroll_into_view_if_needed(timeout=3000)
                    trigger.click(timeout=4000)
                except Exception as e:
                    log(f"commande {ci}: clic impossible ({e})")
                    continue
                time.sleep(1.2)

                # liens dans le popover ouvert
                popover_links = page.locator(".a-popover-content a, .a-popover a")
                pcount = popover_links.count()
                links_here = []
                if pcount > 0:
                    for li in range(pcount):
                        try:
                            a = popover_links.nth(li)
                            txt = (a.inner_text(timeout=1500) or "").strip()
                            href = a.get_attribute("href", timeout=1500) or ""
                        except Exception:
                            continue
                        if href:
                            links_here.append((txt, href))
                else:
                    # pas de popover : liens directs dans la carte
                    inner = card.locator("a")
                    for li in range(min(inner.count(), 30)):
                        try:
                            a = inner.nth(li)
                            txt = (a.inner_text(timeout=800) or "").strip()
                            href = a.get_attribute("href", timeout=800) or ""
                        except Exception:
                            continue
                        if href and is_invoice_like(txt):
                            links_here.append((txt, href))

                for txt, href in links_here:
                    if not href.startswith("http"):
                        href = f"https://www.{args.domain}{href}"
                    if href in seen_href:
                        continue
                    keep = is_invoice_like(txt) or (args.include_summaries and is_summary(txt))
                    # certains liens "Facture" ont un texte vide mais une URL parlante
                    if not keep and any(k in href.lower() for k in
                                        ("invoice", "creditnote", "credit_note")):
                        keep = True
                    if keep:
                        seen_href.add(href)
                        collected.append((txt or "facture", href))
                        log(f"commande {ci}: + '{txt}' -> {href[:90]}")

                # fermer le popover
                try:
                    page.keyboard.press("Escape")
                    time.sleep(0.3)
                except Exception:
                    pass

            # pagination
            nxt = page.locator("ul.a-pagination li.a-last a, a:has-text('Suivant'), a:has-text('Next')")
            if nxt.count() > 0:
                try:
                    nxt.first.click(timeout=4000)
                    page.wait_for_load_state("domcontentloaded")
                    time.sleep(2)
                    continue
                except Exception:
                    break
            break

        print(f"\n{len(collected)} lien(s) de document(s) trouvé(s). Téléchargement...\n")

        # --- Téléchargement via la session (cookies) ---
        downloaded = 0
        for idx, (txt, href) in enumerate(collected):
            base = f"{args.year}_{idx:03d}_{sanitize(txt)}"
            try:
                resp = ctx.request.get(href, timeout=30000)
                ctype = (resp.headers.get("content-type") or "").lower()
                body = resp.body()
                if "pdf" in ctype or body[:4] == b"%PDF":
                    (out_dir / f"{base}.pdf").write_bytes(body)
                    print(f"  ✓ {base}.pdf")
                    downloaded += 1
                else:
                    # page HTML -> rendu PDF via le navigateur
                    inv = ctx.new_page()
                    inv.goto(href, wait_until="networkidle", timeout=25000)
                    inv.pdf(path=str(out_dir / f"{base}.pdf"))
                    inv.close()
                    print(f"  ✓ {base}.pdf (HTML→PDF)")
                    downloaded += 1
            except Exception as e:
                print(f"  ✗ {base} : {e}")

        print(f"\nTerminé : {downloaded}/{len(collected)} fichier(s) dans {out_dir.resolve()}")
        if not args.headless:
            input("Appuyez sur Entrée pour fermer le navigateur...")
        ctx.close()


if __name__ == "__main__":
    main()
