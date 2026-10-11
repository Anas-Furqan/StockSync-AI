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

export function getInvoiceExtraction(id, bridge = globalThis.window?.stockSync) {
  const desktop = requireBridge(bridge)
  return unwrap(
    desktop.getInvoiceExtraction(id),
    'Unable to load invoice extraction data.',
  )
}

export function extractInvoice(id, bridge = globalThis.window?.stockSync) {
  const desktop = requireBridge(bridge)
  return unwrap(
    desktop.extractInvoice(id),
    'Invoice extraction could not be completed.',
  )
}

export function getInvoiceReview(id, bridge = globalThis.window?.stockSync) {
  return unwrap(requireBridge(bridge).getInvoiceReview(id), 'Unable to load invoice review.')
}
export function matchInvoice(id, resetManualDecisions = false, bridge = globalThis.window?.stockSync) {
  return unwrap(requireBridge(bridge).matchInvoice(id, resetManualDecisions), 'Product matching could not be completed.')
}
export function saveInvoiceReview(id, lines, selectedVendorName, bridge = globalThis.window?.stockSync) {
  return unwrap(requireBridge(bridge).saveInvoiceReview(id, lines, selectedVendorName), 'Invoice review could not be saved.')
}
export function getPostingStatus(id, bridge = globalThis.window?.stockSync) { return unwrap(requireBridge(bridge).getPostingStatus(id), 'Unable to check posting readiness.') }
export function preparePosting(id, bridge = globalThis.window?.stockSync) { return unwrap(requireBridge(bridge).preparePosting(id), 'Unable to prepare POS posting.') }
export function postInvoice(id, postingId, bridge = globalThis.window?.stockSync) { return unwrap(requireBridge(bridge).postInvoice(id, postingId), 'POS posting could not be completed.') }

export function getInvoicePreviewUrl(id, bridge = globalThis.window?.stockSync) {
  const desktop = requireBridge(bridge)
  if (typeof desktop.getInvoicePreviewUrl !== 'function') {
    throw new Error('Invoice preview is unavailable.')
  }
  return desktop.getInvoicePreviewUrl(id)
}
