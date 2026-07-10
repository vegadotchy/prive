'use strict';

// ---------------------------------------------------------------------------
// Configuration de la navigation
// ---------------------------------------------------------------------------

// Onglets « site web » (affichés dans une <webview>).
const SITES = {
  helpdesk: { title: 'Help desk', ico: '📞', url: 'https://c074ad90dcce.a.gdms.cloud/click2call/?from_user=webrtc_trunk_2&to_user=service' },
  gmail: { title: 'Gmail', ico: '✉️', url: 'https://mail.google.com/mail/u/0/?tab=wm&ogbl#inbox' },
  whatsapp: {
    title: 'WhatsApp', ico: '💬', url: 'https://web.whatsapp.com/',
    ua: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36'
  },
  doctena: { title: 'Doctena', ico: '📅', url: 'https://secure.doctena.com/' },
  doctoranytime: {
    title: 'Doctoranytime', ico: '🩺',
    url: 'https://www.doctoranytime.be/doctorcrmV2/ConnectedAccount/ChangeAccount?u=0'
  },
  clearfacts: { title: 'ClearFacts / Kyte', ico: '🧾', url: 'https://mbm.clearfacts.be/login' },
  iballab: { title: 'IBC Lab online', ico: '🔬', url: 'https://labonline.lhub-ulb.be/', labSearch: true },
  examens: { title: 'Rapport radiologie', ico: '📋', urlFromSettings: 'examsUrl', radioZip: true },
  medipost: { title: 'Medipost', ico: '📦', url: 'https://www.medipost.shop/' }
};

