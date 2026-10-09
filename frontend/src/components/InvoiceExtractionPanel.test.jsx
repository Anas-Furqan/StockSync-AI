import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it, vi } from 'vitest'
import InvoiceExtractionPanel from './InvoiceExtractionPanel.jsx'

function state(extraction, overrides = {}) {
  return {
    extraction,
    loading: false,
    extracting: false,
    error: null,
    runExtraction: vi.fn(),
    ...overrides,
  }
}

describe('InvoiceExtractionPanel', () => {
  it('shows loading and missing configuration states', () => {
    expect(renderToStaticMarkup(
      <InvoiceExtractionPanel state={state(null, { loading: true })} />,
    )).toContain('Loading extraction data')
    expect(renderToStaticMarkup(
      <InvoiceExtractionPanel state={state({
        isConfigured: false,
        status: 'NotStarted',
      })} />,
    )).toContain('Gemini is not configured')
  })

  it('shows safe failure and retry states', () => {
    const html = renderToStaticMarkup(
      <InvoiceExtractionPanel state={state({
        isConfigured: true,
        status: 'Failed',
        error: 'Gemini is temporarily unavailable.',
        data: null,
      })} />,
    )
    expect(html).toContain('Gemini is temporarily unavailable.')
    expect(html).toContain('Retry extraction')
  })

  it('renders partial structured data, missing values, and uncertainty', () => {
    const text = (value, uncertainty = null) => ({ value, uncertainty })
    const html = renderToStaticMarkup(
      <InvoiceExtractionPanel state={state({
        isConfigured: true,
        status: 'Partial',
        error: null,
        data: {
          supplierName: text('Supplier Ltd'),
          invoiceNumber: text(null, 'Not legible'),
          invoiceDate: text('2026-10-09'),
          currency: text('PKR'),
          lineItems: [{
            originalDescription: text('Medicine 10mg x 20'),
            boxQuantity: text(2),
            packSize: text('20 tablets'),
            netLineAmount: text(500),
            expiryDate: text(null, 'Not printed'),
            barcode: text(null, 'Not present'),
            productCode: text('MED-1'),
          }],
          warnings: ['Verify the invoice number manually.'],
        },
      })} />,
    )
    expect(html).toContain('Supplier Ltd')
    expect(html).toContain('Medicine 10mg x 20')
    expect(html).toContain('Not identified')
    expect(html).toContain('Not legible')
    expect(html).toContain('Verify the invoice number manually.')
    expect(html).toContain('needs human verification')
  })
})
