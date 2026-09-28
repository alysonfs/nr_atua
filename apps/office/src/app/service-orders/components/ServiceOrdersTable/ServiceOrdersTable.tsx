import { getCoreRowModel, useReactTable } from '@tanstack/react-table'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useWorkOrdersByStatus } from '../../hooks/useWorkOrdersByStatus'
import { createServiceOrdersColumns } from './columns'
import { DataTable } from '../../../../shared/components/ui/Table'

const PAGE_SIZE_OPTIONS = [10, 15, 20, 25, 50] as const

/** Converte um valor kebab-case de filtro (ex.: "due-today") para camelCase (ex.: "dueToday"). */
function toCamelCase(value: string): string {
  return value.replace(/-([a-z])/g, (_, letter: string) => letter.toUpperCase())
}

interface ServiceOrdersTableProps {
  /** Tenant ativo do usuário (ver useMyTenants().defaultTenantId). */
  tenantId: string | null
  /** Status exato a filtrar (mutuamente exclusivo com os demais filtros). */
  status?: string
  /** Dias mínimos de abertura a filtrar — aging (mutuamente exclusivo com os demais). */
  minAgeDays?: number
  /** Filtro de prazo de próximo atendimento (RF-030.5) — mutuamente exclusivo com os demais. */
  nextAppointmentFilter?: string
  /** Filtro de criticidade (RF-033.4) — mutuamente exclusivo com os demais. */
  criticalityFilter?: string
}

/**
 * Seção "Ordens de serviço" da página `/service-orders`: lista paginada de
 * OS filtradas pelo status selecionado nos cards de resumo OU por um
 * drill-down vindo do Dashboard (aging, prazo de atendimento ou
 * criticidade).
 *
 * Integra com
 * GET /api/tenants/{tenantId}/work-orders?status=|minAgeDays=|nextAppointmentFilter=|criticalityFilter=&page=&pageSize=
 * (ver useWorkOrdersByStatus).
 */
export function ServiceOrdersTable({
  tenantId,
  status,
  minAgeDays,
  nextAppointmentFilter,
  criticalityFilter,
}: ServiceOrdersTableProps) {
  const { i18n, t } = useTranslation()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState<number>(PAGE_SIZE_OPTIONS[1])

  // Filtro trocado (novo card clicado ou navegação por drill-down): reinicia para a primeira página.
  useEffect(() => {
    setPage(1)
  }, [status, minAgeDays, nextAppointmentFilter, criticalityFilter])

  const { orders, totalCount, isLoading, isError } = useWorkOrdersByStatus(
    tenantId,
    { status, minAgeDays, nextAppointmentFilter, criticalityFilter },
    page,
    pageSize,
  )
  const columns = useMemo(
    () => createServiceOrdersColumns(t, i18n.resolvedLanguage ?? 'pt-BR'),
    [i18n.resolvedLanguage, t],
  )

  const table = useReactTable({
    data: orders,
    columns,
    getCoreRowModel: getCoreRowModel(),
  })

  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))

  const heading = status
    ? t('dashboard.ordersTitle', { status })
    : minAgeDays
      ? t('serviceOrders.ordersTitleByAging', { days: minAgeDays })
      : nextAppointmentFilter
        ? t('serviceOrders.ordersTitleByFilter', {
            label: t(`serviceOrders.nextAppointmentFilterLabels.${toCamelCase(nextAppointmentFilter)}`),
          })
        : t('serviceOrders.ordersTitleByFilter', {
            label: t(`serviceOrders.criticalityFilterLabels.${toCamelCase(criticalityFilter ?? '')}`),
          })

  return (
    <section aria-labelledby="service-orders-heading" className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 id="service-orders-heading" className="text-sm font-semibold text-slate-700">
          {heading}
        </h2>

        <label className="flex items-center gap-2 text-xs text-slate-600">
          {t('common.pageSizeLabel')}
          <select
            className="rounded-md border border-slate-300 px-2 py-1 text-sm"
            value={pageSize}
            onChange={(event) => {
              setPageSize(Number(event.target.value))
              setPage(1)
            }}
          >
            {PAGE_SIZE_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        </label>
      </div>

      {isLoading && (
        <p className="rounded-md border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">
          {t('dashboard.designatedLoading')}
        </p>
      )}

      {!isLoading && isError && (
        <p role="alert" className="rounded-md border-l-4 border-red-500 bg-white p-4 text-sm text-red-800 shadow-sm">
          {t('dashboard.designatedError')}
        </p>
      )}

      {!isLoading && !isError && (
        <>
          <DataTable
            table={table}
            emptyMessage={t('dashboard.designatedEmpty')}
            footer={(
              <div className="flex items-center justify-between text-sm text-slate-600">
                <button
                  type="button"
                  disabled={page <= 1}
                  onClick={() => setPage((current) => Math.max(1, current - 1))}
                  className="rounded-md border border-slate-300 px-3 py-1 disabled:opacity-40"
                >
                  {t('common.previousPage')}
                </button>
                <span>{t('common.pageOf', { page, totalPages })}</span>
                <button
                  type="button"
                  disabled={page >= totalPages}
                  onClick={() => setPage((current) => Math.min(totalPages, current + 1))}
                  className="rounded-md border border-slate-300 px-3 py-1 disabled:opacity-40"
                >
                  {t('common.nextPage')}
                </button>
              </div>
            )}
          />
        </>
      )}
    </section>
  )
}
