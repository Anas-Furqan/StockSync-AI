import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import InvoicePostingPanel from './InvoicePostingPanel.jsx'
const status = (state, overrides = {}) => ({ readiness: { state, canPrepare: false, canPost: false, postingId: null, vendorName: 'Vendor', existingProductCount: 1, newProductCount: 0, stockEntryCount: 1, invoiceTotal: 500, blockers: [], warnings: [], safeError: null, result: null, ...overrides }, posting: null })
describe('InvoicePostingPanel', () => {
 it('renders disabled posting blockers', () => { const html=renderToStaticMarkup(<InvoicePostingPanel invoiceId="id" reviewSaved={0} initialStatus={status('NotPosted',{blockers:['POS writes are disabled.']})} />);expect(html).toContain('POS writes are disabled.');expect(html).toContain('disabled') })
 it('renders unknown-outcome reconciliation guidance', () => { const html=renderToStaticMarkup(<InvoicePostingPanel invoiceId="id" reviewSaved={0} initialStatus={status('OutcomeUnknown')} />);expect(html).toContain('Do not retry');expect(html).toContain('Reconcile') })
 it('shows success only from authoritative result', () => { const html=renderToStaticMarkup(<InvoicePostingPanel invoiceId="id" reviewSaved={0} initialStatus={status('Posted',{result:{stockRowsCommitted:1,productIds:['42']}})} />);expect(html).toContain('POS confirmed 1 stock entries committed');expect(html).toContain('42') })
})
