function Field({ label, field }) {
  const missing = field?.value === null || field?.value === undefined
  return (
    <div className="rounded-xl border border-white/10 bg-black/10 p-3">
      <p className="text-xs uppercase tracking-wide text-slate-500">{label}</p>
      <p className={`mt-1 text-sm ${missing ? 'italic text-slate-500' : 'text-slate-100'}`}>
        {missing ? 'Not identified' : String(field.value)}
      </p>
      {field?.uncertainty && (
        <p className="mt-1 text-xs leading-5 text-amber-300">{field.uncertainty}</p>
      )}
    </div>
  )
}

function DataTable({ data }) {
  return (
    <>
      <div className="grid grid-cols-2 gap-2">
        <Field label="Supplier" field={data.supplierName} />
        <Field label="Invoice number" field={data.invoiceNumber} />
        <Field label="Invoice date" field={data.invoiceDate} />
        <Field label="Currency" field={data.currency} />
      </div>

      <div className="mt-5">
        <h4 className="text-sm font-semibold">Line items</h4>
        {data.lineItems.length === 0 ? (
          <p className="mt-2 rounded-xl border border-dashed border-white/10 p-4 text-sm text-slate-500">
            No line items could be identified.
          </p>
        ) : (
          <div className="mt-2 space-y-3">
            {data.lineItems.map((item, index) => (
              <article className="rounded-xl border border-white/10 p-3" key={index}>
                <Field label={`Item ${index + 1}`} field={item.originalDescription} />
                <div className="mt-2 grid grid-cols-2 gap-2 xl:grid-cols-3">
                  <Field label="Box quantity" field={item.boxQuantity} />
                  <Field label="Pack size" field={item.packSize} />
                  <Field label="Net line amount" field={item.netLineAmount} />
                  <Field label="Expiry date" field={item.expiryDate} />
                  <Field label="Barcode" field={item.barcode} />
                  <Field label="Product code" field={item.productCode} />
                </div>
              </article>
            ))}
          </div>
        )}
      </div>

      {data.warnings.length > 0 && (
        <div className="mt-4 rounded-xl border border-amber-300/20 bg-amber-300/10 p-3">
          <p className="text-xs font-semibold uppercase tracking-wide text-amber-300">Warnings</p>
          <ul className="mt-2 space-y-1 text-sm text-amber-100">
            {data.warnings.map((warning, index) => <li key={index}>{warning}</li>)}
          </ul>
        </div>
      )}
    </>
  )
}

export default function InvoiceExtractionPanel({ state }) {
  if (state.loading) {
    return <p className="py-8 text-center text-sm text-slate-400">Loading extraction data...</p>
  }

  const extraction = state.extraction
  const configured = extraction?.isConfigured !== false
  const status = extraction?.status ?? 'NotStarted'
  const hasResult = Boolean(extraction?.data)

  return (
    <div>
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-sm font-semibold text-mint">AI extraction</p>
          <p className="mt-1 text-xs leading-5 text-slate-500">
            Gemini requires internet access. AI-extracted data needs human verification.
          </p>
        </div>
        {status !== 'NotStarted' && (
          <span className={`rounded-full px-2.5 py-1 text-xs ${
            status === 'Succeeded'
              ? 'bg-mint/10 text-mint'
              : status === 'Partial'
                ? 'bg-amber-300/10 text-amber-300'
                : status === 'Failed'
                  ? 'bg-rose-400/10 text-rose-300'
                  : 'bg-white/10 text-slate-300'
          }`}>
            {status}
          </span>
        )}
      </div>

      {!configured && (
        <div className="mt-4 rounded-xl border border-amber-300/20 bg-amber-300/10 p-4 text-sm text-amber-100">
          Gemini is not configured. Add <code>GEMINI_API_KEY</code> to the local .env file and restart StockSync AI.
        </div>
      )}

      {state.error && (
        <div className="mt-4 rounded-xl border border-rose-400/20 bg-rose-400/10 p-4 text-sm text-rose-200">
          {state.error}
        </div>
      )}

      {extraction?.error && extraction.error !== state.error && (
        <div className="mt-4 rounded-xl border border-rose-400/20 bg-rose-400/10 p-4 text-sm text-rose-200">
          {extraction.error}
        </div>
      )}

      {configured && (
        <button
          className="mt-4 w-full rounded-xl bg-mint px-4 py-3 text-sm font-semibold text-ink disabled:cursor-not-allowed disabled:opacity-50"
          type="button"
          disabled={state.extracting || status === 'Extracting'}
          onClick={state.runExtraction}
        >
          {state.extracting || status === 'Extracting'
            ? 'Extracting invoice data...'
            : status === 'Failed'
              ? 'Retry extraction'
              : hasResult
                ? 'Extract again'
                : 'Extract invoice data'}
        </button>
      )}

      {hasResult && (
        <div className="mt-5">
          {status === 'Failed' && (
            <p className="mb-3 text-xs text-slate-500">
              Showing the latest successful extraction; the most recent retry failed.
            </p>
          )}
          <DataTable data={extraction.data} />
        </div>
      )}
    </div>
  )
}
