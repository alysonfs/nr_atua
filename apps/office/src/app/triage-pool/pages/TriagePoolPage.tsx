import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router-dom'
import { useMyTenants } from '../../office/hooks/useTenants'
import { TriagePoolTable } from '../components/TriagePoolTable'
import { TriagePoolStatusFilter } from '../components/TriagePoolStatusFilter'
import { cn } from '../../../shared/lib/cn'
import { statusFiltersForScope } from '../lib/triageStatus'
import type { TriagemListScope, TriagemListStatus } from '../../../shared/types/triagePool'

const SCOPES: TriagemListScope[] = ['in-progress', 'pool']

function parseScope(value: string | null): TriagemListScope {
  return value === 'in-progress' ? 'in-progress' : 'pool'
}

function isValidStatusFilter(scope: TriagemListScope, value: string | null): value is TriagemListStatus {
  return Boolean(value && (statusFiltersForScope(scope) as string[]).includes(value))
}

/**
 * Página `/triage-pool` (C1): visão de gestão, somente leitura, das triagens
 * do tenant ativo. Duas abas — "Em triagem" (pré-pool, a LLM ainda conduz a
 * coleta) e "Pool" (decisão humana) —, com filtro por estado do escopo
 * ativo (persistidos na URL, para compartilhamento/navegação "voltar") e
 * polling de 30 s. O Office não conversa com o consumidor nem assume
 * triagens: o atendimento operacional fica fora do Office.
 */
export function TriagePoolPage() {
  const { t } = useTranslation()
  const { defaultTenantId, isLoading, isError } = useMyTenants()
  const [searchParams, setSearchParams] = useSearchParams()

  const scope = parseScope(searchParams.get('scope'))
  const statusParam = searchParams.get('status')
  const statusFilter = isValidStatusFilter(scope, statusParam) ? statusParam : undefined

  const updateParams = (nextScope: TriagemListScope, status: TriagemListStatus | undefined) => {
    const params: Record<string, string> = {}
    if (nextScope !== 'pool') params.scope = nextScope
    if (status) params.status = status
    setSearchParams(params)
  }

  const handleStatusChange = (value: TriagemListStatus | undefined) => updateParams(scope, value)
  const handleScopeChange = (value: TriagemListScope) => {
    if (value !== scope) updateParams(value, undefined)
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
          <div className="col-span-12 flex flex-wrap items-center justify-between gap-3">
            <div
              role="tablist"
              aria-label={t('triagePool.scope.label')}
              className="flex gap-2 border-b border-slate-200"
            >
              {SCOPES.map((option) => (
                <button
                  key={option}
                  type="button"
                  role="tab"
                  aria-selected={scope === option}
                  className={cn(
                    'border-b-2 px-3 py-2 text-sm font-medium transition-colors',
                    scope === option
                      ? 'border-slate-900 text-slate-950'
                      : 'border-transparent text-slate-500 hover:text-slate-700',
                  )}
                  onClick={() => handleScopeChange(option)}
                >
                  {t(`triagePool.scope.${option === 'in-progress' ? 'inProgress' : 'pool'}`)}
                </button>
              ))}
            </div>
            <TriagePoolStatusFilter value={statusFilter} onChange={handleStatusChange} scope={scope} />
          </div>
          <p className="col-span-12 text-xs text-slate-500">{t('triagePool.readOnlyNotice')}</p>
          <div className="col-span-12">
            <TriagePoolTable tenantId={defaultTenantId} statusFilter={statusFilter} scope={scope} />
          </div>
        </>
      )}
    </div>
  )
}
