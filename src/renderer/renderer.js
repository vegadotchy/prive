'use strict';

// ---------------------------------------------------------------------------
// Configuration de la navigation
// ---------------------------------------------------------------------------

// Onglets « site web » (affichés dans une <webview>).
const SITES = {
  gmail: { title: 'Gmail', ico: '✉️', url: 'https://mail.google.com/mail/u/0/?tab=wm&ogbl#inbox' },
  doctena: { title: 'Doctena', ico: '📅', url: 'https://secure.doctena.com/' },
  doctoranytime: {
    title: 'Doctoranytime', ico: '🩺',
    url: 'https://www.doctoranytime.be/doctorcrmV2/ConnectedAccount/ChangeAccount?u=0'
  },
  shyfter: { title: 'Shyfter', ico: '🗓️', url: 'https://v3-app.shyfter.co/app/dashboard' },
  inbody: { title: 'InBody', ico: '⚖️', url: 'https://bel.lookinbody.com/', print: true },
  clearfacts: { title: 'ClearFacts / Kyte', ico: '🧾', url: 'https://mbm.clearfacts.be/login' },
  iballab: { title: 'IBC Lab online', ico: '🔬', url: 'https://labonline.lhub-ulb.be/' },
  examens: { title: 'Examens du jour', ico: '📋', urlFromSettings: 'examsUrl' },
  cbip: { title: 'Médicaments (CBIP)', ico: '💊', url: 'https://www.cbip.be/fr/', search: 'cbip' },
  medipost: { title: 'Medipost', ico: '📦', url: 'https://www.medipost.shop/' }
};

// Structure du menu latéral (groupes + entrées). « view » pointe vers un
// panneau statique (accueil, chatgpt, …) ou une clé de SITES.
const NAV = [
  { view: 'accueil', title: 'Accueil', ico: '🏠' },
  { group: 'Communication' },
  { view: 'gmail', title: 'Gmail', ico: '✉️', site: true },
  { view: 'mail', title: 'Envoyer un mail', ico: '📧' },
  { view: 'chatgpt', title: 'Chat IA', ico: '🤖' },
  { group: 'Agenda & patients' },
  { view: 'doctena', title: 'Doctena', ico: '📅', site: true },
  { view: 'doctoranytime', title: 'Doctoranytime', ico: '🩺', site: true },
  { view: 'shyfter', title: 'Shyfter', ico: '🗓️', site: true },
  { view: 'careconnect', title: 'CareConnect', ico: '💻' },
  { group: 'Examens & labo' },
  { view: 'inbody', title: 'InBody', ico: '⚖️', site: true },
  { view: 'iballab', title: 'IBC Lab online', ico: '🔬', site: true },
  { view: 'examens', title: 'Examens du jour', ico: '📋', site: true },
  { view: 'cbip', title: 'Médicaments (CBIP)', ico: '💊', site: true },
  { group: 'Gestion' },
  { view: 'clearfacts', title: 'ClearFacts / Kyte', ico: '🧾', site: true },
  { view: 'medipost', title: 'Medipost', ico: '📦', site: true },
  { group: 'Outils' },
  { view: 'recherche', title: 'Recherche fichiers', ico: '🔎' },
  { view: 'reglages', title: 'Réglages', ico: '⚙️' }
];

let settings = null;
const createdWebviews = new Set();

// ---------------------------------------------------------------------------
// Navigation
// ---------------------------------------------------------------------------

function buildNav() {
  const list = document.getElementById('navList');
  for (const entry of NAV) {
    if (entry.group) {
      const g = document.createElement('div');
      g.className = 'nav-group-label';
      g.textContent = entry.group;
      list.appendChild(g);
      continue;
    }
    const btn = document.createElement('button');
    btn.className = 'nav-item';
    btn.dataset.view = entry.view;
    btn.innerHTML = `<span class="ico">${entry.ico}</span><span>${entry.title}</span>`;
    btn.addEventListener('click', () => showView(entry.view));
    list.appendChild(btn);
  }
}

function showView(viewId) {
  // Onglet site web : créé à la demande.
  if (SITES[viewId]) ensureWebview(viewId);

  document.querySelectorAll('.view').forEach((v) => v.classList.remove('active'));
  const target = document.querySelector(`.view[data-view="${viewId}"]`);
  if (target) target.classList.add('active');

  document.querySelectorAll('.nav-item').forEach((b) => {
    b.classList.toggle('active', b.dataset.view === viewId);
  });
}

