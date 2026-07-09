'use strict';

const { app, BrowserWindow, ipcMain, shell, session, dialog, safeStorage } = require('electron');
const path = require('path');
const fs = require('fs');
const { spawn } = require('child_process');
const { loadSettings, saveSettings } = require('./settings');
const { searchFiles } = require('./fileSearch');
const { startSpeedTest, runSpeedTestOnce } = require('./speedtest');
const { chatCompletion } = require('./chat');
const googleCal = require('./google');

// Un User-Agent Chrome « standard » : certains sites (Google, etc.) refusent
// de se charger dans un moteur qu'ils considèrent obsolète ou non sécurisé.
const CHROME_UA =
  'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36';

let mainWindow = null;

// L'icône est embarquée dans les ressources en version packagée, et dans build/
// en développement.
const iconPath = app.isPackaged
  ? path.join(process.resourcesPath, 'icon.ico')
  : path.join(__dirname, '..', 'build', 'icon.ico');

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1400,
    height: 900,
    minWidth: 1000,
    minHeight: 640,
    title: 'IATECH-CONTROL PRO',
    backgroundColor: '#0b1220',
    icon: iconPath,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false,
      // Nécessaire pour les onglets qui affichent des sites externes via <webview>.
      webviewTag: true
    }
  });

  mainWindow.setMenuBarVisibility(false);
  mainWindow.loadFile(path.join(__dirname, 'renderer', 'index.html'));

  // Le test de vitesse tourne en continu et pousse les résultats vers l'UI.
  startSpeedTest((data) => {
    if (mainWindow && !mainWindow.isDestroyed()) {
      mainWindow.webContents.send('speed:update', data);
    }
  });

  mainWindow.on('closed', () => {
    mainWindow = null;
  });
}

app.whenReady().then(() => {
  // Applique un UA moderne à toutes les requêtes.
  session.defaultSession.setUserAgent(CHROME_UA);

  // Autorise le micro/caméra (appels WebRTC du help desk) et les notifications.
  session.defaultSession.setPermissionRequestHandler((_wc, permission, callback) => {
    callback(['media', 'audioCapture', 'videoCapture', 'notifications', 'clipboard-read', 'clipboard-sanitized-write'].includes(permission));
  });

  createWindow();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});

// --- Certificats auto-signés (serveur interne du cabinet, ex. 192.168.1.220) ---
// On n'accepte l'erreur de certificat QUE pour les hôtes explicitement autorisés
// dans les réglages, jamais globalement.
app.on('certificate-error', (event, webContents, url, error, certificate, callback) => {
  try {
    const settings = loadSettings();
    const allowed = (settings.allowedInsecureHosts || []).map((h) => h.toLowerCase());
    const host = new URL(url).host.toLowerCase();
    if (allowed.some((h) => host === h || host.startsWith(h))) {
      event.preventDefault();
      callback(true);
      return;
    }
  } catch (_) {
    /* ignore, on refuse par défaut */
  }
  callback(false);
});

// Les fenêtres pop-up des sites intégrés (ex. la fenêtre « Création d'un shift »
// de Shyfter) s'ouvrent DANS l'application, en conservant la session, au lieu
// d'être renvoyées vers le navigateur système.
app.on('web-contents-created', (_event, contents) => {
  contents.setWindowOpenHandler(({ url }) => {
    if (url === 'about:blank' || url.startsWith('http://') || url.startsWith('https://')) {
      return {
        action: 'allow',
        overrideBrowserWindowOptions: {
          autoHideMenuBar: true,
          backgroundColor: '#ffffff',
          webPreferences: { partition: 'persist:prive' }
        }
      };
    }
    return { action: 'deny' };
  });
});

// ----------------------------- IPC ------------------------------------------

ipcMain.handle('settings:get', () => loadSettings());

ipcMain.handle('settings:save', (_e, next) => {
  saveSettings(next);
  return loadSettings();
});

ipcMain.handle('files:search', async (_e, opts) => {
  return searchFiles(opts || {});
});

ipcMain.handle('shell:openPath', async (_e, p) => {
  return shell.openPath(p);
});

ipcMain.handle('shell:showItem', async (_e, p) => {
  shell.showItemInFolder(p);
  return true;
});

