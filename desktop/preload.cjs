const { contextBridge, ipcRenderer } = require('electron')

contextBridge.exposeInMainWorld('stockSync', {
  getBackendHealth: () => ipcRenderer.invoke('backend:get-health'),
  getPosStatus: () => ipcRenderer.invoke('pos:get-status'),
  getAppVersion: () => ipcRenderer.invoke('app:get-version'),
})
