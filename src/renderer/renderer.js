'use strict';

// ---------------------------------------------------------------------------
// Configuration de la navigation
// ---------------------------------------------------------------------------

// Onglets « site web » (affichés dans une <webview>).
const SITES = {
  gmail: { title: 'Gmail', ico: '✉️', url: 'https://mail.google.com/mail/u/0/?tab=wm&ogbl#inbox' },
  whatsapp: { title: 'WhatsApp', ico: '💬', url: 'https://web.whatsapp.com/' },
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
  medipost: { title: 'Medipost', ico: '📦', url: 'https://www.medipost.shop/' }
};

// Structure du menu latéral (groupes + entrées). « view » pointe vers un
// panneau statique (accueil, chatgpt, …) ou une clé de SITES.
const NAV = [
  { view: 'accueil', title: 'Accueil', ico: '🏠' },
  { group: 'Communication' },
  { view: 'gmail', title: 'Gmail', ico: '✉️', site: true },
  { view: 'whatsapp', title: 'WhatsApp', ico: '💬', site: true },
  { view: 'mail', title: 'Envoyer un mail', ico: '📧' },
  { view: 'modeles', title: 'Modèles', ico: '📄' },
  { view: 'chatgpt', title: 'Chat IA', ico: '🤖' },
  { group: 'Agenda & patients' },
  { view: 'calendrier', title: 'Calendrier & rappels', ico: '📆' },
  { view: 'medecins', title: 'Médecins', ico: '👨‍⚕️' },
  { view: 'doctena', title: 'Doctena', ico: '📅', site: true },
  { view: 'doctoranytime', title: 'Doctoranytime', ico: '🩺', site: true },
  { view: 'sync', title: 'Synchronisation', ico: '🔄' },
  { view: 'shyfter', title: 'Shyfter', ico: '🗓️', site: true },
  { view: 'careconnect', title: 'CareConnect', ico: '💻' },
  { group: 'Examens & labo' },
  { view: 'inbody', title: 'InBody', ico: '⚖️', site: true },
  { view: 'iballab', title: 'IBC Lab online', ico: '🔬', site: true },
  { view: 'examens', title: 'Examens du jour', ico: '📋', site: true },
  { view: 'cbip', title: 'Médicaments (CBIP)', ico: '💊' },
  { group: 'Gestion' },
  { view: 'prestations', title: 'Prestations', ico: '⏱️' },
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
    'gmail', 'whatsapp', 'medecins', 'doctena', 'doctoranytime', 'sync', 'shyfter', 'inbody',
    'clearfacts', 'iballab', 'examens', 'cbip', 'medipost',
    'careconnect', 'calendrier', 'prestations', 'recherche', 'mail', 'modeles', 'chatgpt'
  ];
  const labels = {
    careconnect: { title: 'CareConnect', ico: '💻' },
    recherche: { title: 'Recherche fichiers', ico: '🔎' },
    mail: { title: 'Envoyer un mail', ico: '📧' },
    modeles: { title: 'Modèles', ico: '📄' },
    medecins: { title: 'Médecins', ico: '👨‍⚕️' },
    calendrier: { title: 'Calendrier & rappels', ico: '📆' },
    prestations: { title: 'Prestations', ico: '⏱️' },
    sync: { title: 'Synchronisation', ico: '🔄' },
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

// Recharge la liste déroulante des modèles de mail (catégorie « mail »).
function refreshMailTemplates() {
  const select = document.getElementById('mailTemplate');
  if (!select) return;
  const mails = (settings.correspondence || []).filter((c) => c.category === 'mail');
  select.innerHTML = '<option value="">— Choisir un modèle —</option>';
  mails.forEach((t) => {
    const opt = document.createElement('option');
    opt.value = t.id;
    opt.textContent = t.name;
    select.appendChild(opt);
  });
}

function setupMail() {
  const select = document.getElementById('mailTemplate');
  const subject = document.getElementById('mailSubject');
  const body = document.getElementById('mailBody');
  const to = document.getElementById('mailTo');

  refreshMailTemplates();

  select.addEventListener('change', () => {
    const t = (settings.correspondence || []).find((c) => c.id === select.value);
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
  const pathEl = document.getElementById('ccPath');

  const refreshPath = () => {
    const p = (settings.launchers && settings.launchers.careconnect) || '';
    pathEl.textContent = p || '(non configuré)';
  };
  refreshPath();

  const savePath = async (p) => {
    settings.launchers = settings.launchers || {};
    settings.launchers.careconnect = p;
    await persistSettings();
    refreshPath();
  };

  btn.addEventListener('click', async () => {
    let p = (settings.launchers && settings.launchers.careconnect) || '';
    if (!p) {
      hint.textContent = 'Aucun fichier configuré : détection en cours…';
      const det = await window.prive.detectCareconnect();
      if (det.ok) { await savePath(det.path); p = det.path; }
      else {
        hint.textContent = 'CareConnect introuvable automatiquement. Cliquez sur « Choisir le fichier .exe… ».';
        return;
      }
    }
    hint.textContent = 'Lancement…';
    const res = await window.prive.launchApp('careconnect');
    hint.textContent = res.ok ? 'CareConnect lancé ✓' : res.error;
  });

  document.getElementById('ccDetect').addEventListener('click', async () => {
    hint.textContent = 'Recherche de CareConnect sur C:\\…';
    const det = await window.prive.detectCareconnect();
    if (det.ok) { await savePath(det.path); hint.textContent = 'Trouvé et enregistré ✓'; }
    else hint.textContent = 'Introuvable automatiquement. Utilisez « Choisir le fichier .exe… ».';
  });

  document.getElementById('ccPick').addEventListener('click', async () => {
    const res = await window.prive.pickExe();
    if (res.ok) { await savePath(res.path); hint.textContent = 'Fichier enregistré ✓'; }
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

// Persiste l'objet settings complet (évite d'écraser les autres champs).
async function persistSettings() {
  settings = await window.prive.saveSettings(settings);
}

function setupSettings() {
  document.getElementById('setCareconnectBrowse').addEventListener('click', async () => {
    const res = await window.prive.pickExe();
    if (res.ok) document.getElementById('setCareconnect').value = res.path;
  });

  document.getElementById('saveSettings').addEventListener('click', async () => {
    settings.ai.apiKey = document.getElementById('setApiKey').value.trim();
    settings.ai.baseUrl = document.getElementById('setBaseUrl').value.trim();
    settings.ai.model = document.getElementById('setModel').value.trim();
    settings.launchers = settings.launchers || {};
    settings.launchers.careconnect = document.getElementById('setCareconnect').value.trim();
    settings.examsUrl = document.getElementById('setExamsUrl').value.trim();
    settings.allowedInsecureHosts = document.getElementById('setInsecureHosts').value
      .split(',').map((s) => s.trim()).filter(Boolean);
    await persistSettings();
    const saved = document.getElementById('settingsSaved');
    saved.textContent = 'Enregistré ✓';
    setTimeout(() => (saved.textContent = ''), 2500);
  });
}

// ---------------------------------------------------------------------------
// Modèles de correspondance
// ---------------------------------------------------------------------------

let modeleFilter = 'all';
let modeleCurrentId = null;

function setupModeles() {
  const listEl = document.getElementById('modelesList');
  const filterEl = document.getElementById('modelesFilter');
  const nameEl = document.getElementById('modeleName');
  const catEl = document.getElementById('modeleCategory');
  const subjEl = document.getElementById('modeleSubject');
  const bodyEl = document.getElementById('modeleBody');
  const statusEl = document.getElementById('modeleStatus');

  const catLabel = { mail: 'Mail', rapport: 'Rapport', prescription: 'Prescription', autre: 'Autre' };

  const renderList = () => {
    const items = (settings.correspondence || [])
      .filter((c) => modeleFilter === 'all' || c.category === modeleFilter);
    listEl.innerHTML = '';
    if (!items.length) {
      listEl.innerHTML = '<div class="hint">Aucun modèle dans cette catégorie.</div>';
    }
    items.forEach((c) => {
      const div = document.createElement('div');
      div.className = 'modele-item' + (c.id === modeleCurrentId ? ' active' : '');
      div.innerHTML = `<div class="m-cat">${catLabel[c.category] || c.category}</div><div class="m-name">${escapeHtml(c.name)}</div>`;
      div.addEventListener('click', () => selectModele(c.id));
      listEl.appendChild(div);
    });
  };

  const selectModele = (id) => {
    const c = (settings.correspondence || []).find((x) => x.id === id);
    if (!c) return;
    modeleCurrentId = id;
    nameEl.value = c.name || '';
    catEl.value = c.category || 'autre';
    subjEl.value = c.subject || '';
    bodyEl.value = c.body || '';
    renderList();
  };

  const newModele = () => {
    modeleCurrentId = null;
    nameEl.value = '';
    catEl.value = modeleFilter === 'all' ? 'mail' : modeleFilter;
    subjEl.value = '';
    bodyEl.value = '';
    renderList();
    nameEl.focus();
  };

  filterEl.addEventListener('click', (e) => {
    if (!e.target.dataset.cat) return;
    modeleFilter = e.target.dataset.cat;
    filterEl.querySelectorAll('.chip').forEach((b) =>
      b.classList.toggle('active', b.dataset.cat === modeleFilter));
    renderList();
  });

  document.getElementById('modeleNew').addEventListener('click', newModele);

  document.getElementById('modeleSave').addEventListener('click', async () => {
    const name = nameEl.value.trim();
    if (!name) { statusEl.textContent = 'Nom requis.'; return; }
    settings.correspondence = settings.correspondence || [];
    if (modeleCurrentId) {
      const c = settings.correspondence.find((x) => x.id === modeleCurrentId);
      if (c) { c.name = name; c.category = catEl.value; c.subject = subjEl.value; c.body = bodyEl.value; }
    } else {
      modeleCurrentId = 'c' + Date.now();
      settings.correspondence.push({
        id: modeleCurrentId, name, category: catEl.value,
        subject: subjEl.value, body: bodyEl.value
      });
    }
    await persistSettings();
    refreshMailTemplates();
    renderList();
    statusEl.textContent = 'Enregistré ✓';
    setTimeout(() => (statusEl.textContent = ''), 2000);
  });

  document.getElementById('modeleDelete').addEventListener('click', async () => {
    if (!modeleCurrentId) return;
    settings.correspondence = (settings.correspondence || []).filter((x) => x.id !== modeleCurrentId);
    await persistSettings();
    refreshMailTemplates();
    newModele();
    statusEl.textContent = 'Supprimé.';
    setTimeout(() => (statusEl.textContent = ''), 2000);
  });

  document.getElementById('modeleCopy').addEventListener('click', async () => {
    const txt = (subjEl.value ? subjEl.value + '\n\n' : '') + bodyEl.value;
    try {
      await navigator.clipboard.writeText(txt);
      statusEl.textContent = 'Copié dans le presse-papiers ✓';
    } catch (_) {
      statusEl.textContent = 'Copie impossible.';
    }
    setTimeout(() => (statusEl.textContent = ''), 2000);
  });

  document.getElementById('modeleToMail').addEventListener('click', () => {
    document.getElementById('mailSubject').value = subjEl.value;
    document.getElementById('mailBody').value = bodyEl.value;
    showView('mail');
  });

  renderList();
}

// ---------------------------------------------------------------------------
// Médecins (base locale, recherche + ajout/suppression)
// ---------------------------------------------------------------------------

let medCurrentId = null;

function setupMedecins() {
  const searchEl = document.getElementById('medSearch');
  const listEl = document.getElementById('medList');
  const countEl = document.getElementById('medCount');
  const nameEl = document.getElementById('medName');
  const societeEl = document.getElementById('medSociete');
  const adresseEl = document.getElementById('medAdresse');
  const telEl = document.getElementById('medTel');
  const nissEl = document.getElementById('medNiss');
  const statusEl = document.getElementById('medStatus');

  const renderList = () => {
    const q = searchEl.value.trim().toLowerCase();
    const all = settings.doctors || [];
    const items = all.filter((d) => {
      if (!q) return true;
      return [d.name, d.societe, d.adresse, d.tel, d.niss]
        .filter(Boolean).join(' ').toLowerCase().includes(q);
    });
    countEl.textContent = `${items.length} / ${all.length} médecin(s)`;
    listEl.innerHTML = '';
    if (!items.length) {
      listEl.innerHTML = '<div class="hint">Aucun médecin trouvé.</div>';
    }
    items.forEach((d) => {
      const div = document.createElement('div');
      div.className = 'modele-item' + (d.id === medCurrentId ? ' active' : '');
      div.innerHTML =
        `<div class="m-name">${escapeHtml(d.name)}</div>` +
        `<div class="m-cat">${escapeHtml(d.societe || '—')}</div>`;
      div.addEventListener('click', () => select(d.id));
      listEl.appendChild(div);
    });
  };

  const select = (id) => {
    const d = (settings.doctors || []).find((x) => x.id === id);
    if (!d) return;
    medCurrentId = id;
    nameEl.value = d.name || '';
    societeEl.value = d.societe || '';
    adresseEl.value = d.adresse || '';
    telEl.value = d.tel || '';
    nissEl.value = d.niss || '';
    renderList();
  };

  const blank = () => {
    medCurrentId = null;
    [nameEl, societeEl, adresseEl, telEl, nissEl].forEach((e) => (e.value = ''));
    renderList();
    nameEl.focus();
  };

  searchEl.addEventListener('input', renderList);
  document.getElementById('medNew').addEventListener('click', blank);

  document.getElementById('medSave').addEventListener('click', async () => {
    const name = nameEl.value.trim();
    if (!name) { statusEl.textContent = 'Nom requis.'; return; }
    settings.doctors = settings.doctors || [];
    const data = {
      name,
      societe: societeEl.value.trim(),
      adresse: adresseEl.value.trim(),
      tel: telEl.value.trim(),
      niss: nissEl.value.trim()
    };
    if (medCurrentId) {
      const d = settings.doctors.find((x) => x.id === medCurrentId);
      if (d) Object.assign(d, data);
    } else {
      medCurrentId = 'd' + Date.now();
      settings.doctors.push({ id: medCurrentId, ...data });
    }
    await persistSettings();
    renderList();
    statusEl.textContent = 'Enregistré ✓';
    setTimeout(() => (statusEl.textContent = ''), 2000);
  });

  document.getElementById('medDelete').addEventListener('click', async () => {
    if (!medCurrentId) return;
    settings.doctors = (settings.doctors || []).filter((x) => x.id !== medCurrentId);
    await persistSettings();
    blank();
    statusEl.textContent = 'Supprimé.';
    setTimeout(() => (statusEl.textContent = ''), 2000);
  });

  renderList();
}

// ---------------------------------------------------------------------------
// Prestations (Employés/Étudiants × 2025/2026 ; heures FICHE vs SHYFTER)
// ---------------------------------------------------------------------------

const PREST_MONTHS = ['JANVIER', 'FÉVRIER', 'MARS', 'AVRIL', 'MAI', 'JUIN', 'JUILLET',
  'AOÛT', 'SEPTEMBRE', 'OCTOBRE', 'NOVEMBRE', 'DÉCEMBRE'];
let prestYear = '2025';
let prestType = 'employes';

function prestKey() { return `${prestType}-${prestYear}`; }

function prestDataset() {
  settings.prestations = settings.prestations || {};
  const k = prestKey();
  if (!settings.prestations[k]) settings.prestations[k] = { persons: [] };
  if (!Array.isArray(settings.prestations[k].persons)) settings.prestations[k].persons = [];
  return settings.prestations[k];
}

function emptyMonths() {
  const m = {};
  PREST_MONTHS.forEach((mo) => (m[mo] = { fiche: 0, shyfter: 0 }));
  return m;
}

function renderPrestTable() {
  const table = document.getElementById('prestTable');
  const ds = prestDataset();
  const persons = ds.persons;

  // En-têtes
  let thead = '<thead><tr><th rowspan="2" class="month">Mois</th>';
  persons.forEach((p, i) => {
    thead += `<th colspan="2" class="person">${escapeHtml(p.name)}<span class="rm" data-rm="${i}" title="Supprimer">×</span></th>`;
  });
  thead += '</tr><tr>';
  persons.forEach(() => { thead += '<th class="sub">Fiche</th><th class="sub">Shyfter</th>'; });
  thead += '</tr></thead>';

  // Corps
  let tbody = '<tbody>';
  PREST_MONTHS.forEach((mo) => {
    tbody += `<tr><td class="month">${mo}</td>`;
    persons.forEach((p, i) => {
      const cell = (p.months && p.months[mo]) || { fiche: 0, shyfter: 0 };
      const mism = Number(cell.fiche) !== Number(cell.shyfter);
      tbody += `<td><input type="number" data-p="${i}" data-mo="${mo}" data-f="fiche" value="${cell.fiche}"></td>`;
      tbody += `<td class="${mism ? 'mismatch' : ''}"><input type="number" data-p="${i}" data-mo="${mo}" data-f="shyfter" value="${cell.shyfter}"></td>`;
    });
    tbody += '</tr>';
  });
  tbody += '</tbody>';

  // Totaux
  let tfoot = '<tfoot><tr><td class="month">TOTAL</td>';
  persons.forEach((p) => {
    let tf = 0, ts = 0;
    PREST_MONTHS.forEach((mo) => {
      const c = (p.months && p.months[mo]) || {};
      tf += Number(c.fiche) || 0; ts += Number(c.shyfter) || 0;
    });
    tfoot += `<td>${tf}</td><td>${ts}</td>`;
  });
  tfoot += '</tr></tfoot>';

  table.innerHTML = thead + tbody + tfoot;

  table.querySelectorAll('input[type=number]').forEach((inp) => {
    inp.addEventListener('input', () => {
      const p = persons[Number(inp.dataset.p)];
      if (!p.months) p.months = emptyMonths();
      if (!p.months[inp.dataset.mo]) p.months[inp.dataset.mo] = { fiche: 0, shyfter: 0 };
      p.months[inp.dataset.mo][inp.dataset.f] = inp.value === '' ? 0 : Number(inp.value);
      // Met à jour le surlignage d'écart de la ligne concernée
      const td = inp.closest('td');
      const cell = p.months[inp.dataset.mo];
      const shyftInput = inp.parentElement.parentElement.querySelector(`input[data-p="${inp.dataset.p}"][data-mo="${inp.dataset.mo}"][data-f="shyfter"]`);
      if (shyftInput) {
        shyftInput.closest('td').classList.toggle('mismatch', Number(cell.fiche) !== Number(cell.shyfter));
      }
    });
  });

  table.querySelectorAll('[data-rm]').forEach((el) => {
    el.addEventListener('click', () => {
      const i = Number(el.dataset.rm);
      if (confirm(`Supprimer ${persons[i].name} ?`)) {
        persons.splice(i, 1);
        renderPrestTable();
      }
    });
  });
}

// Cherche une personne (par nom) dans les jeux de l'année courante et
// affiche Fiche vs Shyfter et la différence (Shyfter − Fiche) par mois.
function updatePersonDatalist() {
  const dl = document.getElementById('prestPersonList');
  if (!dl) return;
  const names = new Set();
  ['employes', 'etudiants'].forEach((type) => {
    const ds = (settings.prestations || {})[`${type}-${prestYear}`];
    (ds && ds.persons || []).forEach((p) => names.add(p.name));
  });
  dl.innerHTML = '';
  [...names].sort().forEach((n) => {
    const o = document.createElement('option');
    o.value = n;
    dl.appendChild(o);
  });
}

function renderPersonSearch(query) {
  const box = document.getElementById('prestPersonResult');
  const q = (query || '').trim().toLowerCase();
  if (!q) { box.innerHTML = ''; return; }

  let found = null, foundType = null;
  for (const type of ['employes', 'etudiants']) {
    const ds = (settings.prestations || {})[`${type}-${prestYear}`];
    const p = (ds && ds.persons || []).find((x) => x.name.toLowerCase() === q)
      || (ds && ds.persons || []).find((x) => x.name.toLowerCase().includes(q));
    if (p) { found = p; foundType = type; break; }
  }
  if (!found) {
    box.innerHTML = '<div class="hint">Aucune personne de ce nom pour cette année.</div>';
    return;
  }

  let totF = 0, totS = 0;
  let rows = '';
  PREST_MONTHS.forEach((mo) => {
    const c = (found.months && found.months[mo]) || { fiche: 0, shyfter: 0 };
    const f = Number(c.fiche) || 0, s = Number(c.shyfter) || 0;
    const diff = s - f;
    totF += f; totS += s;
    const cls = diff < 0 ? 'diff-neg' : (diff > 0 ? 'diff-pos' : '');
    const sign = diff > 0 ? '+' : '';
    rows += `<tr><td class="month">${mo}</td><td>${f}</td><td>${s}</td>` +
            `<td class="${cls}">${sign}${diff}</td></tr>`;
  });
  const totDiff = totS - totF;
  const totCls = totDiff < 0 ? 'diff-neg' : (totDiff > 0 ? 'diff-pos' : '');
  const totSign = totDiff > 0 ? '+' : '';
  const typeLabel = foundType === 'employes' ? 'Employé(e)' : 'Étudiant(e)';

  box.innerHTML =
    `<div class="who">${escapeHtml(found.name)} — ${typeLabel} ${prestYear}</div>` +
    '<div class="prest-table-wrap"><table class="prest-table">' +
    '<thead><tr><th class="month">Mois</th><th>Fiche de paie</th><th>Shyfter</th><th>Différence</th></tr></thead>' +
    `<tbody>${rows}</tbody>` +
    `<tfoot><tr><td class="month">TOTAL</td><td>${totF}</td><td>${totS}</td>` +
    `<td class="${totCls}">${totSign}${totDiff}</td></tr></tfoot>` +
    '</table></div>' +
    `<p class="hint">Différence = Shyfter − Fiche. ${totDiff < 0
      ? 'Négatif (rouge) : a travaillé moins d\'heures que la fiche de paie.'
      : (totDiff > 0 ? 'Positif (vert) : a travaillé plus d\'heures que la fiche de paie.'
      : 'À l\'équilibre sur l\'année.')}</p>`;
}

function setupPrestations() {
  const yearSeg = document.getElementById('prestYear');
  const typeSeg = document.getElementById('prestType');
  const status = document.getElementById('prestStatus');
  const personSearch = document.getElementById('prestPersonSearch');

  yearSeg.addEventListener('click', (e) => {
    if (!e.target.dataset.year) return;
    prestYear = e.target.dataset.year;
    yearSeg.querySelectorAll('.chip').forEach((b) => b.classList.toggle('active', b.dataset.year === prestYear));
    renderPrestTable();
    updatePersonDatalist();
    renderPersonSearch(personSearch.value);
  });

  personSearch.addEventListener('input', () => renderPersonSearch(personSearch.value));
  typeSeg.addEventListener('click', (e) => {
    if (!e.target.dataset.type) return;
    prestType = e.target.dataset.type;
    typeSeg.querySelectorAll('.chip').forEach((b) => b.classList.toggle('active', b.dataset.type === prestType));
    renderPrestTable();
  });

  document.getElementById('prestAddPerson').addEventListener('click', () => {
    const name = prompt('Nom de la personne :');
    if (!name) return;
    prestDataset().persons.push({ name: name.trim(), months: emptyMonths() });
    renderPrestTable();
  });

  document.getElementById('prestSave').addEventListener('click', async () => {
    await persistSettings();
    updatePersonDatalist();
    renderPersonSearch(personSearch.value);
    status.textContent = 'Enregistré ✓';
    setTimeout(() => (status.textContent = ''), 2000);
  });

  renderPrestTable();
  updatePersonDatalist();
}

// ---------------------------------------------------------------------------
// Médicaments (CBIP) — recherche native via l'API JSON
// ---------------------------------------------------------------------------

function setupCbip() {
  const input = document.getElementById('cbipQuery');
  const btn = document.getElementById('cbipBtn');
  const status = document.getElementById('cbipStatus');
  const results = document.getElementById('cbipResults');
  const detail = document.getElementById('cbipDetail');

  const doSearch = async () => {
    const q = input.value.trim();
    if (q.length < 2) { status.textContent = 'Tapez au moins 2 caractères.'; return; }
    status.textContent = 'Recherche…';
    results.innerHTML = '';
    const res = await window.prive.cbipSearch(q);
    if (!res.ok) { status.textContent = res.error || 'Erreur.'; return; }
    let count = 0;
    res.groups.forEach((g) => {
      if (!g.items.length) return;
      const grp = document.createElement('div');
      grp.className = 'cbip-group';
      grp.innerHTML = `<h3>${escapeHtml(g.header || 'Résultats')}</h3>`;
      g.items.forEach((it) => {
        count++;
        const el = document.createElement('div');
        el.className = 'cbip-item';
        el.innerHTML = `<div class="n">${escapeHtml(it.name)}</div>` +
          (it.chapter ? `<div class="c">${escapeHtml(it.chapter)}</div>` : '');
        el.addEventListener('click', () => {
          const url = 'https://www.cbip.be/fr/search/' + encodeURIComponent(it.term);
          detail.src = url;
        });
        grp.appendChild(el);
      });
      results.appendChild(grp);
    });
    status.textContent = count ? `${count} résultat(s) — cliquez pour le détail.` : 'Aucun résultat.';
  };

  btn.addEventListener('click', doSearch);
  input.addEventListener('keydown', (e) => { if (e.key === 'Enter') doSearch(); });
}

// ---------------------------------------------------------------------------
// Calendrier & rappels (natif, notification + son)
// ---------------------------------------------------------------------------

let calView = null;         // { y, m } mois affiché
let calSelected = null;     // 'YYYY-MM-DD'

function pad2(n) { return String(n).padStart(2, '0'); }
function dateKey(d) { return `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`; }

function playBeep() {
  try {
    const Ctx = window.AudioContext || window.webkitAudioContext;
    const ctx = new Ctx();
    let t = ctx.currentTime;
    for (let i = 0; i < 3; i++) {
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      osc.type = 'sine';
      osc.frequency.value = 880;
      gain.gain.setValueAtTime(0.001, t);
      gain.gain.exponentialRampToValueAtTime(0.4, t + 0.02);
      gain.gain.exponentialRampToValueAtTime(0.001, t + 0.35);
      osc.connect(gain); gain.connect(ctx.destination);
      osc.start(t); osc.stop(t + 0.36);
      t += 0.45;
    }
    setTimeout(() => ctx.close(), 2000);
  } catch (_) { /* audio indisponible */ }
}

function fireReminder(ev) {
  const typeLabel = { rdv: 'Rendez-vous', tache: 'Tâche', rappel: 'Rappel' }[ev.type] || 'Rappel';
  try {
    new Notification(`${typeLabel} — ${ev.time}`, {
      body: ev.title + (ev.note ? '\n' + ev.note : '')
    });
  } catch (_) { /* notifications indisponibles */ }
  playBeep();
}

function setupCalendar() {
  const grid = document.getElementById('calGrid');
  const title = document.getElementById('calTitle');
  const dayTitle = document.getElementById('calDayTitle');
  const dayEvents = document.getElementById('calDayEvents');
  const upcoming = document.getElementById('calUpcoming');
  const status = document.getElementById('calStatus');

  const MONTHS = ['janvier', 'février', 'mars', 'avril', 'mai', 'juin', 'juillet',
    'août', 'septembre', 'octobre', 'novembre', 'décembre'];
  const DOW = ['Lun', 'Mar', 'Mer', 'Jeu', 'Ven', 'Sam', 'Dim'];

  const now0 = new Date();
  if (!calView) calView = { y: now0.getFullYear(), m: now0.getMonth() };
  if (!calSelected) calSelected = dateKey(now0);

  const eventsFor = (key) => (settings.events || [])
    .filter((e) => e.date === key)
    .sort((a, b) => (a.time || '').localeCompare(b.time || ''));

  const renderGrid = () => {
    const { y, m } = calView;
    title.textContent = `${MONTHS[m]} ${y}`;
    grid.innerHTML = '';
    DOW.forEach((d) => {
      const h = document.createElement('div'); h.className = 'cal-dow'; h.textContent = d; grid.appendChild(h);
    });
    const first = new Date(y, m, 1);
    let startDow = (first.getDay() + 6) % 7; // lundi = 0
    const days = new Date(y, m + 1, 0).getDate();
    const todayKey = dateKey(new Date());
    for (let i = 0; i < startDow; i++) {
      const e = document.createElement('div'); e.className = 'cal-day empty'; grid.appendChild(e);
    }
    for (let d = 1; d <= days; d++) {
      const key = `${y}-${pad2(m + 1)}-${pad2(d)}`;
      const cell = document.createElement('div');
      cell.className = 'cal-day' + (key === todayKey ? ' today' : '') + (key === calSelected ? ' selected' : '');
      const evs = eventsFor(key);
      const dots = evs.slice(0, 4).map((e) => `<span class="cal-dot ${e.type}"></span>`).join('');
      cell.innerHTML = `<span class="dn">${d}</span><span class="evd">${dots}</span>`;
      cell.addEventListener('click', () => { calSelected = key; renderGrid(); renderDay(); });
      grid.appendChild(cell);
    }
  };

  const renderDay = () => {
    const [y, m, d] = calSelected.split('-');
    dayTitle.textContent = `${parseInt(d, 10)} ${MONTHS[parseInt(m, 10) - 1]} ${y}`;
    const evs = eventsFor(calSelected);
    dayEvents.innerHTML = evs.length ? '' : '<p class="hint">Aucun événement ce jour.</p>';
    evs.forEach((e) => {
      const row = document.createElement('div');
      row.className = 'cal-ev';
      row.innerHTML = `<span class="et">${e.time || ''}</span>` +
        `<span class="tag ${e.type}">${e.type}</span>` +
        `<span>${escapeHtml(e.title)}${e.note ? ' — <span class="c">' + escapeHtml(e.note) + '</span>' : ''}</span>` +
        `<span class="ex" title="Supprimer">×</span>`;
      row.querySelector('.ex').addEventListener('click', async () => {
        settings.events = (settings.events || []).filter((x) => x.id !== e.id);
        await persistSettings();
        renderGrid(); renderDay(); renderUpcoming();
      });
      dayEvents.appendChild(row);
    });
  };

  const renderUpcoming = () => {
    const now = new Date();
    const list = (settings.events || [])
      .map((e) => ({ e, dt: new Date(`${e.date}T${e.time || '00:00'}`) }))
      .filter((x) => x.dt >= now)
      .sort((a, b) => a.dt - b.dt)
      .slice(0, 8);
    upcoming.innerHTML = list.length ? '' : '<p class="hint">Aucune échéance à venir.</p>';
    list.forEach(({ e }) => {
      const row = document.createElement('div');
      row.className = 'cal-ev';
      row.innerHTML = `<span class="et">${e.date} ${e.time || ''}</span>` +
        `<span class="tag ${e.type}">${e.type}</span>` +
        `<span>${escapeHtml(e.title)}</span>`;
      upcoming.appendChild(row);
    });
  };

  document.getElementById('calPrev').addEventListener('click', () => {
    calView.m--; if (calView.m < 0) { calView.m = 11; calView.y--; } renderGrid();
  });
  document.getElementById('calNext').addEventListener('click', () => {
    calView.m++; if (calView.m > 11) { calView.m = 0; calView.y++; } renderGrid();
  });
  document.getElementById('calToday').addEventListener('click', () => {
    const t = new Date();
    calView = { y: t.getFullYear(), m: t.getMonth() };
    calSelected = dateKey(t); renderGrid(); renderDay();
  });
  document.getElementById('calTestSound').addEventListener('click', () => {
    fireReminder({ type: 'rappel', time: '', title: 'Test de notification', note: 'Le son fonctionne.' });
  });

  document.getElementById('calAdd').addEventListener('click', async () => {
    const title2 = document.getElementById('calEvTitle').value.trim();
    if (!title2) { status.textContent = 'Titre requis.'; return; }
    const ev = {
      id: 'e' + Date.now(),
      date: calSelected,
      time: document.getElementById('calEvTime').value || '09:00',
      type: document.getElementById('calType').value,
      title: title2,
      note: document.getElementById('calEvNote').value.trim(),
      notified: false
    };
    settings.events = settings.events || [];
    settings.events.push(ev);
    await persistSettings();
    document.getElementById('calEvTitle').value = '';
    document.getElementById('calEvNote').value = '';
    renderGrid(); renderDay(); renderUpcoming();
    status.textContent = 'Ajouté ✓';
    setTimeout(() => (status.textContent = ''), 1500);
  });

  renderGrid(); renderDay(); renderUpcoming();

  // Moteur de rappels : vérifie chaque 20 s.
  setInterval(async () => {
    const now = Date.now();
    let changed = false;
    (settings.events || []).forEach((e) => {
      if (e.notified) return;
      const dt = new Date(`${e.date}T${e.time || '00:00'}`).getTime();
      if (isNaN(dt)) return;
      if (now >= dt) {
        // Ne sonne que si l'échéance est récente (≤ 2 min), sinon marque comme passée.
        if (now - dt <= 120000) fireReminder(e);
        e.notified = true;
        changed = true;
      }
    });
    if (changed) { await persistSettings(); renderUpcoming(); }
  }, 20000);
}

// ---------------------------------------------------------------------------
// Synchronisation Doctena ↔ Doctoranytime (lecture + comparaison)
// ---------------------------------------------------------------------------

// Récupère le texte visible d'une webview (y compris iframes de même origine).
async function readAgendaText(viewId) {
  const wv = getWebview(viewId);
  if (!wv) return '';
  const code = `(function(){
    function grab(doc){ try { return (doc.body && doc.body.innerText) || ''; } catch(e){ return ''; } }
    var txt = grab(document);
    var frames = document.querySelectorAll('iframe');
    for (var i=0;i<frames.length;i++){ try { txt += '\\n' + grab(frames[i].contentDocument); } catch(e){} }
    return txt;
  })()`;
  try {
    return await wv.executeJavaScript(code, true);
  } catch (_) {
    return '';
  }
}

function cleanApptName(s) {
  s = String(s).split('/')[0];                 // enlève téléphone/email après « / »
  s = s.replace(/phone\s*:.*$/i, '').replace(/email\s*:.*$/i, '');
  s = s.replace(/[@*•]/g, ' ');
  s = s.replace(/\b(dr|dre|mme|mr|mlle|m)\b\.?/gi, ' ');
  s = s.replace(/\+?\d[\d\s().-]{6,}\d/g, ' '); // numéros de téléphone
  return s.replace(/\s+/g, ' ').trim();
}

function normName(s) {
  return cleanApptName(s).toLowerCase()
    .normalize('NFD').replace(/[̀-ͯ]/g, '')
    .replace(/[^a-z0-9\s]/g, ' ')
    .split(/\s+/).filter((w) => w.length > 1).sort().join(' ');
}

function parseAppts(text) {
  const out = [];
  (text || '').split('\n').forEach((raw) => {
    const line = raw.trim();
    const m = line.match(/(\d{1,2})[:h.](\d{2})/);
    if (!m) return;
    const time = m[1].padStart(2, '0') + ':' + m[2];
    let rest = line.slice(line.indexOf(m[0]) + m[0].length).trim();
    if (!rest) return;
    const name = cleanApptName(rest);
    const norm = normName(rest);
    if (name && norm) out.push({ time, name, norm });
  });
  return out;
}

function compareAgendas(aList, bList) {
  const usedB = new Array(bList.length).fill(false);
  const matched = [];       // même personne, même heure
  const timeDiff = [];      // même personne, heure différente
  const onlyA = [];         // présent seulement dans Doctena

  aList.forEach((a) => {
    // priorité à une correspondance exacte (nom + heure)
    let idx = bList.findIndex((b, i) => !usedB[i] && b.norm === a.norm && b.time === a.time);
    if (idx >= 0) { usedB[idx] = true; matched.push({ a, b: bList[idx] }); return; }
    idx = bList.findIndex((b, i) => !usedB[i] && b.norm === a.norm);
    if (idx >= 0) { usedB[idx] = true; timeDiff.push({ a, b: bList[idx] }); return; }
    onlyA.push(a);
  });
  const onlyB = bList.filter((_, i) => !usedB[i]);

  // Doublons internes (même personne 2× dans un même agenda)
  const dupes = [];
  [['Doctena', aList], ['Doctoranytime', bList]].forEach(([src, list]) => {
    const seen = {};
    list.forEach((x) => {
      seen[x.norm] = (seen[x.norm] || 0) + 1;
      if (seen[x.norm] === 2) dupes.push({ src, name: x.name });
    });
  });

  return { matched, timeDiff, onlyA, onlyB, dupes };
}

function renderSyncResult(res, aCount, bCount) {
  const box = document.getElementById('syncResult');
  const esc = escapeHtml;
  let html = '<div class="sync-summary">' +
    `<span class="sync-badge">Doctena : ${aCount} RDV</span>` +
    `<span class="sync-badge">Doctoranytime : ${bCount} RDV</span>` +
    `<span class="sync-badge ok">✓ ${res.matched.length} concordants</span>` +
    `<span class="sync-badge warn">⏰ ${res.timeDiff.length} écarts d'horaire</span>` +
    `<span class="sync-badge bad">✗ ${res.onlyA.length + res.onlyB.length} manquants</span>` +
    '</div>';

  const group = (cls, title, rows) => {
    if (!rows.length) return '';
    return `<div class="sync-group ${cls}"><h3>${title} (${rows.length})</h3>${rows.join('')}</div>`;
  };

  html += group('bad', '⛔ Présents dans Doctena mais ABSENTS de Doctoranytime',
    res.onlyA.map((a) => `<div class="sync-row"><span class="t">${a.time}</span><span>${esc(a.name)}</span></div>`));
  html += group('bad', '⛔ Présents dans Doctoranytime mais ABSENTS de Doctena',
    res.onlyB.map((b) => `<div class="sync-row"><span class="t">${b.time}</span><span>${esc(b.name)}</span></div>`));
  html += group('warn', '⏰ Même patient, horaire différent',
    res.timeDiff.map((p) => `<div class="sync-row"><span class="t">${p.a.time}→${p.b.time}</span><span>${esc(p.a.name)}</span></div>`));
  html += group('warn', '⚠️ Doublons dans un même agenda',
    res.dupes.map((d) => `<div class="sync-row"><span>${esc(d.name)}</span><span class="arrow">— ${d.src}</span></div>`));
  html += group('ok', '✓ Rendez-vous concordants',
    res.matched.map((p) => `<div class="sync-row"><span class="t">${p.a.time}</span><span>${esc(p.a.name)}</span></div>`));

  if (!res.matched.length && !res.timeDiff.length && !res.onlyA.length && !res.onlyB.length) {
    html += '<p class="hint">Aucun rendez-vous détecté. Vérifiez que les deux onglets sont bien ouverts sur la vue « Jour » du praticien, puis relancez.</p>';
  }
  box.innerHTML = html;
}

function setupSync() {
  const status = document.getElementById('syncStatus');
  document.getElementById('syncOpenDoctena').addEventListener('click', () => showView('doctena'));
  document.getElementById('syncOpenDa').addEventListener('click', () => showView('doctoranytime'));

  document.getElementById('syncCompare').addEventListener('click', async () => {
    status.textContent = 'Lecture des agendas…';
    const [ta, tb] = await Promise.all([readAgendaText('doctena'), readAgendaText('doctoranytime')]);
    const aList = parseAppts(ta);
    const bList = parseAppts(tb);
    if (!ta && !tb) {
      status.textContent = 'Impossible de lire les agendas. Ouvrez d\'abord les onglets Doctena et Doctoranytime.';
      return;
    }
    const res = compareAgendas(aList, bList);
    renderSyncResult(res, aList.length, bList.length);
    const problems = res.onlyA.length + res.onlyB.length + res.timeDiff.length + res.dupes.length;
    status.textContent = problems === 0
      ? 'Agendas synchronisés ✓'
      : `${problems} anomalie(s) détectée(s).`;
  });
}

// ---------------------------------------------------------------------------
// Bandeau défilant : date, heure, température
// ---------------------------------------------------------------------------

function setupMarquee() {
  const track = document.getElementById('marqueeTrack');
  let temp = null;
  let tempLabel = '';

  const jours = ['dimanche', 'lundi', 'mardi', 'mercredi', 'jeudi', 'vendredi', 'samedi'];
  const mois = ['janvier', 'février', 'mars', 'avril', 'mai', 'juin', 'juillet',
    'août', 'septembre', 'octobre', 'novembre', 'décembre'];

  const render = () => {
    const d = new Date();
    const date = `${jours[d.getDay()]} ${d.getDate()} ${mois[d.getMonth()]} ${d.getFullYear()}`;
    const heure = d.toLocaleTimeString('fr-BE');
    const tempStr = temp != null ? `🌡️ ${tempLabel} : ${temp.toFixed(1)}°C` : '🌡️ température…';
    const block =
      `<span>📅 ${date}</span><span class="sep">•</span>` +
      `<span>🕐 ${heure}</span><span class="sep">•</span>` +
      `<span>${tempStr}</span><span class="sep">•</span>` +
      `<span>IATECH-CONTROL PRO</span><span class="sep">•</span>`;
    // Doublé pour un défilement continu et sans trou.
    track.innerHTML = block + block;
  };

  const fetchWeather = async () => {
    try {
      const w = await window.prive.getWeather();
      if (w && w.ok) { temp = w.temp; tempLabel = w.label || ''; }
    } catch (_) { /* ignore */ }
  };

  render();
  setInterval(render, 1000);
  fetchWeather();
  setInterval(fetchWeather, 15 * 60 * 1000); // toutes les 15 min
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
  setupModeles();
  setupMedecins();
  setupPrestations();
  setupCbip();
  setupCalendar();
  setupSync();
  setupCareconnect();
  setupSettings();
  fillSettingsForm();
  setupSpeed();
  setupMarquee();
  showView('accueil');
}

window.addEventListener('DOMContentLoaded', init);