ipcMain.handle('shell:openExternal', async (_e, url) => {
  return shell.openExternal(url);
});

// Ouvre un sélecteur de fichier pour choisir un exécutable (.exe).
ipcMain.handle('dialog:pickExe', async () => {
  const result = await dialog.showOpenDialog(mainWindow, {
    title: 'Sélectionner CareConnect.exe',
    defaultPath: 'C:\\',
    properties: ['openFile'],
    filters: [{ name: 'Programmes', extensions: ['exe'] }]
  });
  if (result.canceled || !result.filePaths.length) return { ok: false };
  return { ok: true, path: result.filePaths[0] };
});

// Recherche l'exécutable CareConnect dans les emplacements habituels sur C:\.
ipcMain.handle('careconnect:detect', async () => {
  const bases = [
    process.env['ProgramFiles'],
    process.env['ProgramFiles(x86)'],
    process.env['LOCALAPPDATA'],
    process.env['ProgramData'],
    'C:\\'
  ].filter(Boolean);

  const isTarget = (name) => /careconnect.*\.exe$/i.test(name);
  const found = [];

  const scan = (dir, depth) => {
    if (depth < 0 || found.length) return;
    let entries;
    try {
      entries = fs.readdirSync(dir, { withFileTypes: true });
    } catch (_) {
      return;
    }
    for (const e of entries) {
      if (found.length) return;
      const full = path.join(dir, e.name);
      if (e.isFile() && isTarget(e.name)) { found.push(full); return; }
      // On ne descend que dans les dossiers plausibles (perf).
      if (e.isDirectory() && (depth > 0) && /care|corilus|health/i.test(e.name)) {
        scan(full, depth - 1);
      }
    }
  };

  for (const base of bases) {
    // Au premier niveau on parcourt tous les dossiers, puis on filtre.
    let top;
    try { top = fs.readdirSync(base, { withFileTypes: true }); } catch (_) { continue; }
    for (const e of top) {
      if (found.length) break;
      if (e.isDirectory() && /care|corilus|health/i.test(e.name)) {
        scan(path.join(base, e.name), 2);
      }
    }
    if (found.length) break;
  }

  return found.length ? { ok: true, path: found[0] } : { ok: false };
});

// Lance un exécutable local (ex. CareConnect) à partir du chemin configuré.
// On démarre le programme DIRECTEMENT (répertoire de travail = dossier de
// l'exe), ce qui l'ouvre de façon fiable même s'il charge des DLL locales.
ipcMain.handle('app:launch', async (_e, key) => {
  const settings = loadSettings();
  const map = settings.launchers || {};
  const target = map[key];
  if (!target) {
    return { ok: false, error: `Aucun chemin configuré pour « ${key} ». Cliquez sur « Choisir le fichier .exe… ».` };
  }
  if (!fs.existsSync(target)) {
    return { ok: false, error: `Fichier introuvable : ${target}. Re-sélectionnez le .exe.` };
  }
  try {
    const child = spawn(target, [], {
      detached: true,
      stdio: 'ignore',
      cwd: path.dirname(target)
    });
    child.on('error', () => { /* remonté via le fallback si nécessaire */ });
    child.unref();
    return { ok: true };
  } catch (err) {
    // Repli : ouverture via le shell Windows.
    const result = await shell.openPath(target);
    if (result) return { ok: false, error: result };
    return { ok: true };
  }
});

ipcMain.handle('speed:runNow', async () => {
  return runSpeedTestOnce();
});

ipcMain.handle('chat:send', async (_e, payload) => {
  const settings = loadSettings();
  return chatCompletion(settings, payload);
});