function siteUrl(cfg) {
  if (cfg.urlFromSettings && settings) {
    return settings[cfg.urlFromSettings] || cfg.url || '';
  }
  return cfg.url || '';
}

function ensureWebview(viewId) {
  if (createdWebviews.has(viewId)) return;
  const cfg = SITES[viewId];
  const container = document.getElementById('webviewViews');

  const view = document.createElement('section');
  view.className = 'view';
  view.dataset.view = viewId;

  const wrap = document.createElement('div');
  wrap.className = 'web-wrap';

  // Barre d'outils de l'onglet
  const bar = document.createElement('div');
  bar.className = 'web-bar';
  bar.innerHTML = `
    <span class="web-title">${cfg.ico} ${cfg.title}</span>
    <button class="btn tiny" data-act="back">◀</button>
    <button class="btn tiny" data-act="forward">▶</button>
    <button class="btn tiny" data-act="reload">⟳</button>
    <button class="btn tiny" data-act="external">Ouvrir dans le navigateur</button>
  `;

  const webview = document.createElement('webview');
  webview.setAttribute('partition', 'persist:prive');
  webview.setAttribute('allowpopups', 'true');
  webview.setAttribute('src', siteUrl(cfg));

  // Recherche CBIP : champ qui navigue la webview vers la page de résultats.
  if (cfg.search === 'cbip') {
    const input = document.createElement('input');
    input.type = 'text';
    input.placeholder = 'Rechercher un médicament…';
    input.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        const q = input.value.trim();
        if (q) webview.loadURL('https://www.cbip.be/fr/search?q=' + encodeURIComponent(q));
      }
    });
    const goBtn = document.createElement('button');
    goBtn.className = 'btn tiny';
    goBtn.textContent = 'Chercher';
    goBtn.addEventListener('click', () => {
      const q = input.value.trim();
      if (q) webview.loadURL('https://www.cbip.be/fr/search?q=' + encodeURIComponent(q));
    });
    bar.insertBefore(goBtn, bar.querySelector('[data-act="back"]'));
    bar.insertBefore(input, goBtn);
  }

  // Impression du dernier test (InBody).
  if (cfg.print) {
    const printBtn = document.createElement('button');
    printBtn.className = 'btn tiny';
    printBtn.textContent = 'Imprimer le test affiché';
    printBtn.addEventListener('click', () => {
      try {
        webview.print({});
      } catch (err) {
        alert('Impression impossible : ' + err.message);
      }
    });
    bar.insertBefore(printBtn, bar.querySelector('[data-act="external"]'));
  }

  bar.addEventListener('click', (e) => {
    const act = e.target.dataset ? e.target.dataset.act : null;
    if (!act) return;
    if (act === 'back' && webview.canGoBack()) webview.goBack();
    if (act === 'forward' && webview.canGoForward()) webview.goForward();
    if (act === 'reload') webview.reload();
    if (act === 'external') window.prive.openExternal(webview.getURL() || siteUrl(cfg));
  });

  wrap.appendChild(bar);
  wrap.appendChild(webview);
  view.appendChild(wrap);
  container.appendChild(view);
  createdWebviews.add(viewId);
}

// Permet aux autres modules d'obtenir la webview d'un onglet (ex. Gmail compose).
function getWebview(viewId) {
  ensureWebview(viewId);
  const view = document.querySelector(`#webviewViews .view[data-view="${viewId}"]`);
  return view ? view.querySelector('webview') : null;
}

// ---------------------------------------------------------------------------
// Accueil : raccourcis
// ---------------------------------------------------------------------------

function buildHomeTiles() {
  const grid = document.getElementById('homeTiles');
  const quick = [
    'gmail', 'doctena', 'doctoranytime', 'shyfter', 'inbody',
    'clearfacts', 'iballab', 'examens', 'cbip', 'medipost',
    'careconnect', 'recherche', 'mail', 'chatgpt'
  ];
  const labels = {
    careconnect: { title: 'CareConnect', ico: '💻' },
    recherche: { title: 'Recherche fichiers', ico: '🔎' },
    mail: { title: 'Envoyer un mail', ico: '📧' },
    chatgpt: { title: 'Chat IA', ico: '🤖' }
  };
  for (const key of quick) {
    const meta = SITES[key] || labels[key];
    if (!meta) continue;
    const tile = document.createElement('button');
    tile.className = 'tile';
    tile.innerHTML = `<span class="tile-ico">${meta.ico}</span>${meta.title}`;
    tile.addEventListener('click', () => showView(key));
    grid.appendChild(tile);
  }
}

