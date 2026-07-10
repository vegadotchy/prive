# -*- coding: utf-8 -*-
"""
IATech Trading Bot — Bot de trading Binance (Spot) avec interface graphique.

Modes :
  - Paper  : simulation locale (aucun ordre réel, aucun risque)
  - Réel   : ordres au marché sur Binance Spot (clés API requises)

Stratégie : croisement de moyennes mobiles (SMA rapide / SMA lente) sur
bougies 1 minute, complétée par take-profit, stop-loss, trailing stop,
cooldown entre trades et limite de perte journalière.

Sécurité threads : le bot tourne dans un thread séparé qui ne touche JAMAIS
à tkinter. La configuration est figée dans un dict au démarrage et toutes
les mises à jour repassent par une queue lue par le thread principal.
"""

import base64
import csv
import hashlib
import hmac
import json
import os
import queue
import threading
import time
import tkinter as tk
import urllib.error
import urllib.parse
import urllib.request
from collections import deque
from datetime import datetime, date
from tkinter import filedialog, messagebox, simpledialog, ttk

APP_NAME = "IATech Trading Bot"
APP_VERSION = "2.0.0"
BINANCE_API = "https://api.binance.com"

CONFIG_DIR = os.path.join(os.environ.get("APPDATA", os.path.expanduser("~")), "IATechBot")
CONFIG_FILE = os.path.join(CONFIG_DIR, "config.json")
STATE_FILE = os.path.join(CONFIG_DIR, "state.json")

# ---------------------------------------------------------------------------
# Chiffrement de la configuration (code d'accès)
# ---------------------------------------------------------------------------


def _derive_key(code: str, salt: bytes) -> bytes:
    return hashlib.pbkdf2_hmac("sha256", code.encode("utf-8"), salt, 200_000)


def _keystream(key: bytes, length: int) -> bytes:
    out = b""
    counter = 0
    while len(out) < length:
        out += hashlib.sha256(key + counter.to_bytes(8, "big")).digest()
        counter += 1
    return out[:length]


def encrypt_text(plain: str, key: bytes) -> str:
    data = plain.encode("utf-8")
    stream = _keystream(key, len(data))
    return base64.b64encode(bytes(a ^ b for a, b in zip(data, stream))).decode("ascii")


def decrypt_text(cipher_b64: str, key: bytes) -> str:
    data = base64.b64decode(cipher_b64)
    stream = _keystream(key, len(data))
    return bytes(a ^ b for a, b in zip(data, stream)).decode("utf-8")


class ConfigStore:
    """Configuration persistée, clés API chiffrées par un code d'accès."""

    def __init__(self):
        self.key = None
        self.data = {}

    def exists(self) -> bool:
        return os.path.isfile(CONFIG_FILE)

    def create(self, code: str):
        salt = os.urandom(16)
        self.key = _derive_key(code, salt)
        self.data = {
            "salt": base64.b64encode(salt).decode("ascii"),
            "verifier": hashlib.sha256(self.key + b"verifier").hexdigest(),
            "settings": {},
        }
        self.save()

    def unlock(self, code: str) -> bool:
        with open(CONFIG_FILE, "r", encoding="utf-8") as fh:
            self.data = json.load(fh)
        salt = base64.b64decode(self.data["salt"])
        key = _derive_key(code, salt)
        if hashlib.sha256(key + b"verifier").hexdigest() != self.data.get("verifier"):
            return False
        self.key = key
        return True

    def save(self):
        os.makedirs(CONFIG_DIR, exist_ok=True)
        with open(CONFIG_FILE, "w", encoding="utf-8") as fh:
            json.dump(self.data, fh, indent=2)

    def get(self, name, default=""):
        return self.data.get("settings", {}).get(name, default)

    def set(self, name, value):
        self.data.setdefault("settings", {})[name] = value

    def get_secret(self, name) -> str:
        blob = self.get(name, "")
        if not blob:
            return ""
        try:
            return decrypt_text(blob, self.key)
        except Exception:
            return ""

    def set_secret(self, name, value: str):
        self.set(name, encrypt_text(value, self.key))


# ---------------------------------------------------------------------------
# Client Binance (REST)
# ---------------------------------------------------------------------------


class BinanceError(Exception):
    pass