// --- Coffre-fort de mots de passe (chiffré via safeStorage/DPAPI) ---
function vaultPath() {
  return path.join(app.getPath('userData'), 'vault.dat');
}
ipcMain.handle('vault:get', () => {
  try {
    if (!fs.existsSync(vaultPath())) return { ok: true, entries: [] };
    const raw = JSON.parse(fs.readFileSync(vaultPath(), 'utf8'));
    let json;
    if (raw.enc && safeStorage.isEncryptionAvailable()) {
      json = safeStorage.decryptString(Buffer.from(raw.data, 'base64'));
    } else {
      json = Buffer.from(raw.data, 'base64').toString('utf8');
    }
    return { ok: true, entries: JSON.parse(json), encrypted: Boolean(raw.enc) };
  } catch (err) {
    return { ok: false, error: err.message, entries: [] };
  }
});
ipcMain.handle('vault:save', (_e, entries) => {
  try {
    const json = JSON.stringify(entries || []);
    let out;
    if (safeStorage.isEncryptionAvailable()) {
      out = { enc: true, data: safeStorage.encryptString(json).toString('base64') };
    } else {
      out = { enc: false, data: Buffer.from(json, 'utf8').toString('base64') };
    }
    fs.mkdirSync(path.dirname(vaultPath()), { recursive: true });
    fs.writeFileSync(vaultPath(), JSON.stringify(out), 'utf8');
    return { ok: true, encrypted: out.enc };
  } catch (err) {
    return { ok: false, error: err.message };
  }
});

// Sélectionne un fichier texte (CSV…) et renvoie son contenu.
ipcMain.handle('dialog:pickTextFile', async () => {
  const result = await dialog.showOpenDialog(mainWindow, {
    title: 'Choisir un fichier',
    properties: ['openFile'],
    filters: [{ name: 'CSV / texte', extensions: ['csv', 'txt', 'tsv'] }, { name: 'Tous', extensions: ['*'] }]
  });
  if (result.canceled || !result.filePaths.length) return { ok: false };
  try {
    const content = fs.readFileSync(result.filePaths[0], 'utf8');
    return { ok: true, name: path.basename(result.filePaths[0]), content };
  } catch (err) {
    return { ok: false, error: err.message };
  }
});

// Exporte un contenu HTML en PDF (fenêtre hors-écran + printToPDF).
ipcMain.handle('export:pdf', async (_e, payload) => {
  const html = (payload && payload.html) || '';
  const suggested = (payload && payload.filename) || 'export.pdf';
  const save = await dialog.showSaveDialog(mainWindow, {
    title: 'Enregistrer le PDF',
    defaultPath: suggested,
    filters: [{ name: 'PDF', extensions: ['pdf'] }]
  });
  if (save.canceled || !save.filePath) return { ok: false };
  const win = new BrowserWindow({ show: false, webPreferences: { sandbox: true } });
  try {
    await win.loadURL('data:text/html;charset=utf-8,' + encodeURIComponent(html));
    const pdf = await win.webContents.printToPDF({ printBackground: true, landscape: true });
    fs.writeFileSync(save.filePath, pdf);
    return { ok: true, path: save.filePath };
  } catch (err) {
    return { ok: false, error: err.message };
  } finally {
    win.destroy();
  }
});

// Enregistre un contenu texte/binaire via une boîte de dialogue (ex. XLS/CSV).
ipcMain.handle('export:save', async (_e, payload) => {
  const content = (payload && payload.content) || '';
  const suggested = (payload && payload.filename) || 'export.xls';
  const save = await dialog.showSaveDialog(mainWindow, {
    title: 'Enregistrer le fichier',
    defaultPath: suggested
  });
  if (save.canceled || !save.filePath) return { ok: false };
  try {
    fs.writeFileSync(save.filePath, content, 'utf8');
    return { ok: true, path: save.filePath };
  } catch (err) {
    return { ok: false, error: err.message };
  }
});

