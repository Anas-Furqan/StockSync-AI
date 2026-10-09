import { describe, expect, it, vi } from 'vitest'
import {
  deleteInvoice,
  extractInvoice,
  getInvoiceExtraction,
  getInvoicePreviewUrl,
  listInvoices,
  selectInvoice,
  uploadInvoice,
} from './invoices.js'

describe('invoice desktop service', () => {
  it('selects and uploads only by opaque selection token', async () => {
    const selection = {
      selectionToken: 'opaque-token',
      originalFileName: 'invoice.pdf',
      fileSize: 120,
    }
    const invoice = { id: 'invoice-id', originalFileName: 'invoice.pdf' }
    const bridge = {
      selectInvoice: vi.fn().mockResolvedValue({ ok: true, data: selection }),
      uploadInvoice: vi.fn().mockResolvedValue({ ok: true, data: invoice }),
    }

    await expect(selectInvoice(bridge)).resolves.toEqual(selection)
    await expect(uploadInvoice(selection.selectionToken, bridge)).resolves.toEqual(invoice)
    expect(bridge.uploadInvoice).toHaveBeenCalledWith('opaque-token')
  })

  it('lists, deletes, and constructs previews through narrow bridge methods', async () => {
    const page = { items: [], totalCount: 0 }
    const bridge = {
      listInvoices: vi.fn().mockResolvedValue({ ok: true, data: page }),
      deleteInvoice: vi.fn().mockResolvedValue({ ok: true }),
      getInvoicePreviewUrl: vi.fn().mockReturnValue('stocksync-invoice://file/id'),
    }

    await expect(listInvoices(bridge)).resolves.toEqual(page)
    await expect(deleteInvoice('id', bridge)).resolves.toBeUndefined()
    expect(getInvoicePreviewUrl('id', bridge)).toBe('stocksync-invoice://file/id')
  })

  it('surfaces safe desktop errors', async () => {
    const bridge = {
      listInvoices: vi.fn().mockResolvedValue({
        ok: false,
        error: 'Invoice storage is unavailable.',
      }),
    }

    await expect(listInvoices(bridge)).rejects.toThrow(
      'Invoice storage is unavailable.',
    )
  })

  it('loads and starts extraction through authenticated desktop methods', async () => {
    const extraction = { invoiceId: 'id', status: 'NotStarted', isConfigured: true }
    const completed = { ...extraction, status: 'Succeeded' }
    const bridge = {
      getInvoiceExtraction: vi.fn().mockResolvedValue({ ok: true, data: extraction }),
      extractInvoice: vi.fn().mockResolvedValue({ ok: true, data: completed }),
    }

    await expect(getInvoiceExtraction('id', bridge)).resolves.toEqual(extraction)
    await expect(extractInvoice('id', bridge)).resolves.toEqual(completed)
    expect(bridge.getInvoiceExtraction).toHaveBeenCalledWith('id')
    expect(bridge.extractInvoice).toHaveBeenCalledWith('id')
  })
})