class BinanceClient:
    def __init__(self, api_key: str = "", api_secret: str = ""):
        # .strip() partout : des espaces/retours à la ligne collés avec la clé
        # provoquent l'erreur Binance -1022 (signature invalide).
        self.api_key = (api_key or "").strip()
        self.api_secret = (api_secret or "").strip()
        self._time_offset = 0

    def _request(self, method: str, path: str, params=None, signed=False, timeout=15):
        params = dict(params or {})
        headers = {"User-Agent": f"{APP_NAME}/{APP_VERSION}"}
        if signed:
            if not self.api_key or not self.api_secret:
                raise BinanceError("Clés API manquantes")
            params["timestamp"] = int(time.time() * 1000) + self._time_offset
            params["recvWindow"] = 10_000
            qs = urllib.parse.urlencode(params)
            sig = hmac.new(self.api_secret.encode(), qs.encode(), hashlib.sha256).hexdigest()
            qs = f"{qs}&signature={sig}"
            headers["X-MBX-APIKEY"] = self.api_key
        else:
            qs = urllib.parse.urlencode(params)

        url = f"{BINANCE_API}{path}"
        body = None
        if method == "GET":
            if qs:
                url += "?" + qs
        else:
            body = qs.encode("ascii")
            headers["Content-Type"] = "application/x-www-form-urlencoded"

        req = urllib.request.Request(url, data=body, headers=headers, method=method)
        try:
            with urllib.request.urlopen(req, timeout=timeout) as resp:
                return json.loads(resp.read().decode("utf-8"))
        except urllib.error.HTTPError as exc:
            try:
                payload = json.loads(exc.read().decode("utf-8"))
                raise BinanceError(f"Binance {payload.get('code')}: {payload.get('msg')}") from None
            except (ValueError, KeyError):
                raise BinanceError(f"HTTP {exc.code}") from None
        except Exception as exc:
            raise BinanceError(f"Réseau: {exc}") from None

    def sync_time(self):
        server = self._request("GET", "/api/v3/time")["serverTime"]
        self._time_offset = server - int(time.time() * 1000)

    def price(self, symbol: str) -> float:
        return float(self._request("GET", "/api/v3/ticker/price", {"symbol": symbol})["price"])

    def klines_closes(self, symbol: str, interval="1m", limit=120):
        rows = self._request(
            "GET", "/api/v3/klines", {"symbol": symbol, "interval": interval, "limit": limit}
        )
        return [float(r[4]) for r in rows]

    def symbol_filters(self, symbol: str) -> dict:
        info = self._request("GET", "/api/v3/exchangeInfo", {"symbol": symbol})
        out = {"stepSize": 0.000001, "minQty": 0.0, "minNotional": 10.0}
        for f in info["symbols"][0]["filters"]:
            if f["filterType"] == "LOT_SIZE":
                out["stepSize"] = float(f["stepSize"])
                out["minQty"] = float(f["minQty"])
            elif f["filterType"] in ("NOTIONAL", "MIN_NOTIONAL"):
                out["minNotional"] = float(f.get("minNotional", 10.0))
        return out

    def balances(self) -> dict:
        acct = self._request("GET", "/api/v3/account", signed=True)
        return {b["asset"]: float(b["free"]) for b in acct["balances"] if float(b["free"]) > 0}

    def market_buy_quote(self, symbol: str, quote_amount: float) -> dict:
        return self._request(
            "POST",
            "/api/v3/order",
            {
                "symbol": symbol,
                "side": "BUY",
                "type": "MARKET",
                "quoteOrderQty": f"{quote_amount:.2f}",
            },
            signed=True,
        )

    def market_sell(self, symbol: str, quantity_str: str) -> dict:
        return self._request(
            "POST",
            "/api/v3/order",
            {"symbol": symbol, "side": "SELL", "type": "MARKET", "quantity": quantity_str},
            signed=True,
        )


def round_step(quantity: float, step: float) -> str:
    """Arrondit une quantité vers le bas au pas LOT_SIZE de Binance."""
    if step <= 0:
        return f"{quantity:.6f}"
    precision = max(0, f"{step:.10f}".rstrip("0")[::-1].find("."))
    floored = int(quantity / step) * step
    return f"{floored:.{precision}f}"


# ---------------------------------------------------------------------------
# Brokers (exécution des ordres)
# ---------------------------------------------------------------------------


class PaperBroker:
    """Simulation locale : achats/ventes instantanés au prix du marché."""

    def __init__(self, starting_cash: float, fee_rate: float = 0.001):
        self.cash = starting_cash
        self.qty = 0.0
        self.fee_rate = fee_rate

    def buy(self, price: float, stake: float, filters: dict) -> dict:
        stake = min(stake, self.cash)
        if stake <= 0:
            raise BinanceError("Solde paper insuffisant")
        qty = (stake / price) * (1 - self.fee_rate)
        self.cash -= stake
        self.qty += qty
        return {"qty": qty, "price": price, "cost": stake}

    def sell(self, price: float, filters: dict) -> dict:
        if self.qty <= 0:
            raise BinanceError("Aucune position paper à vendre")
        proceeds = self.qty * price * (1 - self.fee_rate)
        qty = self.qty
        self.cash += proceeds
        self.qty = 0.0
        return {"qty": qty, "price": price, "proceeds": proceeds}

    def equity(self, price: float) -> float:
        return self.cash + self.qty * price


class RealBroker:
    """Ordres réels au marché sur Binance Spot."""

    def __init__(self, client: BinanceClient, symbol: str):
        self.client = client
        self.symbol = symbol
        self.qty = 0.0  # quantité acquise par le bot (à revendre)
        base = symbol
        for quote in ("USDT", "USDC", "FDUSD", "BUSD", "EUR", "TRY", "BTC", "ETH", "BNB"):
            if symbol.endswith(quote):
                base = symbol[: -len(quote)]
                self.quote_asset = quote
                break
        else:
            self.quote_asset = "USDT"
        self.base_asset = base

    def buy(self, price: float, stake: float, filters: dict) -> dict:
        if stake < filters["minNotional"]:
            raise BinanceError(
                f"Mise trop faible : {stake:.2f} {self.quote_asset} < minimum Binance "
                f"{filters['minNotional']:.2f} {self.quote_asset}. Augmente « Mise / trade »."
            )
        resp = self.client.market_buy_quote(self.symbol, stake)
        qty = float(resp.get("executedQty", 0))
        cost = float(resp.get("cummulativeQuoteQty", stake))
        fill_price = cost / qty if qty else price
        self.qty += qty
        return {"qty": qty, "price": fill_price, "cost": cost}

    def sell(self, price: float, filters: dict) -> dict:
        qty_str = round_step(self.qty, filters["stepSize"])
        if float(qty_str) <= 0 or float(qty_str) < filters["minQty"]:
            raise BinanceError("Quantité à vendre trop faible (position vide ?)")
        resp = self.client.market_sell(self.symbol, qty_str)
        qty = float(resp.get("executedQty", 0))
        proceeds = float(resp.get("cummulativeQuoteQty", qty * price))
        fill_price = proceeds / qty if qty else price
        self.qty = max(0.0, self.qty - qty)
        return {"qty": qty, "price": fill_price, "proceeds": proceeds}

    def equity(self, price: float) -> float:
        bals = self.client.balances()
        return bals.get(self.quote_asset, 0.0) + bals.get(self.base_asset, 0.0) * price


