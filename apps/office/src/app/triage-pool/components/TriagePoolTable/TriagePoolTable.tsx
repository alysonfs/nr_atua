import { getCoreRowModel, useReactTable } from '@tanstack/react-table'
import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { useTriagePoolList } from '../../hooks/useTriagePoolList'
import { createTriagePoolColumns } from './columns'
import { DataTable } from '../../../../shared/components/ui/Table'
import type { TriagemPoolStatus } from '../../../../shared/types/triagePool'

interface TriagePoolTableProps {
  /** Tenant ativo do usuário (ver useMyTenants().defaultTenantId). */
  tenantId: string | null
  /** Filtro opcional por um único estado do pool (C1, critério de aceite 4). */
  statusFilter?: TriagemPoolStatus
}

/**
 * C1 — tabela do pool de triagem: OS, consumidor, produto/modelo, estado,
 * checklist "x/total", tempo no pool (relativo) e indicador de nova
 * atividade. Ordenação padrão da API é pela entrada mais antiga no pool.
 * Atualização por polling a cada 30 s (ver useTriagePoolList). O contador da
 * navegação é independente (useTriagePoolCount), com seu próprio polling.
 */
export function TriagePoolTable({ tenantId, statusFilter }: TriagePoolTableProps) {
  const { i18n, t } = useTranslation()
  const { items, totalCount, isLoading, isError } = useTriagePoolList(tenantId, statusFilter)

  const locale = i18n.resolvedLanguage ?? 'pt-BR'
  const columns = useMemo(() => createTriagePoolColumns(t, locale, Date.now()), [t, locale])

  const table = useReactTable({
    data: items,
    columns,
    getCoreRowModel: getCoreRowModel(),
  })

  return (
    <section aria-labelledby="triage-pool-heading" className="space-y-3">
      <h2 id="triage-pool-heading" className="text-sm font-semibold text-slate-700">
        {t('triagePool.listTitle', { count: totalCount })}
      </h2>

      {isLoading && (
        <p className="rounded-md border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">
          {t('triagePool.loading')}
        </p>
      )}

      {!isLoading && isError && (
        <p role="alert" className="rounded-md border-l-4 border-red-500 bg-white p-4 text-sm text-red-800 shadow-sm">
          {t('triagePool.loadError')}
        </p>
      )}

      {!isLoading && !isError && <DataTable table={table} emptyMessage={t('triagePool.empty')} />}
    </section>
  )
}
