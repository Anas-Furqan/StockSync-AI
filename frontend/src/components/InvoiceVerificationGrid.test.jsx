import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it, vi } from 'vitest'
import InvoiceVerificationGrid from './InvoiceVerificationGrid.jsx'

const base = (overrides = {}) => ({ posStatus: 'connected', posMessage: null, catalog: [],
  review: { status: 'NeedsReview', lines: [], ...overrides } })

describe('InvoiceVerificationGrid', () => {
  it('shows loading and safe initial error states', () => {
    expect(renderToStaticMarkup(<InvoiceVerificationGrid invoiceId="id" onBack={vi.fn()} />)).toContain('Loading verification grid')
    expect(renderToStaticMarkup(<InvoiceVerificationGrid invoiceId="id" onBack={vi.fn()} initialError="Run extraction first." />)).toContain('Run extraction first.')
  })
  it('shows the empty state and POS unavailable state', () => {
    expect(renderToStaticMarkup(<InvoiceVerificationGrid invoiceId="id" onBack={vi.fn()} initialData={base()} />)).toContain('No extracted line items')
    const html = renderToStaticMarkup(<InvoiceVerificationGrid invoiceId="id" onBack={vi.fn()} initialData={{ ...base(), posStatus: 'notConfigured', posMessage: 'Configure POS.' }} />)
    expect(html).toContain('POS notConfigured')
    expect(html).toContain('Configure POS.')
  })
  it('renders saved rows, editable values, decisions, and validation', () => {
    const line = { id: 'line', lineIndex: 0, originalDescription: 'Medicine 10 mg', originalBoxQuantity: '2', originalPackSize: '20', originalNetLineAmount: '500', decisionSource: 'AutomaticSuggestion', selectedProductId: '7', selectedProductName: 'Medicine 10 mg', matchScore: 98, candidates: [], boxQuantity: 2, packSize: 20, netLineAmount: 500, totalUnits: 40, perUnitCost: 12.5, expiryDate: null, validationErrors: ['Confirm an existing product.'] }
    const html = renderToStaticMarkup(<InvoiceVerificationGrid invoiceId="id" onBack={vi.fn()} initialData={base({ lines: [line] })} />)
    expect(html).toContain('Medicine 10 mg')
    expect(html).toContain('Accept suggestion')
    expect(html).toContain('NEW PRODUCT')
    expect(html).not.toContain('Unsaved changes')
    expect(html).toContain('Confirm an existing product.')
  })
})
