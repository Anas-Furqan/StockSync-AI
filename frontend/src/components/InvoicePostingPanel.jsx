import { useEffect, useRef, useState } from 'react'
import { getPostingStatus, postInvoice, preparePosting } from '../services/invoices.js'

export default function InvoicePostingPanel({ invoiceId, reviewSaved, initialStatus = null }) {
  const [status, setStatus] = useState(initialStatus), [busy, setBusy] = useState(null), [error, setError] = useState(null)
  const postingRequestActive = useRef(false)
  const refresh = async () => { setBusy('refresh'); setError(null); try { setStatus(await getPostingStatus(invoiceId)) } catch (reason) { setError(reason.message) } finally { setBusy(null) } }
  useEffect(() => { let active = true; getPostingStatus(invoiceId).then((value) => active && setStatus(value)).catch((reason) => active && setError(reason.message)); return () => { active = false } }, [invoiceId, reviewSaved])
  const prepare = async () => { setBusy('prepare'); setError(null); try { setStatus(await preparePosting(invoiceId)) } catch (reason) { setError(reason.message); await refresh() } finally { setBusy(null) } }
  const post = async () => { if (postingRequestActive.current || !globalThis.confirm('Post this verified invoice to the live POS? This creates real product and stock records and cannot be undone here.')) return; postingRequestActive.current = true; setBusy('post'); setError(null); try { setStatus(await postInvoice(invoiceId, status.readiness.postingId)) } catch (reason) { setError(reason.message); await refresh() } finally { postingRequestActive.current = false; setBusy(null) } }
  if (!status) return <section className="m-5 rounded-2xl border border-white/10 p-5"><p className="text-sm text-slate-400">{error || 'Checking POS posting readiness...'}</p></section>
  const r = status.readiness
  return <section className="m-5 rounded-2xl border border-white/10 bg-black/10 p-5" aria-label="POS posting">
    <div className="flex flex-wrap items-start justify-between gap-3"><div><p className="text-xs font-semibold uppercase tracking-wider text-mint">POS posting</p><h3 className="mt-1 text-lg font-semibold">{r.state}</h3><p className="mt-1 text-xs text-slate-500">Authoritative backend readiness</p></div><button type="button" disabled={busy} onClick={refresh} className="rounded-lg border border-white/15 px-3 py-2 text-sm">Refresh</button></div>
    <div className="mt-4 grid gap-2 text-sm sm:grid-cols-2 lg:grid-cols-5"><Summary label="Vendor" value={r.vendorName || '-'} /><Summary label="Existing products" value={r.existingProductCount} /><Summary label="New products" value={r.newProductCount} /><Summary label="Stock entries" value={r.stockEntryCount} /><Summary label="Invoice total" value={r.invoiceTotal} /></div>
    {r.blockers.length > 0 && <div className="mt-4 rounded-xl border border-rose-400/20 bg-rose-400/10 p-3"><p className="text-sm font-semibold text-rose-200">Posting blockers</p><ul className="mt-2 list-disc space-y-1 pl-5 text-xs text-rose-100">{r.blockers.map((item) => <li key={item}>{item}</li>)}</ul></div>}
    {r.warnings.length > 0 && <div className="mt-3 rounded-xl bg-amber-300/10 p-3"><ul className="list-disc space-y-1 pl-5 text-xs text-amber-100">{r.warnings.map((item) => <li key={item}>{item}</li>)}</ul></div>}
    {r.state === 'OutcomeUnknown' && <p className="mt-4 rounded-xl border border-rose-400/30 bg-rose-400/10 p-3 text-sm text-rose-100">Do not retry. SQL Server may have committed before local confirmation. Reconcile this invoice using verified POS identifiers and records.</p>}
    {r.safeError && <p className="mt-3 text-sm text-amber-200">{r.safeError}</p>}{error && <p className="mt-3 text-sm text-rose-200">{error}</p>}
    {r.result && <div className="mt-4 rounded-xl border border-mint/20 bg-mint/10 p-3 text-sm text-mint">POS confirmed {r.result.stockRowsCommitted} stock entries committed. New product IDs: {r.result.productIds.join(', ') || 'none'}.</div>}
    <div className="mt-4 flex flex-wrap gap-2"><button type="button" disabled={busy || !r.canPrepare} onClick={prepare} className="rounded-lg border border-mint/30 px-4 py-2 text-sm text-mint disabled:opacity-40">{busy === 'prepare' ? 'Preparing...' : r.state === 'FailedSafeToRetry' ? 'Prepare safe retry' : 'Prepare posting'}</button><button type="button" disabled={busy || !r.canPost} onClick={post} className="rounded-lg bg-rose-500 px-4 py-2 text-sm font-semibold text-white disabled:opacity-40">{busy === 'post' ? 'Posting to POS...' : 'Post to POS'}</button></div>
    <p className="mt-3 text-xs text-slate-500">Posting uses the immutable prepared snapshot. Editing and saving the review requires a fresh preparation.</p>
  </section>
}
function Summary({ label, value }) { return <div className="rounded-lg bg-white/5 p-3"><p className="text-xs text-slate-500">{label}</p><p className="mt-1 truncate font-medium">{value}</p></div> }
