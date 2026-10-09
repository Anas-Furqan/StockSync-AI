function PosStatus({ pos }) {
  const status = pos.error ? 'connectionFailed' : pos.status?.status
  const label = pos.loading
    ? 'Checking...'
    : status === 'connected'
      ? 'Connected'
      : status === 'notConfigured'
        ? 'Not configured'
        : 'Connection failed'
  const color =
    status === 'connected'
      ? 'text-mint'
      : status === 'notConfigured' || pos.loading
        ? 'text-amber-300'
        : 'text-rose-300'

  return (
    <div className="border-t border-white/10 pt-4">
      <div className="flex items-center justify-between gap-4">
        <dt className="text-slate-400">POS database</dt>
        <dd className={color}>{label}</dd>
      </div>
      {!pos.loading && (
        <p className="mt-2 text-xs leading-5 text-slate-500">
          {pos.error || pos.status?.message}
        </p>
      )}
      {!pos.loading && status !== 'connected' && (
        <button
          className="mt-3 rounded-lg border border-white/15 px-3 py-1.5 text-xs font-medium transition hover:border-mint/60 hover:text-mint focus:outline-none focus:ring-2 focus:ring-mint/60"
          type="button"
          onClick={pos.check}
        >
          Check again
        </button>
      )}
    </div>
  )
}

function StatusCard({ health, error, loading, onRetry, pos }) {
  const connected = !loading && !error && health?.status === 'ready'

  return (
    <aside className="rounded-3xl border border-white/10 bg-panel/80 p-7 shadow-glow backdrop-blur">
      <div className="flex items-center justify-between gap-4">
        <h2 className="text-sm font-medium uppercase tracking-widest text-slate-400">
          Local services
        </h2>
        <span
          className={`h-2.5 w-2.5 rounded-full ${
            connected
              ? 'bg-mint shadow-[0_0_16px_rgba(94,233,181,0.8)]'
              : loading
                ? 'animate-pulse bg-amber-300'
                : 'bg-rose-400'
          }`}
          aria-hidden="true"
        />
      </div>

      <p className="mt-8 text-2xl font-semibold">
        {loading ? 'Connecting…' : connected ? 'Connected' : 'Backend unavailable'}
      </p>

      {error ? (
        <div className="mt-3">
          <p className="text-sm leading-6 text-rose-200">{error}</p>
          <button
            className="mt-5 rounded-lg border border-white/15 px-4 py-2 text-sm font-medium transition hover:border-mint/60 hover:text-mint focus:outline-none focus:ring-2 focus:ring-mint/60"
            type="button"
            onClick={onRetry}
          >
            Try again
          </button>
        </div>
      ) : (
        <dl className="mt-6 space-y-3 text-sm">
          <div className="flex justify-between gap-4 border-t border-white/10 pt-4">
            <dt className="text-slate-400">Backend</dt>
            <dd>{health?.service ?? 'Waiting'}</dd>
          </div>
          <div className="flex justify-between gap-4 border-t border-white/10 pt-4">
            <dt className="text-slate-400">Application</dt>
            <dd>v{health?.applicationVersion ?? health?.version ?? '—'}</dd>
          </div>
          <div className="flex justify-between gap-4 border-t border-white/10 pt-4">
            <dt className="text-slate-400">Backend</dt>
            <dd>v{health?.version ?? '-'}</dd>
          </div>
          <PosStatus pos={pos} />
        </dl>
      )}
    </aside>
  )
}

export default StatusCard