# ---------------------------------------------------------------------------
# Stratégie
# ---------------------------------------------------------------------------


def sma(values, period: int):
    if len(values) < period:
        return None
    return sum(values[-period:]) / period


def crossover_signal(closes, fast: int, slow: int):
    """'buy' au croisement haussier, 'sell' au croisement baissier, sinon None."""
    if len(closes) < slow + 1:
        return None, None, None
    fast_now = sma(closes, fast)
    slow_now = sma(closes, slow)
    fast_prev = sma(closes[:-1], fast)
    slow_prev = sma(closes[:-1], slow)
    signal = None
    if fast_prev is not None and slow_prev is not None:
        if fast_prev <= slow_prev and fast_now > slow_now:
            signal = "buy"
        elif fast_prev >= slow_prev and fast_now < slow_now:
            signal = "sell"
    return signal, fast_now, slow_now


# ---------------------------------------------------------------------------
# Interface graphique
# ---------------------------------------------------------------------------

DARK_BG = "#101822"
PANEL_BG = "#182430"
ACCENT = "#2ecc71"
RED = "#e74c3c"
TEXT = "#e8eef4"
MUTED = "#7f94a8"


class TradingBotApp:
    def __init__(self, root: tk.Tk, store: ConfigStore):
        self.root = root
        self.store = store
        self.queue = queue.Queue()
        self.bot_thread = None
        self.stop_event = threading.Event()
        self.manual_close_event = threading.Event()
        self.price_history = deque(maxlen=400)
        self.trades = []
        self.day_start_equity = None
        self.current_equity = None

        root.title(f"{APP_NAME} v{APP_VERSION}")
        root.geometry("1024x720")
        root.configure(bg=DARK_BG)
        self._build_style()
        self._build_ui()
        self._load_settings()
        self._load_day_state()
        self._poll_queue()

    # ------------------------------------------------------------------ UI

    def _build_style(self):
        style = ttk.Style(self.root)
        try:
            style.theme_use("clam")
        except tk.TclError:
            pass
        style.configure("TNotebook", background=DARK_BG, borderwidth=0)
        style.configure("TNotebook.Tab", background=PANEL_BG, foreground=TEXT, padding=(16, 8))
        style.map("TNotebook.Tab", background=[("selected", ACCENT)], foreground=[("selected", "#08131c")])
        style.configure("TFrame", background=DARK_BG)
        style.configure("Panel.TFrame", background=PANEL_BG)
        style.configure("TLabel", background=DARK_BG, foreground=TEXT, font=("Segoe UI", 10))
        style.configure("Panel.TLabel", background=PANEL_BG, foreground=TEXT, font=("Segoe UI", 10))
        style.configure("Muted.TLabel", background=PANEL_BG, foreground=MUTED, font=("Segoe UI", 9))
        style.configure("Big.TLabel", background=PANEL_BG, foreground=TEXT, font=("Segoe UI", 16, "bold"))
        style.configure("TButton", font=("Segoe UI", 10, "bold"), padding=8)
        style.configure("Treeview", background=PANEL_BG, fieldbackground=PANEL_BG, foreground=TEXT)
        style.configure("Treeview.Heading", background=DARK_BG, foreground=TEXT)

    def _build_ui(self):
        nb = ttk.Notebook(self.root)
        nb.pack(fill="both", expand=True, padx=8, pady=8)
        self.tab_bot = ttk.Frame(nb)
        self.tab_cfg = ttk.Frame(nb)
        self.tab_hist = ttk.Frame(nb)
        nb.add(self.tab_bot, text="  📈 Bot de trading  ")
        nb.add(self.tab_hist, text="  🧾 Historique  ")
        nb.add(self.tab_cfg, text="  ⚙️ Configuration  ")
        self._build_bot_tab()
        self._build_history_tab()
        self._build_config_tab()

    def _build_bot_tab(self):
        # ----- barre de stats
        stats = ttk.Frame(self.tab_bot, style="Panel.TFrame")
        stats.pack(fill="x", padx=6, pady=6)
        self.lbl_price = self._stat(stats, "Prix", "—", 0)
        self.lbl_equity = self._stat(stats, "Équité", "—", 1)
        self.lbl_daygain = self._stat(stats, "Gain du jour", "—", 2)
        self.lbl_trades = self._stat(stats, "Trades", "0", 3)
        self.lbl_winrate = self._stat(stats, "Réussite", "—", 4)
        self.lbl_trend = self._stat(stats, "Tendance", "—", 5)
        self.lbl_status = self._stat(stats, "État", "Arrêté", 6)
        for col in range(7):
            stats.columnconfigure(col, weight=1)

        # ----- graphique
        chart_frame = ttk.Frame(self.tab_bot, style="Panel.TFrame")
        chart_frame.pack(fill="both", expand=True, padx=6, pady=(0, 6))
        self.canvas = tk.Canvas(chart_frame, bg="#0b121a", highlightthickness=0, height=260)
        self.canvas.pack(fill="both", expand=True, padx=4, pady=4)
        self.canvas.bind("<Configure>", lambda e: self._draw_chart())

        # ----- journal d'activité
        log_frame = ttk.Frame(self.tab_bot, style="Panel.TFrame")
        log_frame.pack(fill="both", padx=6, pady=(0, 6))
        ttk.Label(log_frame, text="📋 Journal d'activité", style="Panel.TLabel").pack(anchor="w", padx=6, pady=(4, 0))
        self.log_text = tk.Text(
            log_frame, height=8, bg="#0b121a", fg=TEXT, insertbackground=TEXT,
            font=("Consolas", 9), state="disabled", wrap="word",
        )
        self.log_text.pack(fill="both", expand=True, padx=6, pady=4)

        # ----- boutons
        btns = ttk.Frame(self.tab_bot)
        btns.pack(fill="x", padx=6, pady=(0, 8))
        self.btn_start = tk.Button(
            btns, text="▶ Démarrer le bot", command=self.start_bot,
            bg=ACCENT, fg="#08131c", font=("Segoe UI", 11, "bold"), relief="flat", padx=16, pady=8,
        )
        self.btn_start.pack(side="left", padx=4)
        self.btn_stop = tk.Button(
            btns, text="⏹ Arrêter", command=self.stop_bot, state="disabled",
            bg=RED, fg="white", font=("Segoe UI", 11, "bold"), relief="flat", padx=16, pady=8,
        )
        self.btn_stop.pack(side="left", padx=4)
        self.btn_close_pos = tk.Button(
            btns, text="💵 Fermer la position", command=self.manual_close, state="disabled",
            bg="#f39c12", fg="#08131c", font=("Segoe UI", 11, "bold"), relief="flat", padx=16, pady=8,
        )
        self.btn_close_pos.pack(side="left", padx=4)
        self.mode_badge = ttk.Label(btns, text="", style="TLabel", font=("Segoe UI", 11, "bold"))
        self.mode_badge.pack(side="right", padx=8)

    def _stat(self, parent, title, value, col):
        cell = ttk.Frame(parent, style="Panel.TFrame")
        cell.grid(row=0, column=col, sticky="nsew", padx=8, pady=8)
        ttk.Label(cell, text=title, style="Muted.TLabel").pack(anchor="w")
        lbl = ttk.Label(cell, text=value, style="Big.TLabel")
        lbl.pack(anchor="w")
        return lbl

    def _build_history_tab(self):
        frame = ttk.Frame(self.tab_hist, style="Panel.TFrame")
        frame.pack(fill="both", expand=True, padx=6, pady=6)
        cols = ("heure", "côté", "prix", "quantité", "montant", "pnl")
        self.tree = ttk.Treeview(frame, columns=cols, show="headings", height=18)
        headers = {
            "heure": "Heure", "côté": "Côté", "prix": "Prix",
            "quantité": "Quantité", "montant": "Montant", "pnl": "P&L",
        }
        for c in cols:
            self.tree.heading(c, text=headers[c])
            self.tree.column(c, width=120, anchor="center")
        self.tree.pack(fill="both", expand=True, padx=6, pady=6)
        tk.Button(
            frame, text="⬇ Exporter en CSV", command=self.export_csv,
            bg=PANEL_BG, fg=TEXT, relief="flat", padx=12, pady=6, font=("Segoe UI", 10, "bold"),
        ).pack(anchor="e", padx=6, pady=(0, 6))

    def _build_config_tab(self):
        frame = ttk.Frame(self.tab_cfg, style="Panel.TFrame")
        frame.pack(fill="both", expand=True, padx=6, pady=6)
        pad = {"padx": 10, "pady": 6}

        def row(r, label, var, width=28, show=None):
            ttk.Label(frame, text=label, style="Panel.TLabel").grid(row=r, column=0, sticky="w", **pad)
            entry = tk.Entry(frame, textvariable=var, width=width, bg="#0b121a", fg=TEXT,
                             insertbackground=TEXT, relief="flat", show=show)
            entry.grid(row=r, column=1, sticky="w", **pad)
            return entry

        ttk.Label(frame, text="🔑 Connexion Binance", style="Big.TLabel").grid(row=0, column=0, columnspan=2, sticky="w", **pad)
        self.var_api_key = tk.StringVar()
        self.var_api_secret = tk.StringVar()
        row(1, "Clé API", self.var_api_key, width=64)
        row(2, "Clé secrète", self.var_api_secret, width=64, show="•")

        ttk.Label(frame, text="🤖 Réglages du bot", style="Big.TLabel").grid(row=3, column=0, columnspan=2, sticky="w", **pad)
        self.var_symbol = tk.StringVar(value="BTCUSDT")
        self.var_mode = tk.StringVar(value="paper")
        self.var_stake = tk.StringVar(value="12")
        self.var_capital = tk.StringVar(value="50")
        self.var_fast = tk.StringVar(value="7")
        self.var_slow = tk.StringVar(value="25")
        self.var_tp = tk.StringVar(value="1.5")
        self.var_sl = tk.StringVar(value="1.0")
        self.var_trail = tk.StringVar(value="0.7")
        self.var_daily_loss = tk.StringVar(value="5")
        self.var_cooldown = tk.StringVar(value="60")

        row(4, "Paire (ex : BTCUSDT)", self.var_symbol)
        ttk.Label(frame, text="Mode", style="Panel.TLabel").grid(row=5, column=0, sticky="w", **pad)
        mode_frame = ttk.Frame(frame, style="Panel.TFrame")
        mode_frame.grid(row=5, column=1, sticky="w", **pad)
        tk.Radiobutton(mode_frame, text="🧪 Paper (simulation)", variable=self.var_mode, value="paper",
                       bg=PANEL_BG, fg=TEXT, selectcolor=DARK_BG, activebackground=PANEL_BG,
                       activeforeground=TEXT).pack(side="left")
        tk.Radiobutton(mode_frame, text="💰 Réel (Binance)", variable=self.var_mode, value="real",
                       bg=PANEL_BG, fg=TEXT, selectcolor=DARK_BG, activebackground=PANEL_BG,
                       activeforeground=TEXT).pack(side="left", padx=12)
        row(6, "Mise / trade (USDT)", self.var_stake)
        row(7, "Capital départ paper (USDT)", self.var_capital)
        row(8, "SMA rapide (périodes)", self.var_fast)
        row(9, "SMA lente (périodes)", self.var_slow)
        row(10, "Take-profit (%)", self.var_tp)
        row(11, "Stop-loss (%)", self.var_sl)
        row(12, "Trailing stop (%, 0 = désactivé)", self.var_trail)
        row(13, "Limite de perte journalière (USDT)", self.var_daily_loss)
        row(14, "Délai min entre trades (secondes)", self.var_cooldown)

        btns = ttk.Frame(frame, style="Panel.TFrame")
        btns.grid(row=15, column=0, columnspan=2, sticky="w", **pad)
        tk.Button(btns, text="🔌 Tester la connexion", command=self.test_connection,
                  bg="#3498db", fg="white", relief="flat", padx=12, pady=6,
                  font=("Segoe UI", 10, "bold")).pack(side="left", padx=4)
        tk.Button(btns, text="💾 Enregistrer", command=self.save_settings,
                  bg=ACCENT, fg="#08131c", relief="flat", padx=12, pady=6,
                  font=("Segoe UI", 10, "bold")).pack(side="left", padx=4)
        self.lbl_cfg_status = ttk.Label(frame, text="", style="Muted.TLabel")
        self.lbl_cfg_status.grid(row=16, column=0, columnspan=2, sticky="w", **pad)

    # -------------------------------------------------------- persistance

    def _load_settings(self):
        s = self.store
        self.var_api_key.set(s.get_secret("api_key"))
        self.var_api_secret.set(s.get_secret("api_secret"))
        for var, name, default in (
            (self.var_symbol, "symbol", "BTCUSDT"),
            (self.var_mode, "mode", "paper"),
            (self.var_stake, "stake", "12"),
            (self.var_capital, "capital", "50"),
            (self.var_fast, "sma_fast", "7"),
            (self.var_slow, "sma_slow", "25"),
            (self.var_tp, "tp", "1.5"),
            (self.var_sl, "sl", "1.0"),
            (self.var_trail, "trail", "0.7"),
            (self.var_daily_loss, "daily_loss", "5"),
            (self.var_cooldown, "cooldown", "60"),
        ):
            var.set(s.get(name, default))

    def save_settings(self):
        s = self.store
        s.set_secret("api_key", self.var_api_key.get().strip())
        s.set_secret("api_secret", self.var_api_secret.get().strip())
        for var, name in (
            (self.var_symbol, "symbol"), (self.var_mode, "mode"), (self.var_stake, "stake"),
            (self.var_capital, "capital"), (self.var_fast, "sma_fast"), (self.var_slow, "sma_slow"),
            (self.var_tp, "tp"), (self.var_sl, "sl"), (self.var_trail, "trail"),
            (self.var_daily_loss, "daily_loss"), (self.var_cooldown, "cooldown"),
        ):
            s.set(name, var.get().strip())
        s.save()
        self.lbl_cfg_status.config(text="✅ Réglages enregistrés (clés chiffrées).")

    def _load_day_state(self):
        try:
            with open(STATE_FILE, "r", encoding="utf-8") as fh:
                state = json.load(fh)
            if state.get("date") == date.today().isoformat():
                self.day_start_equity = state.get("day_start_equity")
        except Exception:
            pass

    def _save_day_state(self):
        try:
            os.makedirs(CONFIG_DIR, exist_ok=True)
            with open(STATE_FILE, "w", encoding="utf-8") as fh:
                json.dump({"date": date.today().isoformat(),
                           "day_start_equity": self.day_start_equity}, fh)
        except Exception:
            pass

    # ------------------------------------------------------------ actions

    def _read_config(self):
        """Fige la configuration dans un dict AVANT de lancer le thread.

        Le thread du bot ne doit JAMAIS lire une variable tkinter : cela
        provoque « RuntimeError: main thread is not in main loop » et tue
        silencieusement le bot (le bug d'origine de ce projet).
        """
        try:
            cfg = {
                "symbol": self.var_symbol.get().strip().upper(),
                "mode": self.var_mode.get(),
                "stake": float(self.var_stake.get().replace(",", ".")),
                "capital": float(self.var_capital.get().replace(",", ".")),
                "fast": int(self.var_fast.get()),
                "slow": int(self.var_slow.get()),
                "tp": float(self.var_tp.get().replace(",", ".")) / 100.0,
                "sl": float(self.var_sl.get().replace(",", ".")) / 100.0,
                "trail": float(self.var_trail.get().replace(",", ".")) / 100.0,
                "daily_loss": float(self.var_daily_loss.get().replace(",", ".")),
                "cooldown": float(self.var_cooldown.get()),
                "api_key": self.var_api_key.get().strip(),
                "api_secret": self.var_api_secret.get().strip(),
            }
        except ValueError:
            messagebox.showerror(APP_NAME, "Un des réglages n'est pas un nombre valide.")
            return None
        if cfg["fast"] >= cfg["slow"]:
            messagebox.showerror(APP_NAME, "La SMA rapide doit être plus courte que la SMA lente.")
            return None
        if cfg["mode"] == "real" and (not cfg["api_key"] or not cfg["api_secret"]):
            messagebox.showerror(APP_NAME, "Mode réel : renseigne d'abord tes clés API (onglet Configuration).")
            return None
        return cfg

    def start_bot(self):
        if self.bot_thread and self.bot_thread.is_alive():
            return
        cfg = self._read_config()
        if cfg is None:
            return
        if cfg["mode"] == "real":
            if not messagebox.askyesno(
                APP_NAME,
                "⚠️ Mode RÉEL : le bot va passer de vrais ordres sur ton compte Binance.\n\n"
                f"Paire : {cfg['symbol']}\nMise par trade : {cfg['stake']:.2f}\n\nDémarrer ?",
            ):
                return
        self.stop_event.clear()
        self.manual_close_event.clear()
        self.price_history.clear()
        self.btn_start.config(state="disabled")
        self.btn_stop.config(state="normal")
        self.btn_close_pos.config(state="disabled")
        self.lbl_status.config(text="Démarrage…")
        self.mode_badge.config(
            text="💰 MODE RÉEL" if cfg["mode"] == "real" else "🧪 MODE PAPER",
            foreground=RED if cfg["mode"] == "real" else ACCENT,
        )
        self.bot_thread = threading.Thread(target=self._bot_loop, args=(cfg,), daemon=True)
        self.bot_thread.start()

    def stop_bot(self):
        self.stop_event.set()
        self.lbl_status.config(text="Arrêt…")

    def manual_close(self):
        if messagebox.askyesno(APP_NAME, "Vendre la position maintenant au prix du marché ?"):
            self.manual_close_event.set()

    def test_connection(self):
        key = self.var_api_key.get().strip()
        secret = self.var_api_secret.get().strip()
        symbol = self.var_symbol.get().strip().upper() or "BTCUSDT"
        self.lbl_cfg_status.config(text="Test en cours…")

        def worker():
            try:
                client = BinanceClient(key, secret)
                price = client.price(symbol)
                msg = f"✅ Marché OK — {symbol} = {price:.2f}"
                if key and secret:
                    client.sync_time()
                    bals = client.balances()
                    usdt = bals.get("USDT", 0.0)
                    msg += f" | Compte OK — {usdt:.2f} USDT libres"
                else:
                    msg += " | (pas de clés : compte non testé)"
                self.queue.put({"type": "cfg_status", "text": msg})
            except Exception as exc:
                self.queue.put({"type": "cfg_status", "text": f"❌ {exc}"})

        threading.Thread(target=worker, daemon=True).start()

    def export_csv(self):
        if not self.trades:
            messagebox.showinfo(APP_NAME, "Aucun trade à exporter pour l'instant.")
            return
        path = filedialog.asksaveasfilename(
            defaultextension=".csv", initialfile="trades.csv",
            filetypes=[("CSV", "*.csv")],
        )
        if not path:
            return
        with open(path, "w", newline="", encoding="utf-8-sig") as fh:
            writer = csv.writer(fh, delimiter=";")
            writer.writerow(["Heure", "Côté", "Prix", "Quantité", "Montant", "P&L"])
            for t in self.trades:
                writer.writerow([t["time"], t["side"], t["price"], t["qty"], t["amount"], t.get("pnl", "")])
        messagebox.showinfo(APP_NAME, f"Exporté : {path}")

    # ------------------------------------------------- boucle du bot (thread)

    def _bot_loop(self, cfg: dict):
        """Tourne dans un thread séparé. AUCUN accès tkinter ici — uniquement
        cfg (dict figé), self.queue.put(), et les deux threading.Event."""
        put = self.queue.put

        def log(text):
            put({"type": "log", "text": text})

        try:
            client = BinanceClient(cfg["api_key"], cfg["api_secret"])
            filters = client.symbol_filters(cfg["symbol"])
            if cfg["mode"] == "real":
                client.sync_time()
                broker = RealBroker(client, cfg["symbol"])
            else:
                broker = PaperBroker(cfg["capital"])
            log(f"🚀 Bot démarré — {cfg['symbol']} en mode "
                f"{'RÉEL' if cfg['mode'] == 'real' else 'PAPER'} | mise {cfg['stake']:.2f}")
            if cfg["mode"] == "real" and cfg["stake"] < filters["minNotional"]:
                log(f"⚠️ Attention : mise {cfg['stake']:.2f} < minimum Binance "
                    f"{filters['minNotional']:.2f} — les achats seront refusés.")
        except Exception as exc:
            put({"type": "fatal", "text": f"Impossible de démarrer : {exc}"})
            return

        position = None  # {'entry': float, 'qty': float, 'high': float, 'cost': float}
        last_trade_ts = 0.0
        last_klines_ts = 0.0
        last_equity_ts = 0.0
        closes = []
        equity = None
        day_start_equity = None
        daily_stop_hit = False

        while not self.stop_event.is_set():
            loop_start = time.time()
            try:
                price = client.price(cfg["symbol"])

                # bougies rafraîchies toutes les 20 s
                if time.time() - last_klines_ts > 20:
                    closes = client.klines_closes(cfg["symbol"], "1m", cfg["slow"] + 5)
                    last_klines_ts = time.time()
                if closes:
                    closes[-1] = price
                signal, fast_now, slow_now = crossover_signal(closes, cfg["fast"], cfg["slow"])

                # équité : paper = instantané ; réel = requête signée toutes les 30 s
                if cfg["mode"] == "paper":
                    equity = broker.equity(price)
                elif time.time() - last_equity_ts > 30 or equity is None:
                    equity = broker.equity(price)
                    last_equity_ts = time.time()
                if day_start_equity is None and equity is not None:
                    day_start_equity = equity
                    put({"type": "day_start", "equity": equity})
                day_gain = (equity - day_start_equity) if (equity is not None and day_start_equity is not None) else None

                # limite de perte journalière
                if (not daily_stop_hit and day_gain is not None
                        and cfg["daily_loss"] > 0 and day_gain <= -cfg["daily_loss"]):
                    daily_stop_hit = True
                    log(f"🛑 Limite de perte journalière atteinte ({day_gain:.2f}). "
                        "Le bot n'ouvrira plus de position aujourd'hui.")
                    if position:
                        self.manual_close_event.set()

                # ---------------- décisions de trading
                action = None
                reason = ""
                if position:
                    position["high"] = max(position["high"], price)
                    change = (price - position["entry"]) / position["entry"]
                    if self.manual_close_event.is_set():
                        action, reason = "sell", "fermeture manuelle"
                    elif cfg["tp"] > 0 and change >= cfg["tp"]:
                        action, reason = "sell", f"take-profit +{change * 100:.2f}%"
                    elif cfg["sl"] > 0 and change <= -cfg["sl"]:
                        action, reason = "sell", f"stop-loss {change * 100:.2f}%"
                    elif cfg["trail"] > 0 and price <= position["high"] * (1 - cfg["trail"]):
                        action, reason = "sell", "trailing stop"
                    elif signal == "sell":
                        action, reason = "sell", "croisement baissier"
                else:
                    self.manual_close_event.clear()
                    if (signal == "buy" and not daily_stop_hit
                            and time.time() - last_trade_ts >= cfg["cooldown"]):
                        action, reason = "buy", "croisement haussier"

                if action == "buy":
                    try:
                        fill = broker.buy(price, cfg["stake"], filters)
                        position = {"entry": fill["price"], "qty": fill["qty"],
                                    "high": fill["price"], "cost": fill["cost"]}
                        last_trade_ts = time.time()
                        log(f"🟢 ACHAT exécuté à {fill['price']:.2f} "
                            f"(qté {fill['qty']:.6f}, {reason})")
                        put({"type": "trade", "side": "ACHAT", "price": fill["price"],
                             "qty": fill["qty"], "amount": fill["cost"], "pnl": None})
                    except Exception as exc:
                        log(f"⚠️ Erreur ordre ACHAT : {exc}")
                        last_trade_ts = time.time()  # évite de spammer Binance
                elif action == "sell" and position:
                    try:
                        fill = broker.sell(price, filters)
                        pnl = fill["proceeds"] - position["cost"]
                        log(f"🔴 VENTE exécutée à {fill['price']:.2f} "
                            f"({reason}) — P&L {pnl:+.2f}")
                        put({"type": "trade", "side": "VENTE", "price": fill["price"],
                             "qty": fill["qty"], "amount": fill["proceeds"], "pnl": pnl})
                        position = None
                        last_trade_ts = time.time()
                        self.manual_close_event.clear()
                    except Exception as exc:
                        log(f"⚠️ Erreur ordre VENTE : {exc}")
                        self.manual_close_event.clear()

                trend = None
                if fast_now is not None and slow_now is not None:
                    trend = "up" if fast_now > slow_now else "down"
                put({
                    "type": "tick",
                    "price": price,
                    "equity": equity,
                    "day_gain": day_gain,
                    "trend": trend,
                    "sma_fast": fast_now,
                    "sma_slow": slow_now,
                    "position_open": position is not None,
                    "entry": position["entry"] if position else None,
                })
            except Exception as exc:
                log(f"⚠️ {exc} — nouvel essai dans 5 s")
                self.stop_event.wait(5)

            self.stop_event.wait(max(0.5, 3 - (time.time() - loop_start)))

        if position:
            log("ℹ️ Bot arrêté avec une position ouverte — utilise « Fermer la position » "
                "après redémarrage, ou vends manuellement sur Binance.")
        put({"type": "stopped"})

    # -------------------------------------------------- affichage (thread UI)

    def _poll_queue(self):
        try:
            while True:
                msg = self.queue.get_nowait()
                try:
                    self._apply_update(msg)
                except Exception as exc:
                    # une erreur d'affichage ne doit jamais tuer la boucle
                    print(f"[UI] erreur d'affichage ignorée : {exc}")
        except queue.Empty:
            pass
        self.root.after(200, self._poll_queue)

    def _apply_update(self, msg: dict):
        kind = msg.get("type")
        if kind == "tick":
            price = msg["price"]
            self.price_history.append(price)
            self.lbl_price.config(text=f"{price:,.2f}")
            if msg.get("equity") is not None:
                self.current_equity = msg["equity"]
                self.lbl_equity.config(text=f"{msg['equity']:,.2f}")
            gain = msg.get("day_gain")
            if gain is not None:
                color = ACCENT if gain >= 0 else RED
                self.lbl_daygain.config(text=f"{gain:+,.2f}", foreground=color)
            trend = msg.get("trend")
            if trend == "up":
                self.lbl_trend.config(text="Haussière 📈", foreground=ACCENT)
            elif trend == "down":
                self.lbl_trend.config(text="Baissière 📉", foreground=RED)
            self.lbl_status.config(
                text="Position ouverte" if msg.get("position_open") else "Recherche de signal"
            )
            self.btn_close_pos.config(state="normal" if msg.get("position_open") else "disabled")
            self._last_tick = msg
            self._draw_chart()
        elif kind == "log":
            self._append_log(msg["text"])
        elif kind == "trade":
            now = datetime.now().strftime("%H:%M:%S")
            pnl = msg.get("pnl")
            self.trades.append({
                "time": now, "side": msg["side"], "price": f"{msg['price']:.2f}",
                "qty": f"{msg['qty']:.6f}", "amount": f"{msg['amount']:.2f}",
                "pnl": f"{pnl:+.2f}" if pnl is not None else "",
            })
            self.tree.insert("", 0, values=(
                now, msg["side"], f"{msg['price']:.2f}", f"{msg['qty']:.6f}",
                f"{msg['amount']:.2f}", f"{pnl:+.2f}" if pnl is not None else "—",
            ))
            self.lbl_trades.config(text=str(len(self.trades)))
            sells = [t for t in self.trades if t["side"] == "VENTE" and t["pnl"]]
            if sells:
                wins = sum(1 for t in sells if not t["pnl"].startswith("-"))
                self.lbl_winrate.config(text=f"{wins}/{len(sells)} ({100 * wins / len(sells):.0f}%)")
        elif kind == "day_start":
            if self.day_start_equity is None:
                self.day_start_equity = msg["equity"]
                self._save_day_state()
        elif kind == "cfg_status":
            self.lbl_cfg_status.config(text=msg["text"])
        elif kind == "fatal":
            self._append_log(f"❌ {msg['text']}")
            messagebox.showerror(APP_NAME, msg["text"])
            self._set_stopped()
        elif kind == "stopped":
            self._append_log("⏹ Bot arrêté.")
            self._set_stopped()

    def _set_stopped(self):
        self.btn_start.config(state="normal")
        self.btn_stop.config(state="disabled")
        self.btn_close_pos.config(state="disabled")
        self.lbl_status.config(text="Arrêté")

    def _append_log(self, text):
        stamp = datetime.now().strftime("%H:%M:%S")
        self.log_text.config(state="normal")
        self.log_text.insert("end", f"[{stamp}] {text}\n")
        # borne le journal à ~500 lignes
        if int(self.log_text.index("end-1c").split(".")[0]) > 500:
            self.log_text.delete("1.0", "2.0")
        self.log_text.see("end")
        self.log_text.config(state="disabled")

    def _draw_chart(self):
        c = self.canvas
        c.delete("all")
        w = c.winfo_width()
        h = c.winfo_height()
        if w < 40 or h < 40 or len(self.price_history) < 2:
            c.create_text(w // 2, h // 2, text="En attente de données…", fill=MUTED,
                          font=("Segoe UI", 11))
            return
        prices = list(self.price_history)
        lo, hi = min(prices), max(prices)
        span = (hi - lo) or 1e-9
        pad_x, pad_y = 8, 12

        def xy(i, p):
            x = pad_x + (w - 2 * pad_x) * i / max(1, len(prices) - 1)
            y = h - pad_y - (h - 2 * pad_y) * (p - lo) / span
            return x, y

        # grille légère + bornes
        for frac in (0.25, 0.5, 0.75):
            y = pad_y + (h - 2 * pad_y) * frac
            c.create_line(pad_x, y, w - pad_x, y, fill="#1c2a38")
        c.create_text(w - pad_x - 4, pad_y, text=f"{hi:,.2f}", fill=MUTED, anchor="ne", font=("Segoe UI", 8))
        c.create_text(w - pad_x - 4, h - pad_y, text=f"{lo:,.2f}", fill=MUTED, anchor="se", font=("Segoe UI", 8))

        pts = [xy(i, p) for i, p in enumerate(prices)]
        flat = [v for pt in pts for v in pt]
        up = prices[-1] >= prices[0]
        c.create_line(*flat, fill=ACCENT if up else RED, width=2, smooth=True)

        # ligne d'entrée de la position
        tick = getattr(self, "_last_tick", None)
        if tick and tick.get("entry"):
            _, ey = xy(0, min(max(tick["entry"], lo), hi))
            c.create_line(pad_x, ey, w - pad_x, ey, fill="#f39c12", dash=(4, 3))
            c.create_text(pad_x + 4, ey - 8, text=f"Entrée {tick['entry']:,.2f}",
                          fill="#f39c12", anchor="w", font=("Segoe UI", 8))


