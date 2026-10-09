const { contextBridge, ipcRenderer } = require('electron')

contextBridge.exposeInMainWorld('stockSync', {
  getBackendHealth: () => ipcRenderer.invoke('backend:get-health'),
  getPosStatus: () => ipcRenderer.invoke('pos:get-status'),
  selectInvoice: () => ipcRenderer.invoke('invoice:select'),
  uploadInvoice: (selectionToken) =>
    ipcRenderer.invoke('invoice:upload', { selectionToken }),
  listInvoices: (page = 1, pageSize = 50) =>
    ipcRenderer.invoke('invoice:list', { page, pageSize }),
  getInvoice: (id) => ipcRenderer.invoke('invoice:get', id),
  deleteInvoice: (id) => ipcRenderer.invoke('invoice:delete', id),
  getInvoiceExtraction: (id) => ipcRenderer.invoke('invoice:get-extraction', id),
  extractInvoice: (id) => ipcRenderer.invoke('invoice:extract', id),
  getInvoicePreviewUrl: (id) => `stocksync-invoice://file/${encodeURIComponent(id)}`,
  saveInvoiceCopy: (id) => ipcRenderer.invoke('invoice:save-copy', id),
  getAppVersion: () => ipcRenderer.invoke('app:get-version'),
})
