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
  pickExe: () => ipcRenderer.invoke('dialog:pickExe'),
  detectCareconnect: () => ipcRenderer.invoke('careconnect:detect'),

  runSpeedTest: () => ipcRenderer.invoke('speed:runNow'),
  onSpeed: (cb) => {
    const listener = (_e, data) => cb(data);
    ipcRenderer.on('speed:update', listener);
    return () => ipcRenderer.removeListener('speed:update', listener);
  },

  chat: (messages) => ipcRenderer.invoke('chat:send', { messages }),

  getWeather: () => ipcRenderer.invoke('weather:get'),

  cbipSearch: (query) => ipcRenderer.invoke('cbip:search', query),

  addAttachments: () => ipcRenderer.invoke('attach:add'),
  extractText: (filePath) => ipcRenderer.invoke('attach:extractText', filePath),

  exportPdf: (html, filename) => ipcRenderer.invoke('export:pdf', { html, filename }),
  exportSave: (content, filename) => ipcRenderer.invoke('export:save', { content, filename }),
  saveRadioZip: (payload) => ipcRenderer.invoke('radio:saveZip', payload),
  pickTextFile: () => ipcRenderer.invoke('dialog:pickTextFile'),

  vaultGet: () => ipcRenderer.invoke('vault:get'),
  vaultSave: (entries) => ipcRenderer.invoke('vault:save', entries),

  googleStatus: () => ipcRenderer.invoke('google:status'),
  googleConnect: () => ipcRenderer.invoke('google:connect'),
  googleAddEvent: (ev) => ipcRenderer.invoke('google:addEvent', ev),
  googleListCalendars: () => ipcRenderer.invoke('google:listCalendars')
});
