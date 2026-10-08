const { contextBridge, ipcRenderer } = require('electron')

contextBridge.exposeInMainWorld('stockSync', {
  getBackendHealth: () => ipcRenderer.invoke('backend:get-health'),
  getAppVersion: () => ipcRenderer.invoke('app:get-version'),
})
