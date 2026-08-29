import { TimezonePreference } from './app/office/components/Settings'
import { TrialBadge, TrialCard, TrialExpiryAlert } from './app/office/components/Trial'

function App() {
  return (
    <main className="mx-auto min-h-screen max-w-5xl space-y-8 p-4 sm:p-8">
      <header className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold text-slate-900">ATUA Office</h1>
          <p className="text-slate-600">Visão geral da sua conta</p>
        </div>
        <TrialBadge compact />
      </header>

      <TrialExpiryAlert showClose={false} />

      <section aria-labelledby="trial-heading">
        <h2 id="trial-heading" className="mb-4 text-xl font-semibold text-slate-900">
          Seu Trial
        </h2>
        <TrialCard />
      </section>

      <section aria-labelledby="preferences-heading">
        <h2 id="preferences-heading" className="mb-4 text-xl font-semibold text-slate-900">
          Preferências
        </h2>
        <TimezonePreference />
      </section>
    </main>
  )
}

export default App