// ---------------------------------------------------------------------------
// Chat IA
// ---------------------------------------------------------------------------

const chatHistory = [];

function appendChat(role, content) {
  const log = document.getElementById('chatLog');
  const div = document.createElement('div');
  div.className = 'msg ' + role;
  div.textContent = content;
  log.appendChild(div);
  log.scrollTop = log.scrollHeight;
  return div;
}

function setupChat() {
  const form = document.getElementById('chatForm');
  const input = document.getElementById('chatInput');
  const sendBtn = document.getElementById('chatSend');

  const submit = async () => {
    const text = input.value.trim();
    if (!text) return;
    appendChat('user', text);
    chatHistory.push({ role: 'user', content: text });
    input.value = '';
    sendBtn.disabled = true;
    const pending = appendChat('assistant', '…');
    try {
      const res = await window.prive.chat(chatHistory);
      if (res.ok) {
        pending.textContent = res.content;
        chatHistory.push({ role: 'assistant', content: res.content });
      } else {
        pending.className = 'msg error';
        pending.textContent = res.error;
      }
    } catch (err) {
      pending.className = 'msg error';
      pending.textContent = 'Erreur : ' + err.message;
    } finally {
      sendBtn.disabled = false;
    }
  };

  form.addEventListener('submit', (e) => {
    e.preventDefault();
    submit();
  });
  input.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      submit();
    }
  });
  document.getElementById('chatClear').addEventListener('click', () => {
    chatHistory.length = 0;
    document.getElementById('chatLog').innerHTML = '';
  });
}

// ---------------------------------------------------------------------------
// Recherche de fichiers
// ---------------------------------------------------------------------------

function setupFileSearch() {
  const form = document.getElementById('fileSearchForm');
  const btn = document.getElementById('fileSearchBtn');
  const hint = document.getElementById('fileHint');
  const results = document.getElementById('fileResults');

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const query = document.getElementById('fileQuery').value.trim();
    if (!query) return;
    const searchContent = document.getElementById('fileContent').checked;
    btn.disabled = true;
    hint.textContent = 'Recherche en cours…';
    results.innerHTML = '';
    try {
      const res = await window.prive.searchFiles({ query, searchContent });
      if (res.error) {
        hint.textContent = res.error;
      } else if (!res.results.length) {
        hint.textContent = 'Aucun résultat.';
      } else {
        hint.textContent =
          `${res.results.length} résultat(s)` + (res.truncated ? ' (liste tronquée, affinez la recherche).' : '.');
        for (const r of res.results) {
          const row = document.createElement('div');
          row.className = 'result-row';
          const icon = r.type === 'folder' ? '📁' : '📄';
          row.innerHTML = `
            <span>${icon}</span>
            <span class="r-name">
              <div>${escapeHtml(r.name)}${r.contentMatch ? ' <span class="r-badge">(contenu)</span>' : ''}</div>
              <div class="r-path">${escapeHtml(r.path)}</div>
            </span>
            <button class="btn tiny" data-open>Ouvrir</button>
            <button class="btn tiny" data-folder>Dossier</button>
          `;
          row.querySelector('[data-open]').addEventListener('click', () => window.prive.openPath(r.path));
          row.querySelector('[data-folder]').addEventListener('click', () => window.prive.showItem(r.path));
          results.appendChild(row);
        }
      }
    } catch (err) {
      hint.textContent = 'Erreur : ' + err.message;
    } finally {
      btn.disabled = false;
    }
  });
}

// ---------------------------------------------------------------------------
// Envoyer un mail (modèles)
// ---------------------------------------------------------------------------

