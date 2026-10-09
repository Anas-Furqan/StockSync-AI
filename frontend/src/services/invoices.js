function requireBridge(bridge) {
  if (!bridge) {
    throw new Error(
      'Desktop connection is unavailable. Launch StockSync AI through Electron.',
    )
  }
  return bridge
}

async function unwrap(promise, fallback) {
  const result = await promise
  if (!result?.ok) {
    throw new Error(result?.error || fallback)
  }
  return result.data
}

export function selectInvoice(bridge = globalThis.window?.stockSync) {
  const desktop = requireBridge(bridge)
  return unwrap(desktop.selectInvoice(), 'Unable to select an invoice.')
}

export function uploadInvoice(selectionToken, bridge = globalThis.window?.stockSync) {
  const desktop = requireBridge(bridge)
  return unwrap(
    desktop.uploadInvoice(selectionToken),
    'Unable to upload the invoice.',
  )
}

export function listInvoices(bridge = globalThis.window?.stockSync) {
  const desktop = requireBridge(bridge)
  return unwrap(desktop.listInvoices(1, 50), 'Unable to load invoices.')
}

export function deleteInvoice(id, bridge = globalThis.window?.stockSync) {
  const desktop = requireBridge(bridge)
  return unwrap(desktop.deleteInvoice(id), 'Unable to delete the invoice.')
}

export function saveInvoiceCopy(id, bridge = globalThis.window?.stockSync) {
  const desktop = requireBridge(bridge)
  return unwrap(desktop.saveInvoiceCopy(id), 'Unable to save the invoice copy.')
}

export function getInvoicePreviewUrl(id, bridge = globalThis.window?.stockSync) {
  const desktop = requireBridge(bridge)
  if (typeof desktop.getInvoicePreviewUrl !== 'function') {
    throw new Error('Invoice preview is unavailable.')
  }
  return desktop.getInvoicePreviewUrl(id)
}