# ---------------------------------------------------------------------------
# Démarrage (code d'accès)
# ---------------------------------------------------------------------------


def ask_access_code(root: tk.Tk, store: ConfigStore) -> bool:
    if not store.exists():
        while True:
            code = simpledialog.askstring(
                APP_NAME, "Première utilisation.\nChoisis un code d'accès "
                "(il protégera tes clés API) :", show="•", parent=root)
            if code is None:
                return False
            if len(code) < 4:
                messagebox.showwarning(APP_NAME, "Le code doit faire au moins 4 caractères.")
                continue
            confirm = simpledialog.askstring(APP_NAME, "Confirme le code :", show="•", parent=root)
            if confirm != code:
                messagebox.showwarning(APP_NAME, "Les codes ne correspondent pas.")
                continue
            store.create(code)
            return True
    for _ in range(3):
        code = simpledialog.askstring(APP_NAME, "Entre ton code d'accès :", show="•", parent=root)
        if code is None:
            return False
        if store.unlock(code):
            return True
        messagebox.showwarning(APP_NAME, "Code incorrect.")
    if messagebox.askyesno(
        APP_NAME, "3 échecs. Réinitialiser la configuration ?\n"
        "(Tes clés API enregistrées seront effacées.)"
    ):
        try:
            os.remove(CONFIG_FILE)
        except OSError:
            pass
        return ask_access_code(root, store)
    return False


def main():
    root = tk.Tk()
    root.withdraw()
    store = ConfigStore()
    if not ask_access_code(root, store):
        root.destroy()
        return
    root.deiconify()
    TradingBotApp(root, store)
    root.mainloop()


if __name__ == "__main__":
    main()
