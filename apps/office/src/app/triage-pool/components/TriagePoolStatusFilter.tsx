import { useTranslation } from 'react-i18next'
import { TRIAGE_POOL_STATUS_FILTERS, translateTriagemPoolStatus } from '../lib/triageStatus'
import type { TriagemPoolStatus } from '../../../shared/types/triagePool'

interface TriagePoolStatusFilterProps {
  value: TriagemPoolStatus | undefined
  onChange: (value: TriagemPoolStatus | undefined) => void
}

/** Filtro por um único estado do pool (C1, critério de aceite 4). "Todos" remove o filtro. */
export function TriagePoolStatusFilter({ value, onChange }: TriagePoolStatusFilterProps) {
  const { t } = useTranslation()

  return (
    <label className="flex items-center gap-2 text-xs text-slate-600">
      {t('triagePool.filter.label')}
      <select
        className="rounded-md border border-slate-300 px-2 py-1 text-sm"
        value={value ?? ''}
        onChange={(event) => onChange(event.target.value ? (event.target.value as TriagemPoolStatus) : undefined)}
      >
        <option value="">{t('triagePool.filter.all')}</option>
        {TRIAGE_POOL_STATUS_FILTERS.map((status) => (
          <option key={status} value={status}>
            {translateTriagemPoolStatus(status)}
          </option>
        ))}
      </select>
    </label>
  )
}
