'use strict';

const { contextBridge, ipcRenderer } = require('electron');

// Surface d'API minimale et contrôlée exposée au renderer (contextIsolation).
contextBridge.exposeInMainWorld('prive', {
  getSettings: () => ipcRenderer.invoke('settings:get'),
  saveSettings: (next) => ipcRenderer.invoke('settings:save', next),

  searchFiles: (opts) => ipcRenderer.invoke('files:search', opts),
  openPath: (p) => ipcRenderer.invoke('shell:openPath', p),
  showItem: (p) => ipcRenderer.invoke('shell:showItem', p),
  openExternal: (url) => ipcRenderer.invoke('shell:openExternal', url),

  launchApp: (key) => ipcRenderer.invoke('app:launch', key),

  runSpeedTest: () => ipcRenderer.invoke('speed:runNow'),
  onSpeed: (cb) => {
    const listener = (_e, data) => cb(data);
    ipcRenderer.on('speed:update', listener);
    return () => ipcRenderer.removeListener('speed:update', listener);
  },

  chat: (messages) => ipcRenderer.invoke('chat:send', { messages })
});
