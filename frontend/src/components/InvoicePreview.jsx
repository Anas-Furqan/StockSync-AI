import { getInvoicePreviewUrl } from '../services/invoices.js'
import useInvoiceExtraction from '../hooks/useInvoiceExtraction.js'
import InvoiceExtractionPanel from './InvoiceExtractionPanel.jsx'

export default function InvoicePreview({ invoice, onClose, onSaveCopy }) {
  const extractionState = useInvoiceExtraction(invoice?.id)

  if (!invoice) {
    return null
  }

  const previewUrl = getInvoicePreviewUrl(invoice.id)
  const isPdf = invoice.mimeType === 'application/pdf'
  const isImage = invoice.mimeType?.startsWith('image/')

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/75 p-5 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
      aria-label={`Preview ${invoice.originalFileName}`}
    >
      <div className="flex h-[90vh] w-full max-w-7xl flex-col overflow-hidden rounded-3xl border border-white/10 bg-panel shadow-2xl">
        <header className="flex items-center justify-between gap-4 border-b border-white/10 px-6 py-4">
          <div className="min-w-0">
            <p className="truncate font-semibold">{invoice.originalFileName}</p>
            <p className="mt-1 text-xs text-slate-500">Stored original document</p>
          </div>
          <div className="flex gap-2">
            <button
              className="rounded-lg border border-white/15 px-3 py-2 text-sm hover:border-mint/60 hover:text-mint"
              type="button"
              onClick={() => onSaveCopy(invoice.id)}
            >
              Save copy
            </button>
            <button
              className="rounded-lg bg-white/10 px-3 py-2 text-sm hover:bg-white/15"
              type="button"
              onClick={onClose}
            >
              Close
            </button>
          </div>
        </header>
        <div className="grid min-h-0 flex-1 lg:grid-cols-[1.1fr_0.9fr]">
          <div className="min-h-[360px] bg-black/30 p-4 lg:min-h-0">
            {isPdf && (
              <iframe
                className="h-full w-full rounded-xl bg-white"
                src={previewUrl}
                title={invoice.originalFileName}
              />
            )}
            {isImage && (
              <div className="flex h-full items-center justify-center overflow-auto rounded-xl bg-black/30">
                <img
                  className="max-h-full max-w-full object-contain"
                  src={previewUrl}
                  alt={`Preview of ${invoice.originalFileName}`}
                />
              </div>
            )}
            {!isPdf && !isImage && (
              <div className="flex h-full items-center justify-center text-center text-slate-400">
                <div>
                  <p>Inline preview is unavailable for this file.</p>
                  <button
                    className="mt-4 rounded-lg bg-mint px-4 py-2 font-semibold text-ink"
                    type="button"
                    onClick={() => onSaveCopy(invoice.id)}
                  >
                    Save a copy
                  </button>
                </div>
              </div>
            )}
          </div>
          <aside className="overflow-y-auto border-t border-white/10 p-5 lg:border-l lg:border-t-0">
            <InvoiceExtractionPanel state={extractionState} />
          </aside>
        </div>
      </div>
    </div>
  )
}
