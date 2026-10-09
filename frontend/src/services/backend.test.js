import { describe, expect, it, vi } from 'vitest'
import { getBackendHealth, getPosConnectionStatus } from './backend.js'

describe('getBackendHealth', () => {
  it('returns verified health data from the desktop bridge', async () => {
    const data = { status: 'ready', service: 'StockSyncAI.Api', version: '0.1.0' }
    const bridge = { getBackendHealth: vi.fn().mockResolvedValue({ ok: true, data }) }

    await expect(getBackendHealth(bridge)).resolves.toEqual(data)
  })

  it('turns a failed response into a useful error', async () => {
    const bridge = {
      getBackendHealth: vi
        .fn()
        .mockResolvedValue({ ok: false, error: 'Backend exited unexpectedly.' }),
    }

    await expect(getBackendHealth(bridge)).rejects.toThrow(
      'Backend exited unexpectedly.',
    )
  })
})

describe('getPosConnectionStatus', () => {
  it('returns the POS status from the narrow desktop bridge', async () => {
    const data = {
      status: 'notConfigured',
      message: 'POS database connection is not configured.',
    }
    const bridge = { getPosStatus: vi.fn().mockResolvedValue({ ok: true, data }) }

    await expect(getPosConnectionStatus(bridge)).resolves.toEqual(data)
  })

  it('does not fabricate a connected state when the request fails', async () => {
    const bridge = {
      getPosStatus: vi.fn().mockResolvedValue({
        ok: false,
        error: 'POS status is unavailable.',
      }),
    }

    await expect(getPosConnectionStatus(bridge)).rejects.toThrow(
      'POS status is unavailable.',
    )
  })
})
