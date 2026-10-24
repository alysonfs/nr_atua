import type { ColumnDef } from '@tanstack/react-table'
import type { TFunction } from 'i18next'
import { Link } from 'react-router-dom'
import { Remind } from '@icon-park/react'
import type { TriagemListScope, TriagemPoolListItem } from '../../../../shared/types/triagePool'
import { translateTriagemPoolStatus, triagemPoolStatusTone } from '../../lib/triageStatus'
import { formatRelativeTime } from '../../lib/formatRelativeTime'
import { formatTriageDurationLabel } from '../../lib/formatTriageDuration'
import { Badge } from '../../../../shared/components/ui/Badge'

/**
 * Definição das colunas da tabela do pool de triagem (C1), usada pelo
 * `@tanstack/react-table` via `useReactTable`. Colunas mínimas conforme o
 * épico: OS, consumidor, produto/modelo, estado, checklist, tempo no pool e
 * indicador de nova atividade.
 */
export function createTriagePoolColumns(
  t: TFunction,
  locale: string,
  nowMs: number,
  scope: TriagemListScope = 'pool',
): ColumnDef<TriagemPoolListItem>[] {
  return [
    {
      accessorKey: 'workOrderProviderNo',
      header: t('triagePool.columns.workOrder'),
      cell: (info) => (
        <Link
          to={`/triage-pool/${info.row.original.triagemId}`}
          className="font-medium text-sky-700 hover:underline"
        >
          {info.getValue<string | null>() ?? '—'}
        </Link>
      ),
    },
    {
      accessorKey: 'customerName',
      header: t('triagePool.columns.customer'),
      cell: (info) => info.getValue<string | null>() ?? '—',
    },
    {
      accessorKey: 'productModel',
      header: t('triagePool.columns.product'),
      cell: (info) => {
        const item = info.row.original
        const parts = [item.productBrand, item.productModel].filter(Boolean)
        return parts.length > 0 ? parts.join(' / ') : '—'
      },
    },
    {
      accessorKey: 'status',
      header: t('triagePool.columns.status'),
      cell: (info) => {
        const status = info.getValue<string>()
        return <Badge tone={triagemPoolStatusTone(status)}>{translateTriagemPoolStatus(t, status)}</Badge>
      },
    },
    {
      id: 'checklist',
      header: t('triagePool.columns.checklist'),
      cell: (info) => {
        const item = info.row.original
        return `${item.checklistItemsCompleted}/${item.checklistItemsTotal}`
      },
    },
    {
      accessorKey: 'enteredPoolAtUtc',
      // Fora do pool, a API devolve aqui o momento da última transição de estado.
      header: t(scope === 'in-progress' ? 'triagePool.columns.timeInState' : 'triagePool.columns.timeInPool'),
      cell: (info) => formatRelativeTime(info.getValue<string>(), locale, nowMs),
    },
    {
      id: 'triageDuration',
      header: t('triagePool.columns.triageDuration'),
      cell: (info) => {
        const item = info.row.original
        return formatTriageDurationLabel(t, item.startedAtUtc, item.finishedAtUtc, nowMs)
      },
    },
    {
      id: 'hasNewActivity',
      header: t('triagePool.columns.newActivity'),
      cell: (info) =>
        info.row.original.hasNewActivity ? (
          <span
            role="img"
            aria-label={t('triagePool.columns.newActivityAria')}
            title={t('triagePool.columns.newActivityAria')}
            className="inline-flex items-center text-amber-600"
          >
            <Remind theme="filled" size={16} />
          </span>
        ) : null,
    },
  ]
}
