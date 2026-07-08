'use strict';

const { app, BrowserWindow, ipcMain, shell, session } = require('electron');
const path = require('path');
const { loadSettings, saveSettings } = require('./settings');
const { searchFiles } = require('./fileSearch');
const { startSpeedTest, runSpeedTestOnce } = require('./speedtest');
const { chatCompletion } = require('./chat');

// Un User-Agent Chrome « standard » : certains sites (Google, etc.) refusent
// de se charger dans un moteur qu'ils considèrent obsolète ou non sécurisé.
const CHROME_UA =
  'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36';

let mainWindow = null;

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1400,
    height: 900,
    minWidth: 1000,
    minHeight: 640,
    title: 'Prive',
    backgroundColor: '#0f172a',
    icon: path.join(__dirname, '..', 'build', 'icon.ico'),
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
