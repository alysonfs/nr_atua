import { useMemo, useState } from 'react'
import { TimezonePreference } from '../components/Settings'
import { TrialCard, TrialExpiryAlert } from '../components/Trial'
import { CreateTenantForm, TenantSelector } from '../components/Onboarding'
import { ProviderIntegrationSection } from '../components/Integrations'
import { useMyTenants } from '../hooks/useTenants'
import { useTranslation } from 'react-i18next'

/**
 * Página de Configurações: configuração da empresa (tenant), credenciais do
 * iService e preferências da conta. Acessada pelo ícone de Configurações
 * do header, na rota /settings (RF-005/RF-006/RF-007).
 */
export function SettingsPage() {
  const { t } = useTranslation()
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
    <div className="mx-auto grid w-full max-w-7xl gap-6 px-4 py-6 sm:px-6 lg:grid-cols-[240px_minmax(0,1fr)] lg:px-8">
      <aside className="hidden lg:block">
        <nav aria-label={t('layout.mainNavigation')} className="sticky top-[5.5rem] space-y-1">
          {[
            t('settings.navigation.overview'),
            t('settings.navigation.integrations'),
            t('settings.navigation.collector'),
            t('settings.navigation.preferences'),
          ].map((item, index) => (
            <a
              key={item}
              href={index === 0 ? '#trial-heading' : index === 1 ? '#integration-heading' : index === 2 ? '#integration-heading' : '#preferences-heading'}
              className={`block rounded-md px-3 py-2 text-sm font-medium transition ${
                index === 0
                  ? 'bg-slate-900 text-white shadow-sm'
                  : 'text-slate-600 hover:bg-white hover:text-slate-950'
              }`}
            >
              {item}
            </a>
          ))}
        </nav>
      </aside>

      <main className="min-w-0 space-y-5">
        <div className="flex flex-wrap items-end justify-between gap-4 border-b border-slate-200 pb-5">
          <div>
            <p className="text-xs font-semibold uppercase text-sky-700">{t('settings.eyebrow')}</p>
            <h1 className="mt-1 text-2xl font-semibold text-slate-950 sm:text-3xl">{t('settings.title')}</h1>
            <p className="mt-1 max-w-2xl text-sm text-slate-600">
              {t('settings.subtitle')}
            </p>
          </div>
        </div>

        <TrialExpiryAlert showClose={false} />

        <div className="grid gap-4 sm:grid-cols-3">
          <div className="rounded-md border border-slate-200 bg-white p-4 shadow-sm">
            <p className="text-xs font-semibold uppercase text-slate-500">{t('settings.account')}</p>
            <p className="mt-2 text-lg font-semibold text-slate-950">{t('settings.activeTrial')}</p>
            <p className="text-sm text-slate-600">{t('settings.trialWindow')}</p>
          </div>
          <div className="rounded-md border border-slate-200 bg-white p-4 shadow-sm">
            <p className="text-xs font-semibold uppercase text-slate-500">{t('settings.integration')}</p>
            <p className="mt-2 text-lg font-semibold text-slate-950">iService</p>
            <p className="text-sm text-slate-600">{t('settings.credentialsValidation')}</p>
          </div>
          <div className="rounded-md border border-slate-200 bg-white p-4 shadow-sm">
            <p className="text-xs font-semibold uppercase text-slate-500">{t('settings.collector')}</p>
            <p className="mt-2 text-lg font-semibold text-slate-950">{t('settings.ready')}</p>
            <p className="text-sm text-slate-600">{t('settings.awaitingCommands')}</p>
          </div>
        </div>

        <section aria-labelledby="trial-heading" className="space-y-3">
          <div className="flex items-center justify-between gap-3">
            <div>
              <h2 id="trial-heading" className="text-sm font-semibold uppercase text-slate-500">
                {t('settings.yourTrial')}
              </h2>
              <p className="text-sm text-slate-700">{t('settings.trialDescription')}</p>
            </div>
          </div>
          <TrialCard />
        </section>

        <section aria-labelledby="integration-heading" className="space-y-3">
          <div>
            <h2 id="integration-heading" className="text-sm font-semibold uppercase text-slate-500">
              {t('settings.providerIntegrationTitle')}
            </h2>
            <p className="text-sm text-slate-700">{t('settings.providerIntegrationDescription')}</p>
          </div>

          {isLoading && <p className="rounded-md border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">{t('settings.loadingCompanies')}</p>}

          {!isLoading && isError && (
            <div role="alert" className="rounded-md border-l-4 border-red-500 bg-white p-4 shadow-sm">
              <p className="text-sm text-red-800">
                {t('settings.companiesError')}
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
            <ProviderIntegrationSection tenantId={activeTenantId} integrationId={integrationId} />
          )}

          {!isLoading &&
            !isError &&
            !hasNoTenant &&
            activeTenantId &&
            !integrationId &&
            !requiresSelection && (
              <p className="rounded-md border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">
                {t('settings.preparingIntegration')}
              </p>
            )}
        </section>

        <section aria-labelledby="preferences-heading" className="space-y-3">
          <div>
            <h2 id="preferences-heading" className="text-sm font-semibold uppercase text-slate-500">
              {t('settings.preferences')}
            </h2>
            <p className="text-sm text-slate-700">{t('settings.preferencesDescription')}</p>
          </div>
          <TimezonePreference />
        </section>
      </main>
    </div>
  )
}