function setupMail() {
  const select = document.getElementById('mailTemplate');
  const subject = document.getElementById('mailSubject');
  const body = document.getElementById('mailBody');
  const to = document.getElementById('mailTo');

  const templates = (settings && settings.emailTemplates) || [];
  select.innerHTML = '<option value="">— Choisir un modèle —</option>';
  templates.forEach((t, i) => {
    const opt = document.createElement('option');
    opt.value = String(i);
    opt.textContent = t.name;
    select.appendChild(opt);
  });

  select.addEventListener('change', () => {
    const t = templates[Number(select.value)];
    if (t) {
      subject.value = t.subject || '';
      body.value = t.body || '';
    }
  });

  document.getElementById('mailGmail').addEventListener('click', () => {
    const url =
      'https://mail.google.com/mail/?view=cm&fs=1' +
      '&to=' + encodeURIComponent(to.value) +
      '&su=' + encodeURIComponent(subject.value) +
      '&body=' + encodeURIComponent(body.value);
    const wv = getWebview('gmail');
    if (wv) wv.loadURL(url);
    showView('gmail');
  });

  document.getElementById('mailClient').addEventListener('click', () => {
    const url =
      'mailto:' + encodeURIComponent(to.value) +
      '?subject=' + encodeURIComponent(subject.value) +
      '&body=' + encodeURIComponent(body.value);
    window.prive.openExternal(url);
  });
}

// ---------------------------------------------------------------------------
// CareConnect
// ---------------------------------------------------------------------------

function setupCareconnect() {
  const btn = document.getElementById('launchCareconnect');
  const hint = document.getElementById('careconnectHint');
  btn.addEventListener('click', async () => {
    hint.textContent = 'Lancement…';
    const res = await window.prive.launchApp('careconnect');
    hint.textContent = res.ok ? 'CareConnect lancé.' : res.error;
  });
}

// ---------------------------------------------------------------------------
// Réglages
// ---------------------------------------------------------------------------

function fillSettingsForm() {
  document.getElementById('setApiKey').value = settings.ai.apiKey || '';
  document.getElementById('setBaseUrl').value = settings.ai.baseUrl || '';
  document.getElementById('setModel').value = settings.ai.model || '';
  document.getElementById('setCareconnect').value = (settings.launchers && settings.launchers.careconnect) || '';
  document.getElementById('setExamsUrl').value = settings.examsUrl || '';
  document.getElementById('setInsecureHosts').value = (settings.allowedInsecureHosts || []).join(', ');
}

function setupSettings() {
  document.getElementById('saveSettings').addEventListener('click', async () => {
    const next = {
      ai: {
        apiKey: document.getElementById('setApiKey').value.trim(),
        baseUrl: document.getElementById('setBaseUrl').value.trim(),
        model: document.getElementById('setModel').value.trim()
      },
      launchers: {
        careconnect: document.getElementById('setCareconnect').value.trim()
      },
      examsUrl: document.getElementById('setExamsUrl').value.trim(),
      allowedInsecureHosts: document.getElementById('setInsecureHosts').value
        .split(',').map((s) => s.trim()).filter(Boolean)
    };
    settings = await window.prive.saveSettings(next);
    const saved = document.getElementById('settingsSaved');
    saved.textContent = 'Enregistré ✓';
    setTimeout(() => (saved.textContent = ''), 2500);
  });
}

// ---------------------------------------------------------------------------
// Barre d'état : test de vitesse
// ---------------------------------------------------------------------------

function setupSpeed() {
  const dot = document.getElementById('netDot');
  const label = document.getElementById('netLabel');
  const latency = document.getElementById('netLatency');
  const down = document.getElementById('netDown');

  const render = (d) => {
    if (!d) return;
    if (d.online) {
      dot.className = 'dot ok';
      label.textContent = 'En ligne';
    } else {
      dot.className = 'dot bad';
      label.textContent = 'Hors ligne';
    }
    latency.textContent = d.latencyMs != null ? `${d.latencyMs} ms` : '— ms';
    down.textContent = d.downloadMbps != null ? `${d.downloadMbps.toFixed(1)} Mbps` : '— Mbps';
  };

  window.prive.onSpeed(render);
  document.getElementById('speedRefresh').addEventListener('click', async () => {
    label.textContent = 'Mesure…';
    render(await window.prive.runSpeedTest());
  });
}

// ---------------------------------------------------------------------------
// Utilitaires
// ---------------------------------------------------------------------------

function escapeHtml(s) {
  return String(s)
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

// ---------------------------------------------------------------------------
// Démarrage
// ---------------------------------------------------------------------------

async function init() {
  settings = await window.prive.getSettings();
  buildNav();
  buildHomeTiles();
  setupChat();
  setupFileSearch();
  setupMail();
  setupCareconnect();
  setupSettings();
  fillSettingsForm();
  setupSpeed();
  showView('accueil');
}

window.addEventListener('DOMContentLoaded', init);