// --- Fabrique un fichier ZIP (méthode DEFLATE) sans dépendance externe -------
const zlib = require('zlib');
const CRC_TABLE = (() => {
  const t = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = (c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1);
    t[n] = c >>> 0;
  }
  return t;
})();
function crc32(buf) {
  let c = 0xFFFFFFFF;
  for (let i = 0; i < buf.length; i++) c = CRC_TABLE[(c ^ buf[i]) & 0xFF] ^ (c >>> 8);
  return (c ^ 0xFFFFFFFF) >>> 0;
}
function buildZip(files) {
  // files: [{ name, buffer }]
  const chunks = [];
  const central = [];
  let offset = 0;
  const dosTime = 0, dosDate = 0x21; // date fixe (1980-01-01) : évite toute dépendance à l'horloge
  for (const f of files) {
    const nameBuf = Buffer.from(f.name, 'utf8');
    const data = f.buffer;
    const crc = crc32(data);
    const comp = zlib.deflateRawSync(data);
    const useComp = comp.length < data.length;
    const method = useComp ? 8 : 0;
    const body = useComp ? comp : data;

    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(20, 4);            // version needed
    local.writeUInt16LE(0x0800, 6);        // flag: UTF-8 filename
    local.writeUInt16LE(method, 8);
    local.writeUInt16LE(dosTime, 10);
    local.writeUInt16LE(dosDate, 12);
    local.writeUInt32LE(crc, 14);
    local.writeUInt32LE(body.length, 18);
    local.writeUInt32LE(data.length, 22);
    local.writeUInt16LE(nameBuf.length, 26);
    local.writeUInt16LE(0, 28);
    chunks.push(local, nameBuf, body);

    const cen = Buffer.alloc(46);
    cen.writeUInt32LE(0x02014b50, 0);
    cen.writeUInt16LE(20, 4);
    cen.writeUInt16LE(20, 6);
    cen.writeUInt16LE(0x0800, 8);
    cen.writeUInt16LE(method, 10);
    cen.writeUInt16LE(dosTime, 12);
    cen.writeUInt16LE(dosDate, 14);
    cen.writeUInt32LE(crc, 16);
    cen.writeUInt32LE(body.length, 20);
    cen.writeUInt32LE(data.length, 24);
    cen.writeUInt16LE(nameBuf.length, 28);
    cen.writeUInt32LE(offset, 42);
    central.push(Buffer.concat([cen, nameBuf]));

    offset += local.length + nameBuf.length + body.length;
  }
  const cd = Buffer.concat(central);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(files.length, 8);
  end.writeUInt16LE(files.length, 10);
  end.writeUInt32LE(cd.length, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...chunks, cd, end]);
}

// Enregistre les images d'un rapport radiologie (+ le texte du rapport) en ZIP.
ipcMain.handle('radio:saveZip', async (_e, payload) => {
  const frames = (payload && payload.frames) || [];
  const report = (payload && payload.report) || '';
  if (!frames.length) return { ok: false, reason: 'aucune image' };
  const save = await dialog.showSaveDialog(mainWindow, {
    title: 'Enregistrer le rapport radiologie',
    defaultPath: 'rapport-radiologie.zip',
    filters: [{ name: 'Archive ZIP', extensions: ['zip'] }]
  });
  if (save.canceled || !save.filePath) return { ok: false, canceled: true };
  try {
    const files = [];
    frames.forEach((fr) => {
      const m = /^data:.*?;base64,(.*)$/.exec(fr.dataUrl || '');
      if (!m) return;
      files.push({ name: fr.name || `image_${files.length + 1}.png`, buffer: Buffer.from(m[1], 'base64') });
    });
    if (report) files.push({ name: 'rapport.txt', buffer: Buffer.from(report, 'utf8') });
    if (!files.length) return { ok: false, reason: 'images illisibles' };
    fs.writeFileSync(save.filePath, buildZip(files));
    return { ok: true, path: save.filePath, count: files.length };
  } catch (err) {
    return { ok: false, reason: err.message };
  }
});

// Importe des documents (copie dans le profil) à joindre à un modèle.
ipcMain.handle('attach:add', async () => {
  const result = await dialog.showOpenDialog(mainWindow, {
    title: 'Choisir des documents',
    properties: ['openFile', 'multiSelections'],
    filters: [
      { name: 'Documents', extensions: ['pdf', 'doc', 'docx', 'odt', 'rtf', 'txt', 'md', 'csv', 'xls', 'xlsx', 'ppt', 'pptx', 'jpg', 'jpeg', 'png', 'gif', 'webp', 'heic'] },
      { name: 'Tous les fichiers', extensions: ['*'] }
    ]
  });
  if (result.canceled || !result.filePaths.length) return { ok: false };
  const dir = path.join(app.getPath('userData'), 'attachments');
  fs.mkdirSync(dir, { recursive: true });
  const stored = [];
  for (const src of result.filePaths) {
    const base = path.basename(src);
    const dest = path.join(dir, `${Date.now()}-${Math.round(Math.random() * 1e6)}-${base}`);
    try {
      fs.copyFileSync(src, dest);
      stored.push({ name: base, path: dest, ext: path.extname(base).toLowerCase() });
    } catch (_) { /* ignore ce fichier */ }
  }
  return { ok: stored.length > 0, files: stored };
});

