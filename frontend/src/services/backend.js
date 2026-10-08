export async function getBackendHealth(bridge = globalThis.window?.stockSync) {
  if (!bridge || typeof bridge.getBackendHealth !== 'function') {
    throw new Error(
      'Desktop connection is unavailable. Launch StockSync AI through Electron.',
    )
  }

  const result = await bridge.getBackendHealth()
  if (!result?.ok) {
    throw new Error(result?.error || 'The local backend did not respond.')
  }

  return result.data
}