// Structure du menu latéral (groupes + entrées). « view » pointe vers un
// panneau statique (accueil, chatgpt, …) ou une clé de SITES.
const NAV = [
  { view: 'accueil', title: 'Accueil', ico: '🏠' },
  { view: 'helpdesk', title: 'Contacter le help desk', ico: '📞', site: true },
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
  { view: 'shyfter', title: 'Shyfter', ico: '🗓️' },
  { view: 'careconnect', title: 'CareConnect', ico: '💻' },
  { group: 'Examens & labo' },
  { view: 'inbody', title: 'InBody', ico: '⚖️' },
  { view: 'iballab', title: 'IBC Lab online', ico: '🔬', site: true },
  { view: 'examens', title: 'Rapport radiologie', ico: '📋', site: true },
  { view: 'cbip', title: 'Médicaments (CBIP)', ico: '💊' },
  { group: 'Gestion' },
  { view: 'prestations', title: 'Prestations', ico: '⏱️' },
  { view: 'clearfacts', title: 'ClearFacts / Kyte', ico: '🧾', site: true },
  { view: 'medipost', title: 'Medipost', ico: '📦', site: true },
  { group: 'Outils' },
  { view: 'coffre', title: 'Coffre-fort', ico: '🔐' },
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

// Charge (une seule fois) les deux agendas intégrés de la vue Synchronisation.
let syncPanesLoaded = false;
function ensureSyncPanes() {
  if (syncPanesLoaded) return;
  ['syncWvDoctena', 'syncWvDa'].forEach((id) => {
    const wv = document.getElementById(id);
    if (wv && wv.dataset.src) wv.src = wv.dataset.src;
  });
  syncPanesLoaded = true;
}

let inbodyLoaded = false;
function ensureInbodyPanes() {
  if (inbodyLoaded) return;
  ['inbodyWv', 'inbodyGptWv'].forEach((id) => {
    const wv = document.getElementById(id);
    if (wv && wv.dataset.src) wv.src = wv.dataset.src;
  });
  inbodyLoaded = true;
}

let shyPaneLoaded = false;
function ensureShyfterPane() {
  if (shyPaneLoaded) return;
  const wv = document.getElementById('shyWv');
  if (wv && wv.dataset.src) { wv.src = wv.dataset.src; shyPaneLoaded = true; }
}

let lastSiteView = null;
function showView(viewId) {
  // Onglet site web : créé à la demande.
  if (SITES[viewId]) { ensureWebview(viewId); lastSiteView = viewId; }
  if (viewId === 'sync') ensureSyncPanes();
  if (viewId === 'shyfter') ensureShyfterPane();
  if (viewId === 'inbody') ensureInbodyPanes();

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

// Capture UNIQUEMENT l'image médicale principale de la série (le grand
// visualiseur), image par image, en parcourant la série (flèche droite +
// molette). N'inclut PAS les icônes, vignettes ni logos.
const RADIO_CAPTURE_JS = `(async function(){
  function png(el){
    try{
      if(el.tagName==='CANVAS'){ return el.toDataURL('image/png'); }
      var w=el.naturalWidth||el.width, h=el.naturalHeight||el.height;
      if(!w||!h) return null;
      var c=document.createElement('canvas'); c.width=w; c.height=h;
      c.getContext('2d').drawImage(el,0,0,w,h); return c.toDataURL('image/png');
    }catch(e){ return null; }
  }
  // Le plus grand média affiché (canvas ou img) = le visualiseur principal.
  // Un seuil élimine les icônes / vignettes / logos.
  function mainMedia(){
    var els=document.querySelectorAll('canvas,img'), best=null, ba=0;
    for(var i=0;i<els.length;i++){
      var el=els[i]; var r; try{ r=el.getBoundingClientRect(); }catch(e){ continue; }
      var a=r.width*r.height;
      if(r.width<220 || r.height<220) continue;      // trop petit = icône/vignette
      if(a>ba){ ba=a; best=el; }
    }
    return best;
  }
  function wait(ms){ return new Promise(function(r){ setTimeout(r,ms); }); }
  function reportText(){
    var t=(document.body.innerText||'').split('\\n').map(function(l){return l.trim();}).filter(Boolean);
    var out=[],seen={};
    for(var i=0;i<t.length && out.length<50;i++){ if(!seen[t[i]]){ seen[t[i]]=1; out.push(t[i]); } }
    return out.join('\\n');
  }
  var total=1, mt=(document.body.innerText||'').match(/\\b(\\d{1,3})\\s*\\/\\s*(\\d{1,3})\\b/);
  if(mt){ total=parseInt(mt[2],10)||1; }
  if(total>80) total=80;
  var frames=[], seen={};
  function grab(){
    var el=mainMedia();
    var d=el?png(el):null;
    if(d && !seen[d]){ seen[d]=1; frames.push({ name:'image_'+('0'+(frames.length+1)).slice(-2)+'.png', dataUrl:d }); return true; }
    return false;
  }
  grab();
  var stagnate=0;
  for(var i=1;i<total && stagnate<5;i++){
    var vp=mainMedia()||document.body;
    try{
      ['keydown','keyup'].forEach(function(tp){
        var ev={key:'ArrowRight',code:'ArrowRight',keyCode:39,which:39,bubbles:true};
        vp.dispatchEvent(new KeyboardEvent(tp,ev));
        document.dispatchEvent(new KeyboardEvent(tp,ev));
      });
      vp.dispatchEvent(new WheelEvent('wheel',{deltaY:120,bubbles:true}));
    }catch(e){}
    await wait(700);
    if(grab()) stagnate=0; else stagnate++;
  }
  return JSON.stringify({ frames:frames, report:reportText(), total:total });
})()`;

async function downloadRadioZip(webview, btn) {
  const prev = btn.textContent;
  btn.disabled = true;
  btn.textContent = 'Capture des images…';
  let payload;
  try {
    payload = await webview.executeJavaScript(RADIO_CAPTURE_JS, true);
  } catch (e) {
    btn.disabled = false; btn.textContent = prev;
    alert('Capture impossible : ' + e.message);
    return;
  }
  let data = null;
  try { data = JSON.parse(payload); } catch (_) { /* ignore */ }
  if (!data || !data.frames || !data.frames.length) {
    btn.disabled = false; btn.textContent = prev;
    alert('Aucune image exportable détectée.\n\nOuvrez d’abord l’image/le rapport dans le visualiseur, laissez-la s’afficher, puis réessayez. (Certaines images protégées par le visualiseur peuvent ne pas être exportables.)');
    return;
  }
  const total = data.total || data.frames.length;
  if (data.frames.length < total) {
    btn.textContent = `Création du ZIP (${data.frames.length}/${total})…`;
  } else {
    btn.textContent = `Création du ZIP (${data.frames.length} img)…`;
  }
  let res;
  try {
    res = await window.prive.saveRadioZip({ frames: data.frames, report: data.report || '' });
  } catch (e) {
    res = { ok: false, reason: e.message };
  }
  btn.disabled = false; btn.textContent = prev;
  if (res && res.ok) {
    const missed = total > data.frames.length
      ? `\n\nNote : ${total - data.frames.length} image(s) de la série n’ont pas pu être parcourues automatiquement. Faites défiler la série dans le visualiseur puis relancez pour les ajouter.`
      : '';
    alert(`ZIP enregistré ✓ (${res.count} fichier(s)).${missed}`);
  } else if (res && res.canceled) {
    /* annulé par l'utilisateur */
  } else {
    alert('Enregistrement du ZIP impossible' + (res && res.reason ? ' : ' + res.reason : '') + '.');
  }
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
  // User-Agent « Chrome de bureau » pour les sites qui refusent la signature
  // Electron (WhatsApp Web exige Chrome 100+).
  if (cfg.ua) webview.setAttribute('useragent', cfg.ua);
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

  // Ouvre puis imprime automatiquement le dernier test (ligne la plus récente).
  if (cfg.printLast) {
    const lastBtn = document.createElement('button');
    lastBtn.className = 'btn tiny';
    lastBtn.textContent = '🖨️ Imprimer le dernier test';
    lastBtn.title = 'Ouvre le rapport du test le plus récent (haut de la liste) puis lance l’impression';
    lastBtn.addEventListener('click', async () => {
      const prev = lastBtn.textContent;
      lastBtn.textContent = 'Ouverture du dernier test…';
      lastBtn.disabled = true;
      // Heuristique : clique l'icône « Report » de la 1re ligne de données
      // (la liste LookinBody est triée du test le plus récent au plus ancien).
      const script = `(function(){
        function firstRow(){
          var rows = document.querySelectorAll('tbody tr, [role="row"]');
          for (var i=0;i<rows.length;i++){
            if (rows[i].querySelector('td, [role="cell"], [role="gridcell"]')) return rows[i];
          }
          return null;
        }
        var row = firstRow();
        if(!row) return 'norow';
        var els = row.querySelectorAll('a,button,img,svg,i,[role="button"]');
        var target=null;
        for (var i=0;i<els.length;i++){
          var el=els[i];
          var s=((el.getAttribute&&(el.getAttribute('title')||el.getAttribute('alt')||el.getAttribute('aria-label')||el.getAttribute('data-tooltip')))||'')+' '+(el.className&&el.className.baseVal!==undefined?el.className.baseVal:(el.className||''));
          if(/report|rapport/i.test(s)){ target=el; break; }
        }
        if(!target){
          // Repli : dans les colonnes d'icônes, la 2e icône correspond souvent à « Report »
          var icons = row.querySelectorAll('td a, td button, td img, td svg, [role="cell"] a, [role="cell"] button');
          if(icons.length>=2) target=icons[1];
          else if(icons.length===1) target=icons[0];
        }
        if(target){ (target.closest('a,button')||target).click(); return 'clicked'; }
        return 'notfound';
      })()`;
      let outcome = 'notfound';
      try { outcome = await webview.executeJavaScript(script, true); } catch (_) { outcome = 'error'; }
      if (outcome === 'clicked') {
        // Laisse le rapport se charger avant d'imprimer.
        setTimeout(() => {
          try { webview.print({}); } catch (_) { /* ignore */ }
          lastBtn.textContent = prev;
          lastBtn.disabled = false;
        }, 2800);
      } else {
        lastBtn.textContent = prev;
        lastBtn.disabled = false;
        alert('Impossible d’ouvrir automatiquement le dernier test. Ouvrez son rapport puis utilisez « Imprimer le test affiché ».');
      }
    });
    bar.insertBefore(lastBtn, bar.querySelector('[data-act="external"]'));
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

  // Rapport radiologie : télécharge toutes les images de la série + le rapport en ZIP.
  if (cfg.radioZip) {
    const zipBtn = document.createElement('button');
    zipBtn.className = 'btn tiny gold';
    zipBtn.textContent = '⬇️ Tout télécharger (ZIP)';
    zipBtn.title = 'Parcourt toutes les images de la série affichée, les capture et les enregistre avec le rapport dans un fichier ZIP';
    zipBtn.addEventListener('click', () => downloadRadioZip(webview, zipBtn));
    bar.insertBefore(zipBtn, bar.querySelector('[data-act="external"]'));
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

  // Barre de recherche native pour IBC Lab (remplit et valide le formulaire du site).
  if (cfg.labSearch) {
    const sub = document.createElement('div');
    sub.className = 'web-subbar';
    sub.innerHTML = `
      <span class="sub-label">🔬 Rechercher une analyse :</span>
      <input type="text" data-f="nom" placeholder="Nom de famille" />
      <input type="text" data-f="prenom" placeholder="Prénom" />
      <input type="text" data-f="dob" placeholder="Naissance jj/mm/aaaa" />
      <input type="text" data-f="niss" placeholder="NISS" />
      <button class="btn tiny gold" data-act="labsearch">Rechercher</button>
      <button class="btn tiny" data-act="labreset">Effacer</button>
      <span class="hint" data-el="labstatus"></span>
    `;
    const val = (f) => (sub.querySelector(`input[data-f="${f}"]`).value || '').trim();
    const status = sub.querySelector('[data-el="labstatus"]');

    const runSearch = async () => {
      const nom = val('nom'), prenom = val('prenom'), dob = val('dob'), niss = val('niss');
      if (!nom && !prenom && !dob && !niss) { status.textContent = 'Saisissez au moins un critère.'; return; }
      status.textContent = 'Recherche…';
      const script = `(function(nom, prenom, dob, niss){
        function setVal(inp, value){
          try {
            var proto = window.HTMLInputElement && window.HTMLInputElement.prototype;
            var setter = proto && Object.getOwnPropertyDescriptor(proto,'value').set;
            if (setter) setter.call(inp, value); else inp.value = value;
          } catch(e){ inp.value = value; }
          ['input','change','keyup','blur'].forEach(function(t){
            inp.dispatchEvent(new Event(t,{bubbles:true}));
          });
        }
        function setByLabel(labelText, value){
          if(value===undefined||value==='') return false;
          var nodes = document.querySelectorAll('label,td,th,span,div');
          var lab=null;
          for (var i=0;i<nodes.length;i++){
            var t=(nodes[i].textContent||'').trim().replace(/[:*]/g,'').toLowerCase();
            if(t===labelText.toLowerCase()){ lab=nodes[i]; break; }
          }
          if(!lab) return false;
          var scope=lab.parentElement;
          for (var d=0; d<5 && scope; d++){
            var inp=scope.querySelector('input:not([type=hidden]):not([readonly]):not([disabled])');
            if(inp){ setVal(inp, value); return true; }
            scope=scope.parentElement;
          }
          return false;
        }
        var r={};
        r.nom=setByLabel('Nom de famille', nom);
        r.prenom=setByLabel('Prénom', prenom);
        r.dob=setByLabel('Date de naissance', dob);
        r.niss= niss ? setByLabel('Code', niss) : false;
        var applied=false;
        var els=document.querySelectorAll('button,a,span,input[type=button],input[type=submit]');
        for (var i=0;i<els.length;i++){
          var tx=(els[i].textContent||els[i].value||'').trim();
          if(/^appliquer$/i.test(tx)){ (els[i].closest('button,a')||els[i]).click(); applied=true; break; }
        }
        r.applied=applied;
        return JSON.stringify(r);
      })(${JSON.stringify(nom)}, ${JSON.stringify(prenom)}, ${JSON.stringify(dob)}, ${JSON.stringify(niss)})`;

      let out = {};
      try { out = JSON.parse(await webview.executeJavaScript(script, true)); } catch (_) { out = {}; }
      if (out.applied) {
        status.textContent = 'Recherche lancée ✓ (résultats ci-dessous)';
      } else {
        const filled = out.nom || out.prenom || out.dob || out.niss;
        status.textContent = filled
          ? 'Champs remplis, mais bouton « Appliquer » introuvable — cliquez-le sur la page.'
          : 'Formulaire introuvable : ouvrez « Mes patients » sur la page, puis relancez.';
      }
    };

    sub.addEventListener('click', (e) => {
      const act = e.target.dataset ? e.target.dataset.act : null;
      if (act === 'labsearch') runSearch();
      if (act === 'labreset') sub.querySelectorAll('input').forEach((i) => (i.value = ''));
    });
    sub.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' && e.target.tagName === 'INPUT') runSearch();
    });
    wrap.appendChild(sub);
  }

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
    'careconnect', 'calendrier', 'prestations', 'coffre', 'recherche', 'mail', 'modeles', 'chatgpt'
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
    shyfter: { title: 'Shyfter', ico: '🗓️' },
    coffre: { title: 'Coffre-fort', ico: '🔐' },
    inbody: { title: 'InBody', ico: '⚖️' },
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

  document.getElementById('ccDownload').addEventListener('click', () => {
    window.prive.openExternal('https://services.careconnect.be/client/6.4/otherplatforms.html');
  });

  // --- Opal Vision (lanceur d'exécutable local) ---
  const opalHint = document.getElementById('opalHint');
  const opalPathEl = document.getElementById('opalPath');
  const refreshOpal = () => {
    opalPathEl.textContent = (settings.launchers && settings.launchers.opalvision) || '(non configuré)';
  };
  refreshOpal();
  document.getElementById('launchOpal').addEventListener('click', async () => {
    opalHint.textContent = 'Lancement…';
    const res = await window.prive.launchApp('opalvision');
    opalHint.textContent = res.ok ? 'Opal Vision lancé ✓'
      : (res.error || '') + ' — cliquez « Choisir le fichier .exe… » pour indiquer le bon programme.';
  });
  document.getElementById('opalPick').addEventListener('click', async () => {
    const res = await window.prive.pickExe();
    if (res.ok) {
      settings.launchers = settings.launchers || {};
      settings.launchers.opalvision = res.path;
      await persistSettings();
      refreshOpal();
      opalHint.textContent = 'Fichier enregistré ✓';
    }
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
  document.getElementById('setGoogleId').value = (settings.google && settings.google.clientId) || '';
  document.getElementById('setGoogleSecret').value = (settings.google && settings.google.clientSecret) || '';
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
    settings.google = settings.google || {};
    settings.google.clientId = document.getElementById('setGoogleId').value.trim();
    settings.google.clientSecret = document.getElementById('setGoogleSecret').value.trim();
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
let modeleAttachments = [];

function setupModeles() {
  const listEl = document.getElementById('modelesList');
  const filterEl = document.getElementById('modelesFilter');
  const nameEl = document.getElementById('modeleName');
  const catEl = document.getElementById('modeleCategory');
  const subjEl = document.getElementById('modeleSubject');
  const bodyEl = document.getElementById('modeleBody');
  const statusEl = document.getElementById('modeleStatus');

  const catLabel = { mail: 'Mail', rapport: 'Rapport', prescription: 'Prescription', autre: 'Autre' };
  const searchEl = document.getElementById('modeleSearch');
  let modeleQuery = '';

  const norm = (s) => (s || '').toString().toLowerCase()
    .normalize('NFD').replace(/[̀-ͯ]/g, '');

  const renderList = () => {
    const q = norm(modeleQuery).trim();
    const terms = q ? q.split(/\s+/) : [];
    const items = (settings.correspondence || [])
      .filter((c) => modeleFilter === 'all' || c.category === modeleFilter)
      .filter((c) => {
        if (!terms.length) return true;
        const hay = norm(`${c.name} ${c.subject} ${c.body} ${catLabel[c.category] || c.category}`);
        return terms.every((t) => hay.includes(t));   // tous les mots doivent être présents
      });
    listEl.innerHTML = '';
    if (!items.length) {
      listEl.innerHTML = terms.length
        ? '<div class="hint">Aucun modèle ne correspond à cette recherche.</div>'
        : '<div class="hint">Aucun modèle dans cette catégorie.</div>';
    }
    items.forEach((c) => {
      const div = document.createElement('div');
      div.className = 'modele-item' + (c.id === modeleCurrentId ? ' active' : '');
      div.innerHTML = `<div class="m-cat">${catLabel[c.category] || c.category}</div><div class="m-name">${escapeHtml(c.name)}</div>`;
      div.addEventListener('click', () => selectModele(c.id));
      listEl.appendChild(div);
    });
  };

  const listEl2 = document.getElementById('modeleAttachList');
  const renderAttach = () => {
    listEl2.innerHTML = '';
    if (!modeleAttachments.length) {
      listEl2.innerHTML = '<span class="hint">Aucun document joint.</span>';
      return;
    }
    modeleAttachments.forEach((f, i) => {
      const row = document.createElement('div');
      row.className = 'attach-item';
      row.innerHTML = `<span class="af">📄 ${escapeHtml(f.name)}</span>` +
        '<button class="btn tiny" data-a="open">Ouvrir</button>' +
        '<button class="btn tiny" data-a="text">Insérer le texte</button>' +
        '<button class="btn tiny danger" data-a="rm">×</button>';
      row.querySelector('[data-a="open"]').addEventListener('click', () => window.prive.openPath(f.path));
      row.querySelector('[data-a="text"]').addEventListener('click', async () => {
        const r = await window.prive.extractText(f.path);
        if (r.ok) {
          bodyEl.value = (bodyEl.value ? bodyEl.value + '\n\n' : '') + r.text;
        } else {
          alert(r.reason || 'Extraction impossible.');
        }
      });
      row.querySelector('[data-a="rm"]').addEventListener('click', () => {
        modeleAttachments.splice(i, 1); renderAttach();
      });
      listEl2.appendChild(row);
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
    modeleAttachments = (c.attachments || []).slice();
    renderList(); renderAttach();
  };

  const newModele = () => {
    modeleCurrentId = null;
    nameEl.value = '';
    catEl.value = modeleFilter === 'all' ? 'mail' : modeleFilter;
    subjEl.value = '';
    bodyEl.value = '';
    modeleAttachments = [];
    renderList(); renderAttach();
  };

  document.getElementById('modeleAttach').addEventListener('click', async () => {
    const r = await window.prive.addAttachments();
    if (r.ok) { modeleAttachments.push(...r.files); renderAttach(); }
  });

  searchEl.addEventListener('input', () => { modeleQuery = searchEl.value; renderList(); });

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
      if (c) {
        c.name = name; c.category = catEl.value; c.subject = subjEl.value; c.body = bodyEl.value;
        c.attachments = modeleAttachments.slice();
      }
    } else {
      modeleCurrentId = 'c' + Date.now();
      settings.correspondence.push({
        id: modeleCurrentId, name, category: catEl.value,
        subject: subjEl.value, body: bodyEl.value,
        attachments: modeleAttachments.slice()
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

  // --- Coller un texte / mail et le transformer en modèle -------------------
  const pasteEl = document.getElementById('modelePaste');
  const pasteBtn = document.getElementById('modelePasteBtn');
  const phEl = document.getElementById('modelePastePlaceholders');
  pasteBtn.addEventListener('click', () => {
    const raw = (pasteEl.value || '').trim();
    if (!raw) { statusEl.textContent = 'Collez d’abord un texte à transformer.'; return; }
    const t = transformToModele(raw, phEl && phEl.checked);
    modeleCurrentId = null;                 // nouveau modèle (pas d'écrasement)
    nameEl.value = t.name;
    catEl.value = t.category;
    subjEl.value = t.subject;
    bodyEl.value = t.body;
    modeleAttachments = [];
    renderList(); renderAttach();
    nameEl.focus();
    statusEl.textContent = 'Modèle prêt — vérifiez puis cliquez « Enregistrer ».';
    setTimeout(() => (statusEl.textContent = ''), 4000);
  });

  // --- Importer un document (Word/PDF/texte) directement dans le contenu ---
  const docStatus = document.getElementById('modeleDocStatus');
  document.getElementById('modeleImportDoc').addEventListener('click', async () => {
    docStatus.textContent = 'Lecture du document…';
    const r = await window.prive.pickExtractDoc();
    if (r.canceled) { docStatus.textContent = ''; return; }
    if (!r.ok) { docStatus.textContent = r.reason || 'Import impossible.'; return; }
    const insert = r.text || '';
    if (bodyEl.value.trim() && !confirm('Remplacer le contenu actuel par le document importé ?\n(Annuler = ajouter à la suite)')) {
      bodyEl.value = bodyEl.value + '\n\n' + insert;
    } else {
      bodyEl.value = insert;
    }
    if (!subjEl.value && r.name) subjEl.value = r.name.replace(/\.[^.]+$/, '');
    docStatus.textContent = `Importé : ${escapeHtml(r.name || 'document')} ✓`;
    setTimeout(() => (docStatus.textContent = ''), 4000);
  });

  const modeleExportName = () => (nameEl.value.trim() || subjEl.value.trim() || 'modele')
    .replace(/[\\/:*?"<>|]+/g, '-').slice(0, 60);
  const modeleExportHtml = () => {
    const esc = escapeHtml;
    const bodyHtml = esc(bodyEl.value).replace(/\n/g, '<br>');
    return '<html><head><meta charset="utf-8"><title>' + esc(subjEl.value || nameEl.value) + '</title></head>' +
      '<body style="font-family:Calibri,Arial,sans-serif;font-size:14px;line-height:1.5;color:#111">' +
      (subjEl.value ? '<h2 style="font-family:inherit">' + esc(subjEl.value) + '</h2>' : '') +
      '<div>' + bodyHtml + '</div></body></html>';
  };
  document.getElementById('modeleExportPdf').addEventListener('click', async () => {
    if (!bodyEl.value.trim() && !subjEl.value.trim()) { docStatus.textContent = 'Rien à exporter.'; return; }
    const r = await window.prive.exportPdf(modeleExportHtml(), modeleExportName() + '.pdf');
    docStatus.textContent = r.ok ? 'PDF exporté ✓' : (r.error || 'Export impossible.');
    setTimeout(() => (docStatus.textContent = ''), 3000);
  });
  document.getElementById('modeleExportWord').addEventListener('click', async () => {
    if (!bodyEl.value.trim() && !subjEl.value.trim()) { docStatus.textContent = 'Rien à exporter.'; return; }
    // Un HTML balisé enregistré en .doc s'ouvre nativement dans Word.
    const r = await window.prive.exportSave(modeleExportHtml(), modeleExportName() + '.doc');
    docStatus.textContent = r.ok ? 'Word exporté ✓' : (r.error || 'Export impossible.');
    setTimeout(() => (docStatus.textContent = ''), 3000);
  });

  renderList();
  renderAttach();
}

// Transforme un texte/mail collé en modèle : devine la catégorie, extrait
// l'objet, nettoie les en-têtes de mail et remplace éventuellement les
// données personnelles (noms, dates…) par des champs {{…}} réutilisables.
function transformToModele(raw, usePlaceholders) {
  let text = String(raw).replace(/\r\n/g, '\n').trim();
  let subject = '';

  // 1) Objet explicite (Objet:/Subject:/Sujet:) puis retrait des en-têtes de mail.
  const subjM = text.match(/^\s*(?:objet|subject|sujet)\s*:\s*(.+)$/im);
  if (subjM) subject = subjM[1].trim();
  const HEADER = /^\s*(?:de|from|à|a|to|cc|cci|bcc|envoyé|sent|date|objet|subject|sujet|reply-to|répondre à)\s*:.*$/gim;
  // Ne retire les en-têtes que s'il y en a au moins deux (vrai bloc d'en-tête de mail).
  if ((text.match(HEADER) || []).length >= 2) text = text.replace(HEADER, '').trim();
  text = text.replace(/^\n+/, '').trim();

  // 2) Objet de repli : première ligne courte non vide.
  if (!subject) {
    const first = text.split('\n').map((l) => l.trim()).find(Boolean) || '';
    subject = first.length > 90 ? first.slice(0, 87) + '…' : first;
  }

  // 3) Catégorie devinée par mots-clés.
  const low = (subject + ' ' + text).toLowerCase();
  let category = 'mail';
  if (/prescription|ordonnance|rp\/|posologie|à prendre|comprimé|gélule|mg\b|renouvel/.test(low)) category = 'prescription';
  else if (/rapport|compte[- ]rendu|conclusion|anamnèse|examen clinique|diagnostic|bilan|résultat/.test(low)) category = 'rapport';
  else if (/bonjour|madame|monsieur|cordialement|bien à vous|cher|chère|rendez-vous|confirmation/.test(low)) category = 'mail';

  // 4) Nom du modèle : à partir de l'objet.
  let name = subject.replace(/^(re|tr|fwd|fw)\s*:\s*/i, '').trim() || 'Modèle importé';
  if (name.length > 60) name = name.slice(0, 57) + '…';

  // 5) Remplacement optionnel des données personnelles par des champs.
  let body = text;
  if (usePlaceholders) {
    // Dates : 12/07/2026, 12-07-26, 12 juillet 2026, 2026-07-12…
    body = body.replace(/\b\d{1,2}[\/.\-]\d{1,2}[\/.\-]\d{2,4}\b/g, '{{DATE}}');
    body = body.replace(/\b\d{4}-\d{2}-\d{2}\b/g, '{{DATE}}');
    body = body.replace(/\b\d{1,2}\s+(janvier|février|fevrier|mars|avril|mai|juin|juillet|août|aout|septembre|octobre|novembre|décembre|decembre)\s+\d{4}\b/gi, '{{DATE}}');
    // Heures : 14h30, 08:15
    body = body.replace(/\b\d{1,2}\s*[:h]\s*\d{2}\b/g, '{{HEURE}}');
    // Nom après une formule d'appel : « Cher M. Dupont », « Bonjour Madame Martin ».
    // (on n'attrape pas « Madame, Monsieur » : le mot suivant ne doit pas être une civilité)
    body = body.replace(
      /\b(Cher|Chère|Bonjour|Madame|Monsieur|Mme|Mr|Dr)\.?\s+(?!(?:Madame|Monsieur|Mme|Mr)\b)([A-ZÀ-Ý][\wÀ-ÿ'-]+(?:\s+[A-ZÀ-Ý][\wÀ-ÿ'-]+){0,2})/g,
      '$1 {{PATIENT}}');
    // E-mails et téléphones
    body = body.replace(/\b[\w.+-]+@[\w-]+\.[\w.-]+\b/g, '{{EMAIL}}');
    body = body.replace(/(?:\+?\d[\d\s().-]{7,}\d)/g, '{{TELEPHONE}}');
    // Montants
    body = body.replace(/\b\d+(?:[.,]\d{2})?\s*€/g, '{{MONTANT}}');
    if (subject) {
      subject = subject.replace(/\b\d{1,2}[\/.\-]\d{1,2}[\/.\-]\d{2,4}\b/g, '{{DATE}}');
    }
  }

  return { name, category, subject, body: body.trim() };
}

// ---------------------------------------------------------------------------
// Médecins (base locale, recherche + ajout/suppression)
// ---------------------------------------------------------------------------

let medCurrentId = null;
const medSelected = new Set();

function setupMedecins() {
  const searchEl = document.getElementById('medSearch');
  const listEl = document.getElementById('medList');
  const countEl = document.getElementById('medCount');
  const nameEl = document.getElementById('medName');
  const societeEl = document.getElementById('medSociete');
  const adresseEl = document.getElementById('medAdresse');
  const telEl = document.getElementById('medTel');
  const nissEl = document.getElementById('medNiss');
  const inamiEl = document.getElementById('medInami');
  const statusEl = document.getElementById('medStatus');
  const exportStatus = document.getElementById('medExportStatus');
  const selectAllEl = document.getElementById('medSelectAll');

  const filtered = () => {
    const q = searchEl.value.trim().toLowerCase();
    return (settings.doctors || []).filter((d) => {
      if (!q) return true;
      return [d.name, d.societe, d.adresse, d.tel, d.niss, d.inami]
        .filter(Boolean).join(' ').toLowerCase().includes(q);
    });
  };

  const renderList = () => {
    const all = settings.doctors || [];
    const items = filtered();
    countEl.textContent = `${items.length} / ${all.length} médecin(s)` +
      (medSelected.size ? ` · ${medSelected.size} sélectionné(s)` : '');
    listEl.innerHTML = '';
    if (!items.length) {
      listEl.innerHTML = '<div class="hint">Aucun médecin trouvé.</div>';
    }
    items.forEach((d) => {
      const div = document.createElement('div');
      div.className = 'modele-item med-item' + (d.id === medCurrentId ? ' active' : '');
      div.innerHTML =
        `<input type="checkbox" class="med-check" ${medSelected.has(d.id) ? 'checked' : ''} />` +
        `<div class="med-item-body"><div class="m-name">${escapeHtml(d.name)}</div>` +
        `<div class="m-cat">${escapeHtml(d.societe || '—')}</div></div>`;
      const chk = div.querySelector('.med-check');
      chk.addEventListener('click', (e) => {
        e.stopPropagation();
        if (chk.checked) medSelected.add(d.id); else medSelected.delete(d.id);
        countEl.textContent = `${items.length} / ${all.length} médecin(s)` +
          (medSelected.size ? ` · ${medSelected.size} sélectionné(s)` : '');
      });
      div.querySelector('.med-item-body').addEventListener('click', () => select(d.id));
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
    inamiEl.value = d.inami || '';
    renderList();
  };

  const blank = () => {
    medCurrentId = null;
    [nameEl, societeEl, adresseEl, telEl, nissEl, inamiEl].forEach((e) => (e.value = ''));
    renderList();
    nameEl.focus();
  };

  searchEl.addEventListener('input', renderList);
  document.getElementById('medNew').addEventListener('click', blank);

  selectAllEl.addEventListener('change', () => {
    const items = filtered();
    if (selectAllEl.checked) items.forEach((d) => medSelected.add(d.id));
    else items.forEach((d) => medSelected.delete(d.id));
    renderList();
  });

  document.getElementById('medSave').addEventListener('click', async () => {
    const name = nameEl.value.trim();
    if (!name) { statusEl.textContent = 'Nom requis.'; return; }
    settings.doctors = settings.doctors || [];
    const data = {
      name,
      societe: societeEl.value.trim(),
      adresse: adresseEl.value.trim(),
      tel: telEl.value.trim(),
      niss: nissEl.value.trim(),
      inami: inamiEl.value.trim()
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
    medSelected.delete(medCurrentId);
    await persistSettings();
    blank();
    statusEl.textContent = 'Supprimé.';
    setTimeout(() => (statusEl.textContent = ''), 2000);
  });

  // --- Export XLS / PDF ---
  const COLS = ['Nom & Prénom', 'Société', 'Adresse', 'Téléphone / N°', 'NISS', 'N° INAMI'];
  const rowsToExport = () => {
    const all = settings.doctors || [];
    const chosen = medSelected.size ? all.filter((d) => medSelected.has(d.id)) : filtered();
    return chosen;
  };
  const esc = (s) => escapeHtml(String(s == null ? '' : s));
  const buildTableHtml = (docs) => {
    const head = '<tr>' + COLS.map((c) => `<th style="border:1px solid #999;padding:4px 8px;background:#eee;text-align:left">${c}</th>`).join('') + '</tr>';
    const body = docs.map((d) =>
      '<tr>' + [d.name, d.societe, d.adresse, d.tel, d.niss, d.inami]
        .map((v) => `<td style="border:1px solid #999;padding:4px 8px">${esc(v)}</td>`).join('') + '</tr>'
    ).join('');
    return `<table style="border-collapse:collapse;font-family:Arial,sans-serif;font-size:12px">${head}${body}</table>`;
  };

  document.getElementById('medExportXls').addEventListener('click', async () => {
    const docs = rowsToExport();
    if (!docs.length) { exportStatus.textContent = 'Aucun médecin à exporter.'; return; }
    const html = '<html><head><meta charset="utf-8"></head><body>' + buildTableHtml(docs) + '</body></html>';
    const res = await window.prive.exportSave(html, 'medecins.xls');
    exportStatus.textContent = res.ok ? `Exporté (${docs.length}) ✓` : (res.ok === false && res.error ? res.error : '');
    setTimeout(() => (exportStatus.textContent = ''), 3000);
  });

  document.getElementById('medExportPdf').addEventListener('click', async () => {
    const docs = rowsToExport();
    if (!docs.length) { exportStatus.textContent = 'Aucun médecin à exporter.'; return; }
    const html = '<html><head><meta charset="utf-8"><title>Médecins</title></head><body>' +
      '<h2 style="font-family:Arial">Liste des médecins (' + docs.length + ')</h2>' +
      buildTableHtml(docs) + '</body></html>';
    const res = await window.prive.exportPdf(html, 'medecins.pdf');
    exportStatus.textContent = res.ok ? `PDF exporté (${docs.length}) ✓` : (res.error || '');
    setTimeout(() => (exportStatus.textContent = ''), 3000);
  });

  renderList();
}

// ---------------------------------------------------------------------------
// Coffre-fort de mots de passe (chiffré côté main)
// ---------------------------------------------------------------------------

let vaultEntries = [];
let vaultCurrentId = null;

function csvEscape(s) {
  s = String(s == null ? '' : s);
  return /[",\n]/.test(s) ? '"' + s.replace(/"/g, '""') + '"' : s;
}
function parseCsv(text) {
  const rows = [];
  let row = [], field = '', inQ = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (inQ) {
      if (c === '"' && text[i + 1] === '"') { field += '"'; i++; }
      else if (c === '"') inQ = false;
      else field += c;
    } else if (c === '"') inQ = true;
    else if (c === ',') { row.push(field); field = ''; }
    else if (c === '\n') { row.push(field); rows.push(row); row = []; field = ''; }
    else if (c === '\r') { /* ignore */ }
    else field += c;
  }
  if (field.length || row.length) { row.push(field); rows.push(row); }
  return rows.filter((r) => r.some((x) => x && x.trim()));
}

async function setupVault() {
  const searchEl = document.getElementById('vaultSearch');
  const listEl = document.getElementById('vaultList');
  const countEl = document.getElementById('vaultCount');
  const siteEl = document.getElementById('vSite');
  const urlEl = document.getElementById('vUrl');
  const userEl = document.getElementById('vUser');
  const passEl = document.getElementById('vPass');
  const noteEl = document.getElementById('vNote');
  const statusEl = document.getElementById('vStatus');
  const ioStatus = document.getElementById('vaultIoStatus');

  const loaded = await window.prive.vaultGet();
  vaultEntries = (loaded && loaded.entries) || [];

  const filtered = () => {
    const q = searchEl.value.trim().toLowerCase();
    return vaultEntries.filter((e) => !q ||
      [e.site, e.url, e.user, e.note].filter(Boolean).join(' ').toLowerCase().includes(q));
  };

  const renderList = () => {
    const items = filtered();
    countEl.textContent = `${items.length} / ${vaultEntries.length} entrée(s)` +
      (loaded && loaded.encrypted === false ? ' · ⚠️ non chiffré sur ce poste' : '');
    listEl.innerHTML = items.length ? '' : '<div class="hint">Aucune entrée.</div>';
    items.forEach((e) => {
      const div = document.createElement('div');
      div.className = 'modele-item' + (e.id === vaultCurrentId ? ' active' : '');
      div.innerHTML = `<div class="m-name">${escapeHtml(e.site || e.url || '—')}</div>` +
        `<div class="m-cat">${escapeHtml(e.user || '')}</div>`;
      div.addEventListener('click', () => select(e.id));
      listEl.appendChild(div);
    });
  };

  const select = (id) => {
    const e = vaultEntries.find((x) => x.id === id);
    if (!e) return;
    vaultCurrentId = id;
    siteEl.value = e.site || ''; urlEl.value = e.url || '';
    userEl.value = e.user || ''; passEl.value = e.password || ''; noteEl.value = e.note || '';
    renderList();
  };

  const blank = () => {
    vaultCurrentId = null;
    [siteEl, urlEl, userEl, passEl, noteEl].forEach((x) => (x.value = ''));
    renderList(); siteEl.focus();
  };

  const persist = async () => { await window.prive.vaultSave(vaultEntries); };

  searchEl.addEventListener('input', renderList);
  document.getElementById('vaultNew').addEventListener('click', blank);
  document.getElementById('vToggle').addEventListener('click', () => {
    passEl.type = passEl.type === 'password' ? 'text' : 'password';
  });

  document.getElementById('vSave').addEventListener('click', async () => {
    if (!siteEl.value.trim() && !urlEl.value.trim()) { statusEl.textContent = 'Site ou URL requis.'; return; }
    const data = {
      site: siteEl.value.trim(), url: urlEl.value.trim(),
      user: userEl.value.trim(), password: passEl.value, note: noteEl.value.trim()
    };
    if (vaultCurrentId) {
      const e = vaultEntries.find((x) => x.id === vaultCurrentId);
      if (e) Object.assign(e, data);
    } else {
      vaultCurrentId = 'v' + Date.now();
      vaultEntries.push({ id: vaultCurrentId, ...data });
    }
    await persist(); renderList();
    statusEl.textContent = 'Enregistré ✓';
    setTimeout(() => (statusEl.textContent = ''), 2000);
  });

  document.getElementById('vDelete').addEventListener('click', async () => {
    if (!vaultCurrentId) return;
    vaultEntries = vaultEntries.filter((x) => x.id !== vaultCurrentId);
    await persist(); blank();
  });

  document.getElementById('vCopyUser').addEventListener('click', async () => {
    try { await navigator.clipboard.writeText(userEl.value); statusEl.textContent = 'Identifiant copié ✓'; } catch (_) {}
    setTimeout(() => (statusEl.textContent = ''), 1500);
  });
  document.getElementById('vCopyPass').addEventListener('click', async () => {
    try { await navigator.clipboard.writeText(passEl.value); statusEl.textContent = 'Mot de passe copié ✓'; } catch (_) {}
    setTimeout(() => (statusEl.textContent = ''), 1500);
  });

  // Capture automatique depuis le dernier onglet site visité.
  document.getElementById('vaultCapture').addEventListener('click', async () => {
    if (!lastSiteView || !SITES[lastSiteView]) {
      ioStatus.textContent = 'Ouvrez d’abord un onglet (Gmail, Doctena…) et saisissez-y vos identifiants, puis revenez.';
      return;
    }
    const wv = getWebview(lastSiteView);
    const code = `(function(){
      var p=document.querySelector('input[type=password]');
      if(!p) return '';
      var inputs=[].slice.call(document.querySelectorAll('input'));
      var idx=inputs.indexOf(p), user='';
      for(var i=idx-1;i>=0;i--){ var t=(inputs[i].type||'text').toLowerCase(); if(t==='text'||t==='email'||t==='tel'){ user=inputs[i].value; break; } }
      return JSON.stringify({user:user, pass:p.value, url:location.href, host:location.host});
    })()`;
    let out = '';
    try { out = await wv.executeJavaScript(code, true); } catch (_) {}
    if (!out) { ioStatus.textContent = 'Aucun champ mot de passe trouvé sur cet onglet.'; return; }
    const d = JSON.parse(out);
    blank();
    siteEl.value = SITES[lastSiteView].title || d.host;
    urlEl.value = d.url; userEl.value = d.user; passEl.value = d.pass;
    ioStatus.textContent = 'Capturé depuis ' + (SITES[lastSiteView].title) + ' — vérifiez puis Enregistrer.';
  });

  document.getElementById('vaultExport').addEventListener('click', async () => {
    const header = ['Site', 'URL', 'Identifiant', 'MotDePasse', 'Note'];
    const lines = [header.join(',')].concat(vaultEntries.map((e) =>
      [e.site, e.url, e.user, e.password, e.note].map(csvEscape).join(',')));
    const res = await window.prive.exportSave(lines.join('\r\n'), 'coffre-fort.csv');
    ioStatus.textContent = res.ok ? 'Exporté ✓' : (res.error || '');
    setTimeout(() => (ioStatus.textContent = ''), 3000);
  });

  document.getElementById('vaultImport').addEventListener('click', async () => {
    const res = await window.prive.pickTextFile();
    if (!res.ok) return;
    const rows = parseCsv(res.content);
    if (!rows.length) { ioStatus.textContent = 'Fichier vide.'; return; }
    // Détecte l'en-tête.
    let start = 0;
    const h = rows[0].map((x) => x.toLowerCase());
    const col = { site: 0, url: 1, user: 2, pass: 3, note: 4 };
    if (h.some((x) => /site|url|identifiant|user|pass|mot/.test(x))) {
      start = 1;
      col.site = h.findIndex((x) => /site|name|nom/.test(x));
      col.url = h.findIndex((x) => /url|web|lien/.test(x));
      col.user = h.findIndex((x) => /user|identifiant|login|email/.test(x));
      col.pass = h.findIndex((x) => /pass|mot/.test(x));
      col.note = h.findIndex((x) => /note|comment/.test(x));
    }
    let added = 0;
    for (let i = start; i < rows.length; i++) {
      const r = rows[i];
      const get = (k) => (col[k] >= 0 && r[col[k]] != null ? r[col[k]] : '');
      const entry = { site: get('site'), url: get('url'), user: get('user'), password: get('pass'), note: get('note') };
      if (!entry.site && !entry.url && !entry.user) continue;
      entry.id = 'v' + Date.now() + '_' + i;
      vaultEntries.push(entry); added++;
    }
    await persist(); renderList();
    ioStatus.textContent = `${added} entrée(s) importée(s) ✓`;
    setTimeout(() => (ioStatus.textContent = ''), 3000);
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

// Reconnaît un libellé de mois (« Janvier », « févr », « JUILLET »…) → clé PREST_MONTHS.
function prestMonthKey(label) {
  const n = (label || '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');
  const map = [
    ['janv', 'JANVIER'], ['fevr', 'FÉVRIER'], ['mars', 'MARS'], ['avri', 'AVRIL'],
    ['mai', 'MAI'], ['juin', 'JUIN'], ['juil', 'JUILLET'], ['aout', 'AOÛT'],
    ['sept', 'SEPTEMBRE'], ['octo', 'OCTOBRE'], ['nove', 'NOVEMBRE'], ['dece', 'DÉCEMBRE']
  ];
  for (const [p, m] of map) if (n.includes(p)) return m;
  return null;
}

// Construit les lignes d'export (en-tête aplati « Nom FICHE / Nom SHYFTER »).
function prestExportRows() {
  const ds = prestDataset();
  const persons = ds.persons;
  const header = ['Mois'];
  persons.forEach((p) => { header.push(`${p.name} FICHE`, `${p.name} SHYFTER`); });
  const rows = [header];
  PREST_MONTHS.forEach((mo) => {
    const row = [mo];
    persons.forEach((p) => {
      const c = (p.months && p.months[mo]) || {};
      row.push(Number(c.fiche) || 0, Number(c.shyfter) || 0);
    });
    rows.push(row);
  });
  const totals = ['TOTAL'];
  persons.forEach((p) => {
    let tf = 0, ts = 0;
    PREST_MONTHS.forEach((mo) => { const c = (p.months && p.months[mo]) || {}; tf += Number(c.fiche) || 0; ts += Number(c.shyfter) || 0; });
    totals.push(tf, ts);
  });
  rows.push(totals);
  return rows;
}

function prestTableHtml(rows) {
  const cell = (v, i, isHead) => {
    const tag = isHead ? 'th' : 'td';
    const bg = isHead ? 'background:#eee;' : (i === 0 ? 'background:#f5f5f5;font-weight:bold;' : '');
    const align = i === 0 ? 'left' : 'center';
    return `<${tag} style="border:1px solid #999;padding:4px 8px;text-align:${align};${bg}">${escapeHtml(String(v))}</${tag}>`;
  };
  const body = rows.map((r, ri) =>
    '<tr>' + r.map((v, i) => cell(v, i, ri === 0)).join('') + '</tr>').join('');
  return `<table style="border-collapse:collapse;font-family:Arial,sans-serif;font-size:12px">${body}</table>`;
}

// Analyse un fichier importé (CSV ou XLS-HTML) → tableau de lignes (array d'array).
function prestParseImport(content) {
  if (/<table[\s>]/i.test(content)) {
    try {
      const doc = new DOMParser().parseFromString(content, 'text/html');
      const trs = doc.querySelectorAll('table tr');
      const rows = [];
      trs.forEach((tr) => {
        const cells = Array.from(tr.querySelectorAll('th,td')).map((c) => (c.textContent || '').trim());
        if (cells.length) rows.push(cells);
      });
      return rows;
    } catch (_) { /* repli CSV */ }
  }
  return parseCsv(content);
}

// Applique les lignes importées au jeu de données courant (type + année).
function prestApplyImport(rows) {
  if (!rows || rows.length < 2) return { ok: false, reason: 'fichier vide' };
  // Trouve l'en-tête (ligne avec « Mois » en 1re cellule, sinon 1re ligne).
  let hi = rows.findIndex((r) => /^mois$/i.test((r[0] || '').trim()));
  if (hi < 0) hi = 0;
  const header = rows[hi];
  // Détermine les personnes et leurs colonnes FICHE / SHYFTER.
  const persons = [];
  const colMap = [];   // { idx, personIndex, field }
  for (let i = 1; i < header.length; i++) {
    const h = (header[i] || '').trim();
    const m = h.match(/^(.*?)[\s_-]*(fiche|shyfter|paie)$/i);
    if (!m) continue;
    const name = m[1].trim().replace(/[|/]+$/, '').trim();
    const field = /shyfter/i.test(m[2]) ? 'shyfter' : 'fiche';
    if (!name) continue;
    let pi = persons.findIndex((p) => p.name.toLowerCase() === name.toLowerCase());
    if (pi < 0) { persons.push({ name, months: emptyMonths() }); pi = persons.length - 1; }
    colMap.push({ idx: i, personIndex: pi, field });
  }
  if (!persons.length) return { ok: false, reason: 'aucune colonne « Nom FICHE / SHYFTER » reconnue' };
  // Remplit les valeurs par mois.
  for (let r = hi + 1; r < rows.length; r++) {
    const row = rows[r];
    const mo = prestMonthKey(row[0]);
    if (!mo) continue;   // ignore TOTAL et autres lignes
    colMap.forEach((c) => {
      const raw = (row[c.idx] == null ? '' : String(row[c.idx])).replace(',', '.').replace(/[^0-9.\-]/g, '');
      const val = raw === '' ? 0 : Number(raw);
      persons[c.personIndex].months[mo][c.field] = isNaN(val) ? 0 : val;
    });
  }
  prestDataset().persons = persons;
  return { ok: true, persons: persons.length };
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

  const typeLabelFr = () => (prestType === 'employes' ? 'Employés' : 'Étudiants');

  document.getElementById('prestExportXls').addEventListener('click', async () => {
    const rows = prestExportRows();
    if (rows.length <= 2 || rows[0].length <= 1) { status.textContent = 'Aucune donnée à exporter.'; return; }
    const html = '<html><head><meta charset="utf-8"></head><body>' + prestTableHtml(rows) + '</body></html>';
    const res = await window.prive.exportSave(html, `prestations-${prestType}-${prestYear}.xls`);
    status.textContent = res.ok ? 'Export XLS ✓' : (res.error || 'Export impossible.');
    setTimeout(() => (status.textContent = ''), 3000);
  });

  document.getElementById('prestExportPdf').addEventListener('click', async () => {
    const rows = prestExportRows();
    if (rows.length <= 2 || rows[0].length <= 1) { status.textContent = 'Aucune donnée à exporter.'; return; }
    const html = '<html><head><meta charset="utf-8"><title>Prestations</title></head><body>' +
      `<h2 style="font-family:Arial">Prestations — ${typeLabelFr()} ${prestYear}</h2>` +
      prestTableHtml(rows) + '</body></html>';
    const res = await window.prive.exportPdf(html, `prestations-${prestType}-${prestYear}.pdf`);
    status.textContent = res.ok ? 'Export PDF ✓' : (res.error || 'Export impossible.');
    setTimeout(() => (status.textContent = ''), 3000);
  });

  document.getElementById('prestImport').addEventListener('click', async () => {
    const res = await window.prive.pickTextFile();
    if (!res.ok) return;
    const rows = prestParseImport(res.content);
    if (!rows.length) { status.textContent = 'Fichier illisible ou vide.'; return; }
    if (!confirm(`Importer ces données dans « ${typeLabelFr()} ${prestYear} » ? Les personnes actuelles de cette vue seront remplacées.`)) return;
    const r = prestApplyImport(rows);
    if (!r.ok) { status.textContent = 'Import : ' + r.reason + '. Utilisez un CSV ou un XLS exporté par ce programme.'; return; }
    await persistSettings();
    renderPrestTable();
    updatePersonDatalist();
    renderPersonSearch(personSearch.value);
    status.textContent = `Importé ✓ (${r.persons} personne(s)).`;
    setTimeout(() => (status.textContent = ''), 4000);
  });

  // --- Synchronisation des heures SHYFTER depuis le rapport Shyfter ---
  const monthKeyFromLabel = (label) => {
    const n = (label || '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');
    const map = [
      ['janv', 'JANVIER'], ['fevr', 'FÉVRIER'], ['mars', 'MARS'], ['avri', 'AVRIL'],
      ['mai', 'MAI'], ['juin', 'JUIN'], ['juil', 'JUILLET'], ['aout', 'AOÛT'],
      ['sept', 'SEPTEMBRE'], ['octo', 'OCTOBRE'], ['nove', 'NOVEMBRE'], ['dece', 'DÉCEMBRE']
    ];
    for (const [p, m] of map) if (n.includes(p)) return m;
    return null;
  };
  const normPersonName = (s) => (s || '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '')
    .replace(/\b(em|et)\b/g, ' ').replace(/[^a-z0-9\s]/g, ' ').split(/\s+/).filter((w) => w.length >= 3);

  document.getElementById('prestSyncShyfter').addEventListener('click', async () => {
    const box = document.getElementById('prestShyBox');
    const wv = document.getElementById('prestShyWv');
    box.style.display = 'block';
    status.textContent = 'Chargement du rapport Shyfter…';

    const REPORT_URL = 'https://v3-app.shyfter.co/app/reports/timesheets/default';
    const scrape = `(function(){
      var MONTHS=['janvier','fevrier','février','mars','avril','mai','juin','juillet','aout','août','septembre','octobre','novembre','decembre','décembre'];
      function norm(s){ return (s||'').toLowerCase(); }
      var out=[];
      var tables=document.querySelectorAll('table');
      for(var ti=0;ti<tables.length;ti++){
        var table=tables[ti];
        var heads=table.querySelectorAll('thead th, thead td');
        if(!heads.length){ var fr=table.querySelector('tr'); heads=fr?fr.querySelectorAll('th,td'):[]; }
        var monthCols={};
        for(var i=0;i<heads.length;i++){ var h=norm(heads[i].textContent); MONTHS.forEach(function(mn){ if(h.indexOf(mn)>=0) monthCols[i]=heads[i].textContent.trim(); }); }
        if(!Object.keys(monthCols).length) continue;
        var rows=table.querySelectorAll('tbody tr'); if(!rows.length) rows=table.querySelectorAll('tr');
        for(var r=0;r<rows.length;r++){
          var cells=rows[r].querySelectorAll('td,th'); if(cells.length<2) continue;
          var name=(cells[0].textContent||'').trim(); if(!name || /total/i.test(name)) continue;
          var months={};
          Object.keys(monthCols).forEach(function(ci){
            var raw=(cells[ci]?cells[ci].textContent:'')||'';
            var v=raw.replace(',','.').replace(/[^0-9.]/g,'');
            if(v!=='') months[monthCols[ci]]=parseFloat(v);
          });
          if(name && Object.keys(months).length) out.push({name:name, months:months});
        }
        if(out.length) break;
      }
      return JSON.stringify({rows:out, hasTable: document.querySelectorAll('table').length>0, login:/connect|mot de passe|login|password/i.test((document.body&&document.body.innerText)||'')});
    })()`;

    const scrapeNow = async () => {
      try { return JSON.parse(await wv.executeJavaScript(scrape, true)); } catch (_) { return { rows: [] }; }
    };

    // (Re)charge le rapport puis lit après stabilisation.
    await new Promise((resolve) => {
      let done = false;
      const finish = () => { if (done) return; done = true; wv.removeEventListener('did-stop-loading', finish); setTimeout(resolve, 2500); };
      wv.addEventListener('did-stop-loading', finish);
      try { wv.loadURL(REPORT_URL); } catch (_) { wv.src = REPORT_URL; }
      setTimeout(finish, 8000); // filet de sécurité
    });

    const res = await scrapeNow();
    if (res.login) { status.textContent = 'Connectez-vous à Shyfter dans le panneau ci-dessus, puis relancez la synchro.'; return; }
    if (!res.rows || !res.rows.length) {
      status.textContent = 'Aucun tableau mensuel détecté dans le rapport. Réglez le rapport sur l’année en vue mensuelle, ou envoyez-moi une capture pour calibrer.';
      return;
    }

    // Applique aux jeux de l'année courante (employés + étudiants).
    let filled = 0, matched = 0;
    ['employes', 'etudiants'].forEach((type) => {
      const ds = (settings.prestations || {})[`${type}-${prestYear}`];
      if (!ds || !ds.persons) return;
      res.rows.forEach((row) => {
        const rTokens = normPersonName(row.name);
        const person = ds.persons.find((p) => {
          const pt = normPersonName(p.name);
          return pt.some((w) => rTokens.includes(w));
        });
        if (!person) return;
        matched++;
        if (!person.months) person.months = emptyMonths();
        Object.keys(row.months).forEach((label) => {
          const mk = monthKeyFromLabel(label);
          if (mk && person.months[mk]) { person.months[mk].shyfter = row.months[label]; filled++; }
        });
      });
    });

    await persistSettings();
    renderPrestTable();
    updatePersonDatalist();
    status.textContent = `Synchronisé : ${matched} personne(s), ${filled} valeur(s) SHYFTER mises à jour (${prestYear}).`;
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

// Construit un horodatage iCalendar/Google (heure locale « flottante »).
function calStamp(dateStr, timeStr, addMin) {
  const [Y, M, D] = (dateStr || '').split('-').map(Number);
  const [h, mi] = (timeStr || '00:00').split(':').map(Number);
  const d = new Date(Y, (M || 1) - 1, D || 1, h || 0, mi || 0);
  if (addMin) d.setMinutes(d.getMinutes() + addMin);
  return `${d.getFullYear()}${pad2(d.getMonth() + 1)}${pad2(d.getDate())}T${pad2(d.getHours())}${pad2(d.getMinutes())}00`;
}

// Lien « Ajouter à Google Agenda » vers le compte rattaché.
function googleCalUrl(ev) {
  const email = (settings.calendar && settings.calendar.email) || '';
  const label = { rdv: 'Rendez-vous', tache: 'Tâche', rappel: 'Rappel' }[ev.type] || '';
  const start = calStamp(ev.date, ev.time, 0);
  const end = calStamp(ev.date, ev.time, 30);
  let url = 'https://calendar.google.com/calendar/render?action=TEMPLATE' +
    '&text=' + encodeURIComponent(`${label} : ${ev.title}`) +
    '&dates=' + start + '/' + end +
    (ev.note ? '&details=' + encodeURIComponent(ev.note) : '');
  if (email) url += '&authuser=' + encodeURIComponent(email);
  return url;
}

// Génère un fichier .ics de tous les événements.
function buildIcs(events) {
  const esc = (s) => String(s || '').replace(/([,;\\])/g, '\\$1').replace(/\n/g, '\\n');
  const lines = ['BEGIN:VCALENDAR', 'VERSION:2.0', 'PRODID:-//IATECH-CONTROL PRO//FR', 'CALSCALE:GREGORIAN'];
  (events || []).forEach((ev) => {
    const label = { rdv: 'Rendez-vous', tache: 'Tâche', rappel: 'Rappel' }[ev.type] || '';
    lines.push('BEGIN:VEVENT',
      `UID:${ev.id}@iatech-control-pro`,
      `DTSTART:${calStamp(ev.date, ev.time, 0)}`,
      `DTEND:${calStamp(ev.date, ev.time, 30)}`,
      `SUMMARY:${esc(label + ' : ' + ev.title)}`,
      ev.note ? `DESCRIPTION:${esc(ev.note)}` : 'DESCRIPTION:',
      'END:VEVENT');
  });
  lines.push('END:VCALENDAR');
  return lines.join('\r\n');
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

  // Adresse rattachée
  const emailEl = document.getElementById('calEmail');
  const linkStatus = document.getElementById('calLinkStatus');
  emailEl.value = (settings.calendar && settings.calendar.email) || '';
  const showLink = () => {
    const e = (settings.calendar && settings.calendar.email) || '';
    linkStatus.textContent = e ? `Rattaché à ${e}` : 'Aucune adresse rattachée.';
  };
  showLink();
  document.getElementById('calEmailSave').addEventListener('click', async () => {
    settings.calendar = settings.calendar || {};
    settings.calendar.email = emailEl.value.trim();
    await persistSettings();
    showLink();
    linkStatus.textContent = 'Enregistré ✓ — ' + linkStatus.textContent;
  });
  // Connexion Google Agenda
  const gStatusEl = document.getElementById('calGoogleStatus');
  const refreshGoogleStatus = async () => {
    const s = await window.prive.googleStatus();
    gStatusEl.textContent = s.connected ? '✅ Google Agenda connecté'
      : (s.hasCreds ? 'Non connecté — cliquez pour autoriser.' : 'Renseignez Client ID/Secret dans Réglages.');
    return s;
  };
  // Champs Client ID / Secret directement dans l'onglet Calendrier.
  const gId = document.getElementById('calGoogleId');
  const gSecret = document.getElementById('calGoogleSecret');
  if (gId) gId.value = (settings.google && settings.google.clientId) || '';
  if (gSecret) gSecret.value = (settings.google && settings.google.clientSecret) || '';
  const saveCredsBtn = document.getElementById('calGoogleSaveCreds');
  if (saveCredsBtn) {
    saveCredsBtn.addEventListener('click', async () => {
      settings.google = settings.google || {};
      settings.google.clientId = gId.value.trim();
      settings.google.clientSecret = gSecret.value.trim();
      await persistSettings();
      const rId = document.getElementById('setGoogleId'); if (rId) rId.value = settings.google.clientId;
      const rSec = document.getElementById('setGoogleSecret'); if (rSec) rSec.value = settings.google.clientSecret;
      await refreshGoogleStatus();
      gStatusEl.textContent = 'Clé enregistrée ✓ — ' + gStatusEl.textContent;
    });
  }

  refreshGoogleStatus();

  // Diagnostic : liste les agendas où le compte connecté peut écrire, et indique
  // si l'adresse rattachée en fait partie.
  const calsEl = document.getElementById('calGoogleCals');
  const checkBtn = document.getElementById('calGoogleCheck');
  if (checkBtn) {
    checkBtn.addEventListener('click', async () => {
      calsEl.textContent = 'Lecture des agendas…';
      const r = await window.prive.googleListCalendars();
      if (!r.ok) { calsEl.textContent = 'Impossible de lister les agendas : ' + (r.error || '') + ' (reconnectez-vous).'; return; }
      const target = (settings.calendar && settings.calendar.email || '').toLowerCase();
      const writable = r.calendars.filter((c) => c.writable);
      const targetOk = writable.some((c) => c.id.toLowerCase() === target);
      const list = r.calendars.map((c) =>
        `${c.writable ? '✏️' : '👁️'} ${escapeHtml(c.summary || c.id)}${c.primary ? ' (principal)' : ''} — ${escapeHtml(c.id)}`
      ).join('<br>');
      let head;
      if (!target) head = '<b>Aucune adresse rattachée</b> : les événements iront dans l’agenda principal ci-dessous.';
      else if (targetOk) head = `<b style="color:#4ade80">✓ « ${escapeHtml(target)} » est accessible en écriture</b> — les événements iront bien dedans.`;
      else head = `<b style="color:#f87171">✗ « ${escapeHtml(target)} » n’est PAS accessible en écriture par le compte connecté.</b> ` +
        'Les événements tombent dans l’agenda principal. Connectez-vous avec ce compte, ou partagez cet agenda avec droit de modification.';
      calsEl.innerHTML = head + '<br><br><b>Agendas du compte connecté</b> (✏️ = écriture) :<br>' + list;
    });
  }

  document.getElementById('calGoogleConnect').addEventListener('click', async () => {
    gStatusEl.textContent = 'Ouverture de la fenêtre d\'autorisation Google…';
    const r = await window.prive.googleConnect();
    if (r.ok) {
      settings = await window.prive.getSettings(); // récupère le refreshToken en mémoire
      gStatusEl.textContent = '✅ Google Agenda connecté';
    } else {
      gStatusEl.textContent = 'Échec : ' + (r.error || '');
    }
  });

  // Ajoute un événement à Google Agenda (API si connecté, sinon lien pré-rempli).
  const pushToGoogle = async (ev) => {
    const s = await window.prive.googleStatus();
    if (s.connected) {
      const r = await window.prive.googleAddEvent(ev);
      return r.ok ? { ok: true, api: true } : { ok: false, error: r.error };
    }
    window.prive.openExternal(googleCalUrl(ev));
    return { ok: true, api: false };
  };
  window.__pushToGoogle = pushToGoogle;

  document.getElementById('calExport').addEventListener('click', () => {
    const ics = buildIcs(settings.events || []);
    const blob = new Blob([ics], { type: 'text/calendar' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'calendrier-iatech-control-pro.ics';
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 2000);
  });

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
        `<span class="gcal" title="Ajouter au calendrier rattaché">📅</span>` +
        `<span class="ex" title="Supprimer">×</span>`;
      row.querySelector('.gcal').addEventListener('click', async () => {
        const r = await window.__pushToGoogle(e);
        status.textContent = r.ok ? (r.api ? 'Ajouté à Google Agenda ✓' : 'Google Agenda ouvert — cliquez Enregistrer.') : ('Google : ' + (r.error || 'échec'));
        setTimeout(() => (status.textContent = ''), 3000);
      });
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
        `<span>${escapeHtml(e.title)}</span>` +
        `<span class="gcal" title="Ajouter au calendrier rattaché" style="margin-left:auto">📅</span>`;
      row.querySelector('.gcal').addEventListener('click', async () => {
        const r = await window.__pushToGoogle(e);
        status.textContent = r.ok ? (r.api ? 'Ajouté à Google Agenda ✓' : 'Google Agenda ouvert — cliquez Enregistrer.') : ('Google : ' + (r.error || 'échec'));
        setTimeout(() => (status.textContent = ''), 3000);
      });
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
    // Envoi automatique vers Google Agenda si connecté.
    const gs = await window.prive.googleStatus();
    if (gs.connected) {
      const r = await window.prive.googleAddEvent(ev);
      if (r.ok && r.fellBack) {
        status.textContent = 'Ajouté ✓ — mais dans l’agenda PRINCIPAL du compte connecté.';
        alert('⚠️ L’événement a bien été créé dans Google Agenda, mais PAS dans « ' + (r.target || '') + ' ».\n\n' +
          'Raison : le compte Google que vous avez connecté n’a pas le droit d’écrire dans cet agenda (' + (r.reason || '') + ').\n\n' +
          'Deux solutions :\n' +
          '1) Connectez-vous avec le compte « ' + (r.target || 'eurocare…') + ' » lui-même (bouton « Connecter Google Agenda »), ou\n' +
          '2) Dans Google Agenda, partagez cet agenda avec votre compte en lui donnant « Apporter des modifications ».\n\n' +
          'Cliquez « 🔎 Vérifier l’agenda cible » pour voir les agendas où vous pouvez écrire.');
      } else if (r.ok) {
        status.textContent = 'Ajouté ✓ (aussi dans Google Agenda)';
      } else {
        status.textContent = 'Ajouté localement — échec Google.';
        alert('Google Agenda n’a pas pu enregistrer l’événement :\n\n' + (r.error || 'erreur inconnue') +
          '\n\nAstuce : reconnectez Google Agenda (bouton « Connecter Google Agenda »).');
      }
    }
    setTimeout(() => (status.textContent = ''), 6000);
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
// Lit un agenda de deux façons complémentaires :
//   1) « events » : chaque carte de rendez-vous est associée à son heure par sa
//      position verticale (utile quand les noms sont dans la grille et les heures
//      dans une colonne séparée — cas Doctoranytime).
//   2) « raw » : texte brut de la page (utile quand chaque ligne porte déjà son
//      heure — cas Doctena « 8:15 Jeanie Brunet »).
// Les deux sont fusionnés puis dédupliqués côté application.
const AGENDA_READ_JS = `(function(){
  function txtOf(el){ try { return ((el.innerText||el.textContent||'')+'').replace(/\\s+/g,' ').trim(); } catch(e){ return ''; } }
  var TIME_ONLY = /^\\s*(\\d{1,2})[:h.](\\d{2})\\s*$/;
  var TIME_ANY  = /(\\d{1,2})[:h.](\\d{2})/;
  var NAME_RE   = /[A-Za-zÀ-ÿ]{2,}/;
  function collect(doc, out){
    try { out.raw += '\\n' + ((doc.body && doc.body.innerText) || ''); } catch(e){}
    var all; try { all = doc.querySelectorAll('*'); } catch(e){ return; }
    // a) repère la colonne des heures (labels « 08:00 », « 8h30 »…)
    var labels = [];
    for (var i=0;i<all.length;i++){
      var t = txtOf(all[i]); var m = t.match(TIME_ONLY);
      if (!m) continue;
      var r; try { r = all[i].getBoundingClientRect(); } catch(e){ continue; }
      if (r.height>0 && r.width>0) labels.push({ y:r.top+r.height/2, left:r.left, right:r.right, time:('0'+m[1]).slice(-2)+':'+m[2] });
    }
    var gutterLeft = 0, yMin = 1e9, yMax = -1e9, gap = 40;
    if (labels.length){
      gutterLeft = labels[0].left;
      var ys = [];
      for (var k=0;k<labels.length;k++){
        if (labels[k].left<gutterLeft) gutterLeft=labels[k].left;
        if (labels[k].y<yMin) yMin=labels[k].y;
        if (labels[k].y>yMax) yMax=labels[k].y;
        ys.push(labels[k].y);
      }
      ys.sort(function(a,b){return a-b;});
      var diffs=[]; for (var k=1;k<ys.length;k++){ var d=ys[k]-ys[k-1]; if (d>2) diffs.push(d); }
      if (diffs.length){ diffs.sort(function(a,b){return a-b;}); gap = diffs[Math.floor(diffs.length/2)] || 40; }
    }
    // b) cartes de rendez-vous : plus petit élément portant un nom, à droite de la colonne des heures
    for (var i=0;i<all.length;i++){
      var el = all[i];
      if (el.children && el.children.length > 3) continue;      // conteneur, pas une carte
      var t = txtOf(el);
      if (!t || t.length > 160) continue;
      if (TIME_ONLY.test(t)) continue;                          // c'est un label d'heure
      if (!NAME_RE.test(t)) continue;                           // pas de nom
      var same = false;
      for (var c=0; c<el.children.length; c++){ if (txtOf(el.children[c]) === t){ same = true; break; } }
      if (same) continue;                                       // un enfant porte déjà tout le texte
      var r; try { r = el.getBoundingClientRect(); } catch(e){ continue; }
      if (r.height<=0 || r.width<=0) continue;
      var cy = r.top + r.height/2;
      if (labels.length){
        if (r.left < gutterLeft - 8) continue;                 // à gauche des heures = menu/sidebar
        if (cy < yMin - gap || cy > yMax + gap) continue;      // hors de la grille horaire (bannière/entête/pied)
      }
      // heure : d'abord dans le texte de la carte, sinon par la ligne la plus proche
      var time = null, own = t.match(TIME_ANY);
      if (own && t.indexOf(own[0]) < 4){ time = ('0'+own[1]).slice(-2)+':'+own[2]; }
      if (!time && labels.length){
        var best=null, bd=1e9;
        for (var k=0;k<labels.length;k++){ var d=Math.abs(labels[k].y-cy); if (d<bd){ bd=d; best=labels[k]; } }
        if (best && bd < Math.max(14, gap*0.6)) time = best.time;
      }
      if (!time) continue;
      out.events.push({ time:time, name:t });
    }
  }
  var out = { events: [], raw: '' };
  collect(document, out);
  var frames = document.querySelectorAll('iframe');
  for (var f=0; f<frames.length; f++){ try { collect(frames[f].contentDocument, out); } catch(e){} }
  return JSON.stringify(out);
})()`;

async function readAgendaWv(wv) {
  if (!wv) return '';
  try {
    return await wv.executeJavaScript(AGENDA_READ_JS, true);
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

// Un texte contient-il un vrai nom (au moins un mot de 2 lettres) ?
function hasName(s) {
  return /[a-zà-ÿ]{2,}/i.test(cleanApptName(s));
}

// Rejette les e-mails / adresses web (souvent affichés dans les cartes de RDV).
function looksLikeContact(s) {
  const t = (s || '').toLowerCase();
  if (/@/.test(t)) return true;
  if (/\b(gmail|outlook|hotmail|yahoo|proton|icloud|live|orange|skynet|telenet|voo)\b/.test(t)) return true;
  if (/\.(com|be|fr|net|org|eu)\b/.test(t)) return true;
  if (/https?:|www\./.test(t)) return true;
  return false;
}

// Un vrai nom de patient : au moins deux mots, chacun capitalisé (ou tout en
// majuscules pour les noms de famille), sans chiffre ni libellé d'interface.
const NAME_PARTICLE = /^(de|du|des|la|le|les|van|von|der|den|el|al|bin|ben|da|di|do|dos|das|ait|ould|abd|of|d)$/i;
function looksLikePersonName(s) {
  const clean = cleanApptName(s);
  if (!clean || looksLikeContact(clean) || /\d/.test(clean)) return false;
  const tokens = clean.split(/\s+/).filter(Boolean);
  if (tokens.length < 2 || tokens.length > 5) return false;
  let strong = 0;
  for (const tok of tokens) {
    if (NAME_PARTICLE.test(tok)) continue;
    if (/^[A-ZÀ-Ý][a-zà-ÿ'’.-]*$/.test(tok) || /^[A-ZÀ-Ý'’-]{2,}$/.test(tok)) { strong++; continue; }
    return false;   // un mot ni capitalisé ni particule → ce n'est pas un nom
  }
  return strong >= 2;
}

// Noms à exclure : le médecin choisi + les noms de la bannière Doctoranytime
// (« connecté en tant que X … agenda de Y »).
function bannerExcludes(raw, doctorName) {
  const set = new Set();
  const addN = (nm) => { const n = normName(nm); if (n) set.add(n); };
  if (doctorName) addN(doctorName);
  const r = raw || '';
  const NAME = "[A-ZÀ-Ý][\\p{L}'’.-]+(?:\\s+[A-ZÀ-Ý][\\p{L}'’.-]+){0,2}";
  let m;
  const re1 = new RegExp('en tant que\\s+(' + NAME + ')', 'gu');
  const re2 = new RegExp("agenda\\s+(?:de\\s+)?(?:la\\s+|l['’]\\s*)?(" + NAME + ')', 'gu');
  const re3 = new RegExp('Dr[e]?\\.?\\s+(' + NAME + ')', 'gu');
  while ((m = re1.exec(r))) addN(m[1]);
  while ((m = re2.exec(r))) addN(m[1]);
  while ((m = re3.exec(r))) addN(m[1]);
  return set;
}

// Détecte une page de connexion / sélection (pas un agenda).
function looksLikeLogin(text) {
  return /(connectez-vous|mot de passe|se souvenir de moi|s[ée]lectionner un h[ôo]pital|\bpassword\b|\blog\s?in\b|identifiant)/i.test(text || '');
}

// Rejette les faux « rendez-vous » : en-têtes de semaine/date, fuseau horaire,
// libellés d'interface (menus, bannières…).
const UI_NOISE = /\b(aujourd|semaine|jour|mois|agenda|layouts?|rechercher|recherche|contacter|support|chat disponible|changer d.utilisateur|en tant que|acc[ée]dant|connect[ée]|d[ée]connexion|param[èe]tres|r[ée]glages|nouveau|ajouter|imprimer|t[âa]ches?|filtres?|options?|profil|compte|notifications?)\b/;
const MONTHS_ONLY = /^(janvier|f[ée]vrier|mars|avril|mai|juin|juillet|ao[uû]t|septembre|octobre|novembre|d[ée]cembre)(\s+\d{4})?$/;
function looksLikeHeader(name) {
  const n = (name || '').toLowerCase().trim();
  if (/\bsem\.?\s*\d|\bsemaine\b|aujourd|\bgmt\b|\bphone\b|\bemail\b/.test(n)) return true;
  if (/^\W*\d{1,2}\s*(janv|f[eé]vr|mars|avri|mai|juin|juil|ao[uû]t|sept|octo|nove|d[eé]ce)/.test(n)) return true;
  if (/^(lun|mar|mer|jeu|ven|sam|dim)\.?\b/.test(n) && n.replace(/[^a-zà-ÿ]/g, '').length < 9) return true;
  if (MONTHS_ONLY.test(n)) return true;
  if (UI_NOISE.test(n)) return true;
  return false;
}

// Extrait le texte brut (.raw) d'une lecture d'agenda (JSON ou texte simple).
function rawOf(read) {
  if (typeof read !== 'string') return '';
  try { const o = JSON.parse(read); if (o && typeof o === 'object') return o.raw || ''; } catch (_) {}
  return read;
}

// Fusionne les rendez-vous « géométriques » (events) et ceux lus ligne par ligne
// (raw), dédupliqués par nom. Les heures « inline » (raw) sont prioritaires.
function apptsFromRead(read, doctorName) {
  let data = null;
  if (typeof read === 'string') { try { data = JSON.parse(read); } catch (_) {} }
  const out = [];
  const seen = new Set();
  const raw = data && typeof data.raw === 'string' ? data.raw
            : (data ? '' : (read || ''));
  const excl = bannerExcludes(raw, doctorName);
  const add = (time, src) => {
    const name = cleanApptName(src);
    if (!hasName(name) || looksLikeHeader(name) || !looksLikePersonName(src)) return;
    const norm = normName(src);
    if (!norm || seen.has(norm) || excl.has(norm)) return;
    seen.add(norm);
    out.push({ time, name, norm });
  };
  // 1) raw d'abord : heures les plus fiables (chaque ligne porte son heure).
  parseAppts(raw).forEach((a) => add(a.time, a.name));
  // 2) events (association par position) : complète les rendez-vous manquants.
  if (data && Array.isArray(data.events)) data.events.forEach((e) => add(e.time, e.name));
  out.sort((a, b) => (a.time || '').localeCompare(b.time || ''));
  return out;
}

function parseAppts(text) {
  const lines = (text || '').split('\n').map((l) => l.trim()).filter(Boolean);
  const out = [];
  const push = (time, src) => {
    const name = cleanApptName(src);
    if (!hasName(name) || looksLikeHeader(name)) return false;
    out.push({ time, name, norm: normName(src) });
    return true;
  };
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    if (/gmt\s*[+-]?\s*\d/i.test(line)) continue; // ligne de fuseau horaire
    const m = line.match(/(\d{1,2})[:h.](\d{2})/);
    if (!m) continue;
    const idx = line.indexOf(m[0]);
    if (line[idx - 1] === '+') continue; // « GMT+02:00 »
    const time = m[1].padStart(2, '0') + ':' + m[2];
    let rest = line.slice(idx + m[0].length).trim();
    // Retire une éventuelle 2e heure (plage « 08:00 - 11:00 »).
    rest = rest.replace(/^[\s\-–—à]*\d{1,2}[:h.]\d{2}\s*/, '').trim();
    if (rest && push(time, rest)) continue;
    // Nom sur la/les ligne(s) suivante(s) — cas Doctoranytime (heure puis nom).
    for (let j = i + 1; j < Math.min(i + 3, lines.length); j++) {
      if (/\d{1,2}[:h.]\d{2}/.test(lines[j])) break; // prochaine heure : on arrête
      if (push(time, lines[j])) { i = j; break; }
    }
  }
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

function renderSyncResult(res, aList, bList, context) {
  const box = document.getElementById('syncResult');
  const esc = escapeHtml;
  const aCount = aList.length, bCount = bList.length;

  // Statut par rendez-vous (pour colorer chaque ligne des deux listes).
  const statusA = {}, statusB = {};
  res.matched.forEach((p) => { statusA[p.a.norm] = 'ok'; statusB[p.b.norm] = 'ok'; });
  res.timeDiff.forEach((p) => { statusA[p.a.norm] = 'warn'; statusB[p.b.norm] = 'warn'; });
  res.onlyA.forEach((a) => { statusA[a.norm] = 'bad'; });
  res.onlyB.forEach((b) => { statusB[b.norm] = 'bad'; });
  const dot = { ok: '🟢', warn: '🟠', bad: '🔴' };

  let html = context ? `<div class="sync-context">📋 ${esc(context)}</div>` : '';

  // --- Verdict global : identiques, ou erreurs (noms manquants / créneaux) ---
  const nbMissing = res.onlyA.length + res.onlyB.length;
  const nbSlot = res.timeDiff.length;
  const nbErr = nbMissing + nbSlot;
  if (aCount && bCount) {
    if (nbErr === 0) {
      html += '<div class="sync-verdict ok">✅ Agendas IDENTIQUES — mêmes patients, mêmes créneaux.</div>';
    } else {
      const parts = [];
      if (nbMissing) parts.push(`${nbMissing} patient(s) présent(s) d'un seul côté`);
      if (nbSlot) parts.push(`${nbSlot} patient(s) sur un créneau horaire différent`);
      html += `<div class="sync-verdict bad">⛔ ${nbErr} DIFFÉRENCE(S) — ${esc(parts.join(' · '))}. Corrigez les points ci-dessous.</div>`;
    }
  }
  // Alerte si un agenda montre beaucoup plus de RDV que l'autre (probable vue
  // multi-praticiens au lieu d'un seul praticien).
  const big = Math.max(aCount, bCount), small = Math.min(aCount, bCount);
  if (small > 0 && big >= small * 2 && big - small >= 6) {
    const many = aCount > bCount ? 'Doctena' : 'Doctoranytime';
    html += `<div class="sync-verdict warn">⚠️ « ${many} » affiche beaucoup plus de rendez-vous (${big}) que l'autre (${small}). ` +
      'Vérifiez qu\'il est bien réglé sur UN SEUL praticien en vue « Jour » (pas la vue de tout le cabinet), sinon la comparaison est faussée.</div>';
  }

  html += '<div class="sync-summary">' +
    `<span class="sync-badge">Doctena : ${aCount} RDV</span>` +
    `<span class="sync-badge">Doctoranytime : ${bCount} RDV</span>` +
    `<span class="sync-badge ok">🟢 ${res.matched.length} concordants</span>` +
    `<span class="sync-badge warn">🟠 ${res.timeDiff.length} écarts d'horaire</span>` +
    `<span class="sync-badge bad">🔴 ${res.onlyA.length + res.onlyB.length} manquants</span>` +
    '</div>';

  // --- Les deux listes brutes, côte à côte (le cœur de la demande) ---------
  const listCol = (title, list, status) => {
    const rows = list.length
      ? list.map((x) => {
          const st = status[x.norm] || '';
          return `<div class="sync-row ${st}"><span class="dot">${dot[st] || '⚪'}</span>` +
                 `<span class="t">${esc(x.time || '—')}</span><span>${esc(x.name)}</span></div>`;
        }).join('')
      : '<div class="hint">Aucun rendez-vous lu. Ouvrez l’agenda sur la vue « Jour ».</div>';
    return `<div class="sync-group"><h3>${title} (${list.length})</h3><div class="sync-rows">${rows}</div></div>`;
  };
  html += '<div class="sync-lists">' +
    listCol('🗓️ Agenda Doctena', aList, statusA) +
    listCol('🗓️ Agenda Doctoranytime', bList, statusB) +
    '</div>';

  // --- Détail des différences détectées automatiquement --------------------
  const group = (cls, title, rows) => {
    if (!rows.length) return '';
    return `<div class="sync-group ${cls}"><h3>${title} (${rows.length})</h3><div class="sync-rows">${rows.join('')}</div></div>`;
  };
  let diffs = '';
  diffs += group('bad', '⛔ Dans Doctena, ABSENT de Doctoranytime',
    res.onlyA.map((a) => `<div class="sync-row"><span class="t">${a.time}</span><span>${esc(a.name)}</span></div>`));
  diffs += group('bad', '⛔ Dans Doctoranytime, ABSENT de Doctena',
    res.onlyB.map((b) => `<div class="sync-row"><span class="t">${b.time}</span><span>${esc(b.name)}</span></div>`));
  diffs += group('warn', '⛔ ERREUR créneau : même patient, horaire différent (Doctena → Doctoranytime)',
    res.timeDiff.map((p) => `<div class="sync-row"><span class="t">${p.a.time}→${p.b.time}</span><span>${esc(p.a.name)}</span></div>`));
  diffs += group('warn', '⚠️ Doublons dans un même agenda',
    res.dupes.map((d) => `<div class="sync-row"><span>${esc(d.name)}</span><span class="arrow">— ${d.src}</span></div>`));
  if (diffs) {
    html += '<div class="sync-diff-title">Différences détectées</div>' +
            '<div class="sync-diff">' + diffs + '</div>';
  } else if (aCount || bCount) {
    html += '<p class="hint ok-hint">✓ Aucune différence : les deux agendas concordent.</p>';
  }

  if (!aCount && !bCount) {
    html += '<p class="hint">Aucun rendez-vous détecté. Vérifiez que les deux onglets sont bien ouverts sur la vue « Jour » du praticien, puis relancez.</p>';
  }
  box.innerHTML = html;
}

function setupSync() {
  const status = document.getElementById('syncStatus');
  const doctorEl = document.getElementById('syncDoctor');
  const dateEl = document.getElementById('syncDate');
  const fromEl = document.getElementById('syncFrom');
  const toEl = document.getElementById('syncTo');

  // Liste des médecins pour l'auto-complétion.
  const dl = document.getElementById('syncDocList');
  (settings.doctors || []).slice().sort((a, b) => a.name.localeCompare(b.name)).forEach((d) => {
    const o = document.createElement('option'); o.value = d.name; dl.appendChild(o);
  });
  // Date du jour par défaut.
  if (!dateEl.value) {
    const t = new Date();
    dateEl.value = `${t.getFullYear()}-${pad2(t.getMonth() + 1)}-${pad2(t.getDate())}`;
  }

  const wvDoctena = () => document.getElementById('syncWvDoctena');
  const wvDa = () => document.getElementById('syncWvDa');

  const wvOf = (k) => (k === 'd' ? wvDoctena() : (k === 'a' ? wvDa() : null));

  // Script best-effort : clique le bouton « jour précédent / suivant / aujourd'hui »
  // de l'agenda affiché (Doctena / Doctoranytime), quels que soient leurs libellés.
  const navScript = (dir) => `(function(){
    function vis(el){ try{ var r=el.getBoundingClientRect(); return r.width>0&&r.height>0; }catch(e){ return false; } }
    function attrs(el){ return ((el.getAttribute&&(el.getAttribute('aria-label')||el.getAttribute('title')||el.getAttribute('data-tooltip'))||'')+' '+(el.textContent||'')+' '+((el.className&&el.className.baseVal!==undefined)?el.className.baseVal:(el.className||''))).toLowerCase(); }
    var dir=${JSON.stringify(dir)};
    var wants;
    if(dir==='today') wants=[/aujourd/,/today/,/\\bce jour\\b/];
    else if(dir==='prev') wants=[/pr[eé]c[eé]dent/,/previous/,/\\bprev\\b/,/back/,/arrow.?left/,/chevron.?left/,/fa-chevron-left/,/fa-angle-left/,/-left\\b/];
    else wants=[/suivant/,/next/,/forward/,/arrow.?right/,/chevron.?right/,/fa-chevron-right/,/fa-angle-right/,/-right\\b/];
    var cands=document.querySelectorAll('button,a,[role=button],i,svg,span,div');
    for(var i=0;i<cands.length;i++){
      var el=cands[i]; if(!vis(el)) continue;
      var s=attrs(el);
      for(var w=0;w<wants.length;w++){ if(wants[w].test(s)){ (el.closest('button,a,[role=button]')||el).click(); return 'ok'; } }
    }
    // Repli : flèches clavier pour changer de jour
    var key = dir==='prev' ? 37 : (dir==='next' ? 39 : null);
    if(key){ ['keydown','keyup'].forEach(function(t){ document.dispatchEvent(new KeyboardEvent(t,{keyCode:key,which:key,bubbles:true})); }); return 'key'; }
    return 'notfound';
  })()`;

  // Ferme une éventuelle fenêtre (modale/pop-up) puis revient à l'agenda.
  const closePopupScript = `(function(){
    document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',keyCode:27,which:27,bubbles:true}));
    var sel=['[aria-label*="ferm" i]','[aria-label*="close" i]','[title*="ferm" i]','[title*="close" i]','.close','.modal-close','button.close'];
    for(var s=0;s<sel.length;s++){ var el=document.querySelector(sel[s]); if(el){ try{ el.click(); }catch(e){} } }
    return 'ok';
  })()`;

  // Boutons de chaque panneau : recharger / ouvrir / naviguer / revenir / agrandir.
  const splitEl = document.querySelector('.sync-split');
  if (splitEl) {
    const zoomFactors = { d: 1, a: 1 };
    splitEl.addEventListener('click', async (e) => {
      const d = e.target.dataset || {};
      if (d.reload) { const wv = wvOf(d.reload); if (wv) wv.reload(); return; }
      if (d.ext) { const wv = wvOf(d.ext); if (wv) window.prive.openExternal(wv.getURL()); return; }
      if (d.zoom && d.pane) {
        const wv = wvOf(d.pane);
        if (!wv) return;
        let f = zoomFactors[d.pane] || 1;
        if (d.zoom === 'in') f = Math.min(2.5, f + 0.1);
        else if (d.zoom === 'out') f = Math.max(0.3, f - 0.1);
        else f = 1;
        zoomFactors[d.pane] = f;
        try { wv.setZoomFactor(f); } catch (_) {}
        status.textContent = `Zoom ${d.pane === 'd' ? 'Doctena' : 'Doctoranytime'} : ${Math.round(f * 100)}%`;
        setTimeout(() => (status.textContent = ''), 1500);
        return;
      }
      if (d.home) {
        const wv = wvOf(d.home);
        if (wv) { try { await wv.executeJavaScript(closePopupScript, true); } catch (_) {} }
        status.textContent = 'Fenêtre fermée — retour à l’agenda.';
        setTimeout(() => (status.textContent = ''), 1800);
        return;
      }
      if (d.nav && d.pane) {
        const wv = wvOf(d.pane);
        if (!wv) return;
        try {
          const r = await wv.executeJavaScript(navScript(d.nav), true);
          status.textContent = r === 'notfound'
            ? 'Bouton de navigation introuvable sur cet agenda — utilisez ses propres flèches.'
            : 'Jour changé.';
        } catch (_) { status.textContent = 'Navigation impossible.'; }
        setTimeout(() => (status.textContent = ''), 2200);
        return;
      }
      if (d.expand) {
        const pane = e.target.closest('.sync-pane');
        const wasExpanded = pane.classList.contains('expanded');
        splitEl.querySelectorAll('.sync-pane').forEach((p) => p.classList.remove('expanded'));
        splitEl.classList.toggle('has-expanded', !wasExpanded);
        if (!wasExpanded) pane.classList.add('expanded');
        return;
      }
    });
  }
  document.getElementById('syncReload').addEventListener('click', () => {
    ensureSyncPanes();
    [wvDoctena(), wvDa()].forEach((wv) => { try { wv.reload(); } catch (_) { /* pas encore prêt */ } });
    status.textContent = 'Rechargement des deux agendas…';
    setTimeout(() => (status.textContent = ''), 2000);
  });

  // Diagnostic : montre exactement les rendez-vous lus dans chaque agenda.
  document.getElementById('syncDebug').addEventListener('click', async () => {
    ensureSyncPanes();
    status.textContent = 'Lecture…';
    const [ta, tb] = await Promise.all([readAgendaWv(wvDoctena()), readAgendaWv(wvDa())]);
    const doc = doctorEl.value.trim();
    const a = apptsFromRead(ta, doc), b = apptsFromRead(tb, doc);
    const fmt = (list) => list.length
      ? list.map((x) => `<div class="sync-row"><span class="t">${x.time}</span><span>${escapeHtml(x.name)}</span></div>`).join('')
      : '<div class="hint">(rien détecté — l\'agenda est peut-être dans un cadre sécurisé illisible)</div>';
    document.getElementById('syncResult').innerHTML =
      `<div class="sync-group"><h3>Doctena — ${a.length} détecté(s)</h3>${fmt(a)}</div>` +
      `<div class="sync-group"><h3>Doctoranytime — ${b.length} détecté(s)</h3>${fmt(b)}</div>` +
      '<p class="hint">Si des rendez-vous visibles à l\'écran manquent dans cette liste, faites-moi une capture de ceci : j\'ajusterai l\'extraction précisément.</p>';
    status.textContent = `${a.length} + ${b.length} rendez-vous lus.`;
  });

  document.getElementById('syncCompare').addEventListener('click', async () => {
    ensureSyncPanes();
    status.textContent = 'Lecture des agendas…';
    const [ta, tb] = await Promise.all([readAgendaWv(wvDoctena()), readAgendaWv(wvDa())]);
    const doc = doctorEl.value.trim();
    let aList = apptsFromRead(ta, doc);
    let bList = apptsFromRead(tb, doc);
    const rawA = rawOf(ta), rawB = rawOf(tb);
    // Diagnostics : un panneau est-il sur une page de connexion / sélection ?
    const notes = [];
    if (!aList.length && (looksLikeLogin(rawA) || !rawA)) {
      notes.push('⚠️ Doctena n’est pas sur un agenda : connectez-vous dans le panneau de gauche, puis ouvrez la vue « Jour » du praticien.');
    }
    if (!bList.length && (looksLikeLogin(rawB) || !rawB)) {
      notes.push('⚠️ Doctoranytime n’est pas sur un agenda : sélectionnez l’hôpital → « Sélectionner un praticien » → vue « Jour ».');
    }

    // Filtre optionnel sur la plage horaire choisie.
    const from = fromEl.value, to = toEl.value;
    const inWindow = (t) => (!from || t >= from) && (!to || t <= to);
    if (from || to) { aList = aList.filter((x) => inWindow(x.time)); bList = bList.filter((x) => inWindow(x.time)); }

    const res = compareAgendas(aList, bList);
    const ctx = [];
    if (doctorEl.value.trim()) ctx.push('Dr ' + doctorEl.value.trim());
    if (dateEl.value) ctx.push(dateEl.value.split('-').reverse().join('/'));
    if (from || to) ctx.push(`${from || '…'}–${to || '…'}`);
    renderSyncResult(res, aList, bList, ctx.join(' · '));

    // Ajoute les avertissements en tête des résultats.
    if (notes.length) {
      const box = document.getElementById('syncResult');
      box.insertAdjacentHTML('afterbegin',
        '<div class="sync-notes">' + notes.map((n) => `<div>${escapeHtml(n)}</div>`).join('') + '</div>');
    }

    const problems = res.onlyA.length + res.onlyB.length + res.timeDiff.length + res.dupes.length;
    if (notes.length) {
      status.textContent = 'Un ou deux agendas ne sont pas encore affichés — voir les avertissements.';
    } else {
      status.textContent = problems === 0
        ? `Agendas synchronisés ✓ (${aList.length} RDV comparés)`
        : `${problems} anomalie(s) détectée(s).`;
    }
  });
}

// ---------------------------------------------------------------------------
// Shyfter — formulaire de disponibilités (lot + best-effort)
// ---------------------------------------------------------------------------

// Lit les pointages/horaires affichés dans Shyfter : renvoie la liste des
// personnes avec leur plage horaire et la section (jour) à laquelle elles
// appartiennent (« Pointages du jour », « Pointages d'hier », une date…).
const SHYFTER_READ_JS = `(function(){
  function collapse(s){ return (s||'').replace(/[ \\t]+/g,' ').trim(); }
  var RANGE=/(\\d{1,2}[:h]\\d{2})\\s*[-–—]\\s*(\\d{1,2}[:h]\\d{2}|en cours)/i;
  var SECTION=/(pointages?\\s+(du jour|d.?hier)|personnel\\s+.\\s*l.horaire\\s+(aujourd|demain)|planning|semaine|p[ée]riode)/i;
  var STATUS=/^(arriv|parti|en cours|en retard|en avance|absent|cong[ée]|\\bva\\b|\\brm\\b|\\beuro)/i;
  var all=document.querySelectorAll('body *');
  var section='', out=[], seen={};
  function isName(c){
    if(!c || c.length<2 || c.length>48) return false;
    if(!/[a-zà-ÿ]{2,}/i.test(c)) return false;
    if(RANGE.test(c) || STATUS.test(c)) return false;
    if(/^\\d/.test(c)) return false;
    return true;
  }
  for(var i=0;i<all.length;i++){
    var el=all[i];
    var full='';
    try{ full=(el.innerText||el.textContent||''); }catch(e){ continue; }
    var flat=collapse(full.replace(/\\n+/g,' '));
    if(!flat) continue;
    // Titre de section (élément court)
    if(flat.length<=48 && SECTION.test(flat) && !RANGE.test(flat)){ section=flat; continue; }
    if(flat.length>200 || !RANGE.test(flat)) continue;
    // Cellules de la ligne (séparées par des sauts de ligne dans le rendu)
    var cells=full.split('\\n').map(collapse).filter(Boolean);
    if(cells.length<2) continue;
    // Cette ligne doit être « serrée » : on écarte si un enfant contient déjà une plage.
    var childRange=false;
    for(var c=0;c<el.children.length;c++){ try{ if(RANGE.test(el.children[c].innerText||'')){ childRange=true; break; } }catch(e){} }
    if(childRange) continue;
    // Trouve la cellule d'heure et la cellule de nom.
    var m=null, ri=-1;
    for(var k=0;k<cells.length;k++){ var mm=cells[k].match(RANGE); if(mm){ m=mm; ri=k; break; } }
    if(!m) continue;
    var name='';
    for(var k=0;k<cells.length;k++){ if(k===ri) continue; if(/^(e[mt]|zz?|z)[-\\s]/i.test(cells[k]) && isName(cells[k])){ name=cells[k]; break; } }
    if(!name){ for(var k=0;k<cells.length;k++){ if(k!==ri && isName(cells[k])){ name=cells[k]; break; } } }
    if(!name) continue;
    name=name.replace(/^[•\\-\\u2013\\s]+/,'').replace(/\\s+(eurocare|euro).*$/i,'').trim();
    if(!isName(name)) continue;
    var key=name.toLowerCase()+'|'+m[1]+'|'+section;
    if(seen[key]) continue; seen[key]=1;
    out.push({ name:name, start:m[1].replace('h',':'), end:(/en cours/i.test(m[2])?'':m[2].replace('h',':')), status:'', day:section });
  }
  return JSON.stringify({ rows:out, login:/mot de passe|se connecter|password|log ?in/i.test((document.body&&document.body.innerText)||'') });
})()`;

function setupShyfter() {
  const wv = () => document.getElementById('shyWv');
  const readStatus = document.getElementById('shyReadStatus');
  const nameSel = document.getElementById('shyFName');
  const daySel = document.getElementById('shyFDay');
  const atTime = document.getElementById('shyFTime');
  const fromTime = document.getElementById('shyFFrom');
  const toTime = document.getElementById('shyFTo');
  const countEl = document.getElementById('shyFCount');
  const resultsEl = document.getElementById('shyFResults');

  let entries = [];

  const countersEl = document.getElementById('shyCounters');

  const toMin = (t) => {
    const m = /(\d{1,2})[:h](\d{2})/.exec(t || '');
    return m ? (Number(m[1]) * 60 + Number(m[2])) : null;
  };
  const fmtDur = (min) => {
    if (!min || min <= 0) return '0h00';
    return `${Math.floor(min / 60)}h${pad2(min % 60)}`;
  };
  // Date associée à un libellé de section (« du jour » = aujourd'hui, « d'hier »
  // = hier, une date explicite si présente).
  const dayToDate = (label) => {
    const l = (label || '').toLowerCase();
    const base = new Date(); base.setHours(0, 0, 0, 0);
    const md = l.match(/(\d{1,2})[\/.](\d{1,2})(?:[\/.](\d{2,4}))?/);
    if (md) {
      const y = md[3] ? (md[3].length === 2 ? 2000 + Number(md[3]) : Number(md[3])) : base.getFullYear();
      return new Date(y, Number(md[2]) - 1, Number(md[1]));
    }
    if (/hier/.test(l)) { base.setDate(base.getDate() - 1); return base; }
    if (/demain/.test(l)) { base.setDate(base.getDate() + 1); return base; }
    if (/jour|aujourd|horaire/.test(l)) return base;
    return null;
  };
  // Durée pointée d'une entrée (en minutes). « En cours » : jusqu'à maintenant
  // si c'est aujourd'hui, sinon 0.
  const entryMinutes = (e) => {
    const s = toMin(e.start); if (s == null) return 0;
    let en;
    if (e.end) { en = toMin(e.end); }
    else {
      const dd = dayToDate(e.day); const t = new Date();
      if (dd && dd.toDateString() !== new Date(t.getFullYear(), t.getMonth(), t.getDate()).toDateString()) return 0;
      en = t.getHours() * 60 + t.getMinutes();
    }
    if (en == null || en < s) return 0;
    return en - s;
  };

  const fillSelect = (sel, values, keepAll) => {
    const cur = sel.value;
    sel.innerHTML = keepAll ? `<option value="">${keepAll}</option>` : '';
    values.forEach((v) => {
      const o = document.createElement('option');
      o.value = v; o.textContent = v; sel.appendChild(o);
    });
    if ([...sel.options].some((o) => o.value === cur)) sel.value = cur;
  };

  const render = () => {
    const wantName = nameSel.value;
    const wantDay = daySel.value;
    const at = toMin(atTime.value);
    const fMin = toMin(fromTime.value);
    const tMin = toMin(toTime.value);

    const rows = entries.filter((e) => {
      if (wantName && e.name !== wantName) return false;
      if (wantDay && e.day !== wantDay) return false;
      const s = toMin(e.start);
      const en = e.end ? toMin(e.end) : 24 * 60;   // « en cours » = jusqu'à la fin de journée
      if (at != null) { if (s == null || at < s || at > en) return false; }
      if (fMin != null && en != null && en < fMin) return false;
      if (tMin != null && s != null && s > tMin) return false;
      return true;
    });

    countEl.textContent = `${rows.length} personne(s)`;

    // --- Compteur d'heures (jour / semaine / mois / année) ---
    // Basé sur le nom choisi (ou tout le monde) ; sur les données lues.
    const t = new Date();
    const monday = new Date(t); monday.setHours(0, 0, 0, 0);
    monday.setDate(monday.getDate() - ((monday.getDay() + 6) % 7)); // lundi de la semaine
    const nextMonday = new Date(monday); nextMonday.setDate(nextMonday.getDate() + 7);
    let tDay = 0, tWeek = 0, tMonth = 0, tYear = 0, tAll = 0;
    entries.filter((e) => !wantName || e.name === wantName).forEach((e) => {
      const min = entryMinutes(e); tAll += min;
      const dd = dayToDate(e.day); if (!dd) return;
      if (dd.toDateString() === new Date(t.getFullYear(), t.getMonth(), t.getDate()).toDateString()) tDay += min;
      if (dd >= monday && dd < nextMonday) tWeek += min;
      if (dd.getFullYear() === t.getFullYear() && dd.getMonth() === t.getMonth()) tMonth += min;
      if (dd.getFullYear() === t.getFullYear()) tYear += min;
    });
    const who = wantName ? escapeHtml(wantName) : 'Tous';
    const cnt = (lbl, v) => `<div class="shy-counter"><span class="c-val">${fmtDur(v)}</span><span class="c-lbl">${lbl}</span></div>`;
    if (countersEl) {
      countersEl.innerHTML = entries.length
        ? `<div class="shy-counter-who">${who}</div>` +
          cnt('Jour', tDay) + cnt('Semaine', tWeek) + cnt('Mois', tMonth) + cnt('Année', tYear) +
          `<div class="shy-counter-note">Total lu : ${fmtDur(tAll)}. Semaine/mois/année = d'après les jours affichés dans Shyfter — pour l'historique complet, ouvrez le rapport des prestations puis relisez.</div>`
        : '';
    }

    if (!entries.length) {
      resultsEl.innerHTML = '<div class="hint">Cliquez « 🔄 Lire les pointages » (Shyfter doit afficher les pointages/horaires à gauche).</div>';
      return;
    }
    if (!rows.length) { resultsEl.innerHTML = '<div class="hint">Personne ne correspond à ces filtres.</div>'; return; }
    resultsEl.innerHTML = rows.map((e) => {
      const range = e.end ? `${e.start}–${e.end}` : `${e.start} · en cours`;
      const st = e.status ? `<span class="sr-status">${escapeHtml(e.status)}</span>` : '';
      const day = e.day ? `<span class="sr-day">${escapeHtml(e.day)}</span>` : '';
      return `<div class="shy-result"><span class="sr-time">${range}</span>` +
        `<span class="sr-name">${escapeHtml(e.name)}</span>${st}${day}</div>`;
    }).join('');
  };

  const refreshFilters = () => {
    const names = [...new Set(entries.map((e) => e.name))].sort((a, b) => a.localeCompare(b));
    const days = [...new Set(entries.map((e) => e.day).filter(Boolean))];
    fillSelect(nameSel, names, '— Tous —');
    fillSelect(daySel, days, '— Tous les jours lus —');
  };

  document.getElementById('shyRead').addEventListener('click', async () => {
    ensureShyfterPane();
    readStatus.textContent = 'Lecture…';
    let data = {};
    try { data = JSON.parse(await wv().executeJavaScript(SHYFTER_READ_JS, true)); } catch (_) { data = {}; }
    entries = (data.rows || []).map((r) => ({
      name: r.name, start: r.start, end: r.end, status: r.status, day: r.day || '—'
    }));
    if (!entries.length) {
      readStatus.textContent = data.login
        ? 'Connectez-vous à Shyfter dans le panneau de gauche, puis relisez.'
        : 'Aucun pointage lu — ouvrez la page qui liste les horaires/pointages, puis relisez.';
    } else {
      readStatus.textContent = `${entries.length} ligne(s) lue(s) ✓`;
    }
    refreshFilters();
    render();
    setTimeout(() => (readStatus.textContent = ''), 4000);
  });

  [nameSel, daySel].forEach((el) => el.addEventListener('change', render));
  [atTime, fromTime, toTime].forEach((el) => el.addEventListener('input', render));
  document.getElementById('shyFReset').addEventListener('click', () => {
    nameSel.value = ''; daySel.value = ''; atTime.value = ''; fromTime.value = ''; toTime.value = '';
    render();
  });

  document.getElementById('shyReload').addEventListener('click', () => { ensureShyfterPane(); try { wv().reload(); } catch (_) {} });
  document.getElementById('shyExt').addEventListener('click', () => { const w = wv(); if (w) window.prive.openExternal(w.getURL()); });

  // Lecture automatique quand la page Shyfter finit de charger (remplit la liste
  // déroulante des noms sans action manuelle).
  const w = wv();
  if (w) {
    w.addEventListener('did-stop-loading', () => {
      setTimeout(() => { try { document.getElementById('shyRead').click(); } catch (_) {} }, 1500);
    });
  }

  render();
}

// ---------------------------------------------------------------------------
// InBody — vue divisée redimensionnable (InBody + ChatGPT)
// ---------------------------------------------------------------------------

function setupInbody() {
  const wv = () => document.getElementById('inbodyWv');
  const gpt = () => document.getElementById('inbodyGptWv');
  const wrap = document.getElementById('inbodySplit');
  const left = document.getElementById('inbodyLeft');
  const divider = document.getElementById('inbodyDivider');

  document.getElementById('ibReload').addEventListener('click', () => { ensureInbodyPanes(); try { wv().reload(); } catch (_) {} });
  document.getElementById('ibExt').addEventListener('click', () => { const w = wv(); if (w) window.prive.openExternal(w.getURL()); });
  document.getElementById('ibGptReload').addEventListener('click', () => { ensureInbodyPanes(); try { gpt().reload(); } catch (_) {} });
  document.getElementById('ibGptExt').addEventListener('click', () => { const w = gpt(); if (w) window.prive.openExternal(w.getURL()); });

  document.getElementById('ibPrint').addEventListener('click', () => {
    try { wv().print({}); } catch (e) { alert('Impression impossible : ' + e.message); }
  });

  // Ouvre le rapport du test le plus récent (1re ligne) puis imprime.
  document.getElementById('ibLastPrint').addEventListener('click', async () => {
    const btn = document.getElementById('ibLastPrint');
    const prev = btn.textContent; btn.disabled = true; btn.textContent = 'Ouverture…';
    const script = `(function(){
      function firstRow(){ var rows=document.querySelectorAll('tbody tr,[role="row"]'); for(var i=0;i<rows.length;i++){ if(rows[i].querySelector('td,[role="cell"],[role="gridcell"]')) return rows[i]; } return null; }
      var row=firstRow(); if(!row) return 'norow';
      var els=row.querySelectorAll('a,button,img,svg,i,[role="button"]'), target=null;
      for(var i=0;i<els.length;i++){ var s=((els[i].getAttribute&&(els[i].getAttribute('title')||els[i].getAttribute('alt')||els[i].getAttribute('aria-label')))||'')+' '+(els[i].className&&els[i].className.baseVal!==undefined?els[i].className.baseVal:(els[i].className||'')); if(/report|rapport/i.test(s)){ target=els[i]; break; } }
      if(!target){ var ic=row.querySelectorAll('td a,td button,td img,td svg'); if(ic.length>=2) target=ic[1]; else if(ic.length===1) target=ic[0]; }
      if(target){ (target.closest('a,button')||target).click(); return 'clicked'; }
      return 'notfound';
    })()`;
    let outcome = 'notfound';
    try { outcome = await wv().executeJavaScript(script, true); } catch (_) { outcome = 'error'; }
    if (outcome === 'clicked') {
      setTimeout(() => { try { wv().print({}); } catch (_) {} btn.textContent = prev; btn.disabled = false; }, 2800);
    } else {
      btn.textContent = prev; btn.disabled = false;
      alert('Impossible d’ouvrir automatiquement le dernier test. Ouvrez son rapport puis « Imprimer affiché ».');
    }
  });

  // Redimensionnement par glissement de la barre centrale.
  let dragging = false;
  divider.addEventListener('mousedown', (e) => { dragging = true; wrap.classList.add('dragging'); e.preventDefault(); });
  window.addEventListener('mousemove', (e) => {
    if (!dragging) return;
    const r = wrap.getBoundingClientRect();
    let pct = ((e.clientX - r.left) / r.width) * 100;
    pct = Math.max(20, Math.min(80, pct));
    left.style.flex = '0 0 ' + pct + '%';
  });
  window.addEventListener('mouseup', () => { if (dragging) { dragging = false; wrap.classList.remove('dragging'); } });
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
  setupShyfter();
  setupInbody();
  setupSync();
  setupVault();
  setupCareconnect();
  setupSettings();
  fillSettingsForm();
  setupSpeed();
  setupMarquee();
  showView('accueil');
}

window.addEventListener('DOMContentLoaded', init);
