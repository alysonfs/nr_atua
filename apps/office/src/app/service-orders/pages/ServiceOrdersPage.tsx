import { useCallback, useMemo } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { StatusSummary } from '../components/StatusSummary'
import { ServiceOrdersTable } from '../components/ServiceOrdersTable'
import { useMyTenants } from '../../office/hooks/useTenants'

/** Status inicial exibido na tabela quando a página é aberta sem filtro na URL. */
const DEFAULT_STATUS = 'assigned'

/**
 * Página `/service-orders`: lista de ordens de serviço, com dois modos de
 * filtro mutuamente exclusivos — por status exato (cards de resumo, nesta
 * mesma página) ou por tempo mínimo de abertura/aging (drill-down vindo dos
 * cards do Dashboard). O filtro é dirigido pela URL (`useSearchParams`) para
 * permitir deep-link a partir do Dashboard (`?minAgeDays=N`) e navegação
 * compartilhável/"voltar" do navegador.
 */
export function ServiceOrdersPage() {
  const { t } = useTranslation()
  const { defaultTenantId, isLoading, isError } = useMyTenants()
  const [searchParams, setSearchParams] = useSearchParams()

  const minAgeDaysParam = searchParams.get('minAgeDays')
  const minAgeDays = minAgeDaysParam ? Number(minAgeDaysParam) : undefined
  const isValidMinAgeDays = typeof minAgeDays === 'number' && Number.isFinite(minAgeDays) && minAgeDays > 0

  const status = isValidMinAgeDays ? undefined : (searchParams.get('status') ?? DEFAULT_STATUS)

  const handleSelectStatus = useCallback(
    (nextStatus: string) => {
      setSearchParams({ status: nextStatus })
    },
    [setSearchParams],
  )

  const filterProps = useMemo(
    () => (isValidMinAgeDays ? { minAgeDays } : { status }),
    [isValidMinAgeDays, minAgeDays, status],
  )

  return (
    <div className="grid w-full grid-cols-12 gap-4">
      <div className="col-span-12 border-b border-slate-200 pb-5">
        <h1 className="text-2xl font-semibold text-slate-950 sm:text-3xl">{t('serviceOrders.title')}</h1>
        <p className="mt-1 max-w-2xl text-sm text-slate-600">{t('serviceOrders.subtitle')}</p>
      </div>

      {isLoading && (
        <p className="col-span-12 rounded-md border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">
          {t('dashboard.loadingCompany')}
        </p>
      )}

      {!isLoading && isError && (
        <p
          role="alert"
          className="col-span-12 rounded-md border-l-4 border-red-500 bg-white p-4 text-sm text-red-800 shadow-sm"
        >
          {t('dashboard.companyError')}
        </p>
      )}

      {!isLoading && !isError && (
        <>
          <div className="col-span-12">
            <StatusSummary
              tenantId={defaultTenantId}
              selectedStatus={status ?? ''}
              onSelectStatus={handleSelectStatus}
            />
          </div>
          <div className="col-span-12">
            <ServiceOrdersTable tenantId={defaultTenantId} {...filterProps} />
          </div>
        </>
      )}
    </div>
  )
}
