'use strict';

const { app, BrowserWindow, ipcMain, shell, session, dialog } = require('electron');
const path = require('path');
const fs = require('fs');
const { loadSettings, saveSettings } = require('./settings');
const { searchFiles } = require('./fileSearch');
const { startSpeedTest, runSpeedTestOnce } = require('./speedtest');
const { chatCompletion } = require('./chat');

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

// Ouvre les liens « target=_blank » dans le navigateur système plutôt que dans
// une fenêtre Electron nue.
app.on('web-contents-created', (_event, contents) => {
  contents.setWindowOpenHandler(({ url }) => {
    if (url.startsWith('http://') || url.startsWith('https://')) {
      shell.openExternal(url);
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
ipcMain.handle('app:launch', async (_e, key) => {
  const settings = loadSettings();
  const map = settings.launchers || {};
  const target = map[key];
  if (!target) {
    return { ok: false, error: `Aucun chemin configuré pour « ${key} ». Renseignez-le dans Réglages.` };
  }
  const result = await shell.openPath(target);
  // openPath renvoie une chaîne vide en cas de succès, un message d'erreur sinon.
  if (result) return { ok: false, error: result };
  return { ok: true };
});

ipcMain.handle('speed:runNow', async () => {
  return runSpeedTestOnce();
});

ipcMain.handle('chat:send', async (_e, payload) => {
  const settings = loadSettings();
  return chatCompletion(settings, payload);
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
