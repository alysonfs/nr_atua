import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router-dom'
import { useMyTenants } from '../../office/hooks/useTenants'
import { TriagePoolTable } from '../components/TriagePoolTable'
import { TriagePoolStatusFilter } from '../components/TriagePoolStatusFilter'
import { TRIAGE_POOL_STATUS_FILTERS } from '../lib/triageStatus'
import type { TriagemPoolStatus } from '../../../shared/types/triagePool'

const VALID_STATUS_FILTERS = new Set<string>(TRIAGE_POOL_STATUS_FILTERS)

function isValidStatusFilter(value: string | null): value is TriagemPoolStatus {
  return Boolean(value && VALID_STATUS_FILTERS.has(value))
}

/**
 * Página `/triage-pool` (C1): lista do pool compartilhado de triagem do
 * tenant ativo, com filtro por estado (persistido na URL, para
 * compartilhamento/navegação "voltar") e polling de 30 s.
 */
export function TriagePoolPage() {
  const { t } = useTranslation()
  const { defaultTenantId, isLoading, isError } = useMyTenants()
  const [searchParams, setSearchParams] = useSearchParams()

  const statusParam = searchParams.get('status')
  const statusFilter = isValidStatusFilter(statusParam) ? statusParam : undefined

  const handleStatusChange = (value: TriagemPoolStatus | undefined) => {
    if (value) {
      setSearchParams({ status: value })
    } else {
      setSearchParams({})
    }
  }

  return (
    <div className="grid w-full grid-cols-12 gap-4">
      <div className="col-span-12 border-b border-slate-200 pb-5">
        <h1 className="text-2xl font-semibold text-slate-950 sm:text-3xl">{t('triagePool.title')}</h1>
        <p className="mt-1 max-w-2xl text-sm text-slate-600">{t('triagePool.subtitle')}</p>
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
          <div className="col-span-12 flex justify-end">
            <TriagePoolStatusFilter value={statusFilter} onChange={handleStatusChange} />
          </div>
          <div className="col-span-12">
            <TriagePoolTable tenantId={defaultTenantId} statusFilter={statusFilter} />
          </div>
        </>
      )}
    </div>
  )
}
