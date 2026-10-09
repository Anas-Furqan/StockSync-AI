import { useState } from 'react'
import useInvoices from '../hooks/useInvoices.js'
import InvoicePreview from './InvoicePreview.jsx'

function formatFileSize(bytes) {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

function UploadPanel({ state }) {
  return (
    <section className="rounded-3xl border border-white/10 bg-panel/70 p-6">
      <div className="flex items-start justify-between gap-4">
        <div>
          <p className="text-sm font-semibold text-mint">Add invoice</p>
          <h2 className="mt-1 text-2xl font-semibold">Store an original document</h2>
        </div>
        <span className="rounded-full bg-white/5 px-3 py-1 text-xs text-slate-400">20 MB max</span>
      </div>

      <button
        className="mt-6 flex w-full flex-col items-center rounded-2xl border border-dashed border-white/20 bg-black/10 px-6 py-8 text-center transition hover:border-mint/60 hover:bg-mint/5 focus:outline-none focus:ring-2 focus:ring-mint/60"
        type="button"
        onClick={state.chooseFile}
      >
        <span className="flex h-11 w-11 items-center justify-center rounded-full bg-mint/10 text-xl text-mint">+</span>
        <span className="mt-3 font-medium">Choose an invoice</span>
        <span className="mt-1 text-sm text-slate-500">PDF, JPG, PNG, or WebP</span>
      </button>

      {state.selectedFile && (
        <div className="mt-4 flex items-center justify-between gap-4 rounded-xl bg-white/5 p-4">
          <div className="min-w-0">
            <p className="truncate font-medium">{state.selectedFile.originalFileName}</p>
            <p className="mt-1 text-xs uppercase tracking-wide text-slate-500">
              {state.selectedFile.fileExtension.replace('.', '')} / {formatFileSize(state.selectedFile.fileSize)}
            </p>
          </div>
          <button
            className="rounded-lg bg-mint px-4 py-2 text-sm font-semibold text-ink disabled:cursor-not-allowed disabled:opacity-50"
            type="button"
            disabled={state.uploading}
            onClick={state.upload}
          >
            {state.uploading ? 'Uploading...' : 'Upload'}
          </button>
        </div>
      )}
    </section>
  )
}

export function InvoiceManagerView({ state, onPreview }) {
  const confirmDelete = (invoice) => {
    if (globalThis.confirm(`Delete ${invoice.originalFileName}? This cannot be undone.`)) {
      state.remove(invoice)
    }
  }

  return (
    <section className="rounded-3xl border border-white/10 bg-panel/70 p-6">
      <div className="flex items-center justify-between gap-4">
        <div>
          <p className="text-sm font-semibold text-mint">Invoice library</p>
          <h2 className="mt-1 text-2xl font-semibold">Uploaded documents</h2>
          <p className="mt-1 text-sm text-slate-500">{state.totalCount} stored locally</p>
        </div>
        <button
          className="rounded-lg border border-white/15 px-4 py-2 text-sm hover:border-mint/60 hover:text-mint disabled:opacity-50"
          type="button"
          disabled={state.loading}
          onClick={state.refresh}
        >
          Refresh
        </button>
      </div>

      {state.error && (
        <div className="mt-5 rounded-xl border border-rose-400/20 bg-rose-400/10 px-4 py-3 text-sm text-rose-200">
          {state.error}
        </div>
      )}
      {state.notice && (
        <div className="mt-5 rounded-xl border border-mint/20 bg-mint/10 px-4 py-3 text-sm text-mint">
          {state.notice}
        </div>
      )}

      {state.loading ? (
        <div className="mt-6 rounded-2xl border border-white/10 px-5 py-10 text-center text-slate-400">
          Loading invoices...
        </div>
      ) : state.invoices.length === 0 ? (
        <div className="mt-6 rounded-2xl border border-dashed border-white/15 px-5 py-12 text-center">
          <p className="font-medium">No invoices yet</p>
          <p className="mt-2 text-sm text-slate-500">Choose a supplier document above to store it locally.</p>
        </div>
      ) : (
        <div className="mt-6 overflow-hidden rounded-2xl border border-white/10">
          {state.invoices.map((invoice) => (
            <article
              className="grid gap-4 border-b border-white/10 px-5 py-4 last:border-b-0 md:grid-cols-[minmax(0,1fr)_auto] md:items-center"
              key={invoice.id}
            >
              <div className="min-w-0">
                <p className="truncate font-medium">{invoice.originalFileName}</p>
                <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-slate-500">
                  <span>{invoice.fileExtension.replace('.', '').toUpperCase()}</span>
                  <span>{formatFileSize(invoice.fileSize)}</span>
                  <span>{new Date(invoice.uploadedUtc).toLocaleString()}</span>
                  <span className="text-mint">{invoice.status}</span>
                </div>
              </div>
              <div className="flex gap-2">
                <button
                  className="rounded-lg border border-white/15 px-3 py-2 text-sm hover:border-mint/60 hover:text-mint"
                  type="button"
                  onClick={() => onPreview(invoice)}
                >
                  View
                </button>
                <button
                  className="rounded-lg border border-rose-400/20 px-3 py-2 text-sm text-rose-300 hover:bg-rose-400/10 disabled:opacity-50"
                  type="button"
                  disabled={state.deletingId === invoice.id}
                  onClick={() => confirmDelete(invoice)}
                >
                  {state.deletingId === invoice.id ? 'Deleting...' : 'Delete'}
                </button>
              </div>
            </article>
          ))}
        </div>
      )}
    </section>
  )
}

export default function InvoiceManager() {
  const state = useInvoices()
  const [preview, setPreview] = useState(null)

  return (
    <>
      <UploadPanel state={state} />
      <InvoiceManagerView state={state} onPreview={setPreview} />
      <InvoicePreview
        invoice={preview}
        onClose={() => setPreview(null)}
        onSaveCopy={state.saveCopy}
      />
    </>
  )
}
