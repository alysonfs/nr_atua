import { useMemo, useState } from 'react'
import { TimezonePreference } from './app/office/components/Settings'
import { TrialBadge, TrialCard, TrialExpiryAlert } from './app/office/components/Trial'
import { CreateTenantForm, TenantSelector } from './app/office/components/Onboarding'
import { IServiceIntegrationPanel } from './app/office/components/Integrations'
import { useMyTenants } from './app/office/hooks/useTenants'

function App() {
  const { tenants, defaultTenantId, hasNoTenant, requiresSelection, isLoading, isError, refetch } =
    useMyTenants()
  const [selectedTenantId, setSelectedTenantId] = useState<string | null>(null)
  const [createdTenant, setCreatedTenant] = useState<{
    tenantId: string
    integrationId: string
  } | null>(null)

  const activeTenantId = selectedTenantId ?? createdTenant?.tenantId ?? defaultTenantId

  const integrationId = useMemo(() => {
    if (createdTenant && createdTenant.tenantId === activeTenantId) {
      return createdTenant.integrationId
    }
    const activeTenant = tenants.find((tenant) => tenant.tenantId === activeTenantId)
    return activeTenant?.integrationId ?? null
  }, [activeTenantId, createdTenant, tenants])

  const handleTenantCreated = (tenantId: string, newIntegrationId: string) => {
    setCreatedTenant({ tenantId, integrationId: newIntegrationId })
    setSelectedTenantId(tenantId)
    void refetch()
  }

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

      <section aria-labelledby="integration-heading">
        <h2 id="integration-heading" className="mb-4 text-xl font-semibold text-slate-900">
          Integração com o iService
        </h2>

        {isLoading && <p className="text-sm text-slate-500">Carregando suas empresas...</p>}

        {!isLoading && isError && (
          <div role="alert" className="rounded-lg border border-red-200 bg-red-50 p-6 shadow-sm">
            <p className="text-sm text-red-800">
              Não foi possível carregar suas empresas. Tente novamente mais tarde.
            </p>
          </div>
        )}

        {!isLoading && !isError && hasNoTenant && (
          <CreateTenantForm onCreated={handleTenantCreated} />
        )}

        {!isLoading && !isError && !hasNoTenant && requiresSelection && !selectedTenantId && (
          <TenantSelector tenants={tenants} onSelect={setSelectedTenantId} />
        )}

        {!isLoading && !isError && !hasNoTenant && activeTenantId && integrationId && (
          <IServiceIntegrationPanel tenantId={activeTenantId} integrationId={integrationId} />
        )}

        {!isLoading &&
          !isError &&
          !hasNoTenant &&
          activeTenantId &&
          !integrationId &&
          !requiresSelection && (
            <p className="text-sm text-slate-500">
              Preparando a configuração da integração...
            </p>
          )}
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