// Extrait le texte d'un document (formats texte uniquement).
ipcMain.handle('attach:extractText', async (_e, filePath) => {
  const ext = path.extname(filePath || '').toLowerCase();
  const TEXT = ['.txt', '.md', '.csv', '.log', '.json', '.xml', '.html', '.htm', '.rtf'];
  if (!TEXT.includes(ext)) {
    return { ok: false, reason: 'Aperçu texte indisponible pour ce format — le document reste joint et ouvrable.' };
  }
  try {
    let txt = fs.readFileSync(filePath, 'utf8');
    if (ext === '.rtf') txt = txt.replace(/\\[a-z]+-?\d* ?/gi, '').replace(/[{}]/g, '');
    if (ext === '.html' || ext === '.htm') txt = txt.replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ');
    return { ok: true, text: txt.slice(0, 100000) };
  } catch (err) {
    return { ok: false, reason: 'Lecture impossible : ' + err.message };
  }
});

// Recherche de médicaments via l'API JSON publique du CBIP.
ipcMain.handle('cbip:search', async (_e, query) => {
  const q = String(query || '').trim();
  if (q.length < 2) return { ok: false, error: 'Terme trop court.' };
  const strip = (s) => String(s || '')
    .replace(/<[^>]*>/g, '')
    .replace(/&nbsp;/g, ' ').replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<').replace(/&gt;/g, '>')
    .replace(/\s+/g, ' ').trim();

  try {
    const url = `https://www.cbip.be/fr/search.json?query=${encodeURIComponent(q)}&page=1`;
    const res = await fetch(url, { headers: { Accept: 'application/json' }, cache: 'no-store' });
    if (!res.ok) return { ok: false, error: `Erreur CBIP (${res.status}).` };
    const data = await res.json();

    const groups = [];
    const walk = (node, bucket) => {
      (node || []).forEach((n) => {
        if (n.children && n.children.length) {
          const g = { header: strip(n.title), items: [] };
          walk(n.children, g.items);
          if (g.items.length) groups.push(g);
        } else {
          bucket.push({
            name: strip(n.title),
            chapter: strip(n.chapter),
            term: n.term || strip(n.title)
          });
        }
      });
    };
    const loose = [];
    walk(Array.isArray(data) ? data : [], loose);
    if (loose.length) groups.unshift({ header: 'Résultats', items: loose });
    return { ok: true, groups };
  } catch (err) {
    return { ok: false, error: 'Impossible de contacter le CBIP : ' + err.message };
  }
});

// Google Agenda : connexion OAuth et ajout d'événements.
ipcMain.handle('google:status', () => googleCal.status(loadSettings()));
ipcMain.handle('google:connect', async () => {
  const res = await googleCal.connect(loadSettings(), saveSettings);
  return res;
});
ipcMain.handle('google:addEvent', async (_e, ev) => googleCal.addEvent(loadSettings(), ev));
ipcMain.handle('google:listCalendars', async () => googleCal.listCalendars(loadSettings()));

// Météo temps réel (Open-Meteo, sans clé) pour le bandeau défilant.
ipcMain.handle('weather:get', async () => {
  const s = loadSettings();
  const w = s.weather || {};
  const lat = w.latitude != null ? w.latitude : 50.8503;
  const lon = w.longitude != null ? w.longitude : 4.3517;
  const url =
    `https://api.open-meteo.com/v1/forecast?latitude=${lat}&longitude=${lon}` +
    `&current=temperature_2m`;
  try {
    const res = await fetch(url, { cache: 'no-store' });
    if (!res.ok) return { ok: false };
    const data = await res.json();
    const temp = data && data.current ? data.current.temperature_2m : null;
    return { ok: temp != null, temp, label: w.label || '' };
  } catch (_) {
    return { ok: false };
  }
});
