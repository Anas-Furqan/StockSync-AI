import StatusCard from './components/StatusCard.jsx'
import useBackendHealth from './hooks/useBackendHealth.js'
import usePosConnectionStatus from './hooks/usePosConnectionStatus.js'

function App() {
  const { health, error, loading, refresh } = useBackendHealth()
  const pos = usePosConnectionStatus()

  return (
    <main className="relative flex min-h-screen items-center overflow-hidden bg-ink px-6 py-12 text-slate-100">
      <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(circle_at_top_right,rgba(94,233,181,0.14),transparent_38%)]" />
      <section className="relative mx-auto grid w-full max-w-5xl gap-10 lg:grid-cols-[1.25fr_0.75fr] lg:items-center">
        <div>
          <p className="mb-4 text-sm font-semibold uppercase tracking-[0.3em] text-mint">
            Pharmacy inventory foundation
          </p>
          <h1 className="max-w-2xl text-5xl font-semibold tracking-tight sm:text-7xl">
            StockSync <span className="text-mint">AI</span>
          </h1>
          <p className="mt-6 max-w-xl text-lg leading-8 text-slate-300">
            A secure local workspace for preparing and verifying pharmacy stock
            updates before they reach the existing point-of-sale system.
          </p>
          <p className="mt-10 text-sm text-slate-500">Phase 2 - POS read integration</p>
        </div>

        <StatusCard
          health={health}
          error={error}
          loading={loading}
          onRetry={refresh}
          pos={pos}
        />
      </section>
    </main>
  )
}

export default App
