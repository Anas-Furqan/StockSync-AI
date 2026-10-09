import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it, vi } from 'vitest'
import { InvoiceManagerView } from './InvoiceManager.jsx'

function state(overrides = {}) {
  return {
    invoices: [],
    totalCount: 0,
    loading: false,
    deletingId: null,
    error: null,
    notice: null,
    refresh: vi.fn(),
    remove: vi.fn(),
    ...overrides,
  }
}

describe('InvoiceManagerView', () => {
  it('renders loading and empty states', () => {
    expect(renderToStaticMarkup(
      <InvoiceManagerView state={state({ loading: true })} onPreview={vi.fn()} />,
    )).toContain('Loading invoices...')
    expect(renderToStaticMarkup(
      <InvoiceManagerView state={state()} onPreview={vi.fn()} />,
    )).toContain('No invoices yet')
  })

  it('renders errors without fabricating invoice data', () => {
    const html = renderToStaticMarkup(
      <InvoiceManagerView
        state={state({ error: 'Invoice storage is unavailable.' })}
        onPreview={vi.fn()}
      />,
    )
    expect(html).toContain('Invoice storage is unavailable.')
    expect(html).toContain('No invoices yet')
  })

  it('renders stored invoice metadata and actions', () => {
    const html = renderToStaticMarkup(
      <InvoiceManagerView
        state={state({
          totalCount: 1,
          invoices: [{
            id: 'f103ad1b-742d-47b0-8935-6364763d36b6',
            originalFileName: 'supplier.pdf',
            fileExtension: '.pdf',
            fileSize: 2048,
            uploadedUtc: '2026-10-09T10:00:00Z',
            status: 'Uploaded',
          }],
        })}
        onPreview={vi.fn()}
      />,
    )
    expect(html).toContain('supplier.pdf')
    expect(html).toContain('2.0 KB')
    expect(html).toContain('Uploaded')
    expect(html).toContain('View')
    expect(html).toContain('Delete')
  })
})
