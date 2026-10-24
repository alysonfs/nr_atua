import { getCoreRowModel, useReactTable } from '@tanstack/react-table'
import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { useTriagePoolList } from '../../hooks/useTriagePoolList'
import { createTriagePoolColumns } from './columns'
import { DataTable } from '../../../../shared/components/ui/Table'
import type { TriagemListScope, TriagemListStatus } from '../../../../shared/types/triagePool'

interface TriagePoolTableProps {
  /** Tenant ativo do usuário (ver useMyTenants().defaultTenantId). */
  tenantId: string | null
  /** Filtro opcional por um único estado do escopo (C1, critério de aceite 4). */
  statusFilter?: TriagemListStatus
  /** `pool` (padrão) ou `in-progress` (triagens pré-pool, somente leitura). */
  scope?: TriagemListScope
}

/**
 * C1 — tabela do pool de triagem (ou das triagens em andamento, pré-pool,
 * quando `scope = 'in-progress'`): OS, consumidor, produto/modelo, estado,
 * checklist "x/total", tempo no pool (relativo) e indicador de nova
 * atividade. Ordenação padrão da API é pela entrada mais antiga no pool.
 * Atualização por polling a cada 30 s (ver useTriagePoolList). O contador da
 * navegação é independente (useTriagePoolCount), com seu próprio polling.
 */
export function TriagePoolTable({ tenantId, statusFilter, scope = 'pool' }: TriagePoolTableProps) {
  const { i18n, t } = useTranslation()
  const { items, totalCount, isLoading, isError } = useTriagePoolList(tenantId, statusFilter, scope)

  const locale = i18n.resolvedLanguage ?? 'pt-BR'
  // `items` muda a cada ciclo de polling (ver useTriagePoolList): usá-lo como
  // dependência recalcula `Date.now()` e mantém "tempo no pool" e "tempo de
  // triagem" atualizados sem exigir um intervalo próprio. O valor em si não
  // é lido no corpo do memo — serve apenas de gatilho de recálculo.
  /* eslint-disable react-hooks/exhaustive-deps */
  const columns = useMemo(
    () => createTriagePoolColumns(t, locale, Date.now(), scope),
    [t, locale, scope, items],
  )
  /* eslint-enable react-hooks/exhaustive-deps */
  const textKey = scope === 'in-progress' ? 'triagePool.inProgress' : 'triagePool'

  const table = useReactTable({
    data: items,
    columns,
    getCoreRowModel: getCoreRowModel(),
  })

  return (
    <section aria-labelledby="triage-pool-heading" className="space-y-3">
      <h2 id="triage-pool-heading" className="text-sm font-semibold text-slate-700">
        {t(`${textKey}.listTitle`, { count: totalCount })}
      </h2>

      {isLoading && (
        <p className="rounded-md border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">
          {t(`${textKey}.loading`)}
        </p>
      )}

      {!isLoading && isError && (
        <p role="alert" className="rounded-md border-l-4 border-red-500 bg-white p-4 text-sm text-red-800 shadow-sm">
          {t(`${textKey}.loadError`)}
        </p>
      )}

      {!isLoading && !isError && <DataTable table={table} emptyMessage={t(`${textKey}.empty`)} />}
    </section>
  )
}
