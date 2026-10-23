import { useTranslation } from 'react-i18next'
import { statusFiltersForScope, translateTriagemPoolStatus } from '../lib/triageStatus'
import type { TriagemListScope, TriagemListStatus } from '../../../shared/types/triagePool'

interface TriagePoolStatusFilterProps {
  value: TriagemListStatus | undefined
  onChange: (value: TriagemListStatus | undefined) => void
  /** Escopo ativo: define as opções de estado disponíveis. Padrão: `pool`. */
  scope?: TriagemListScope
}

/** Filtro por um único estado do escopo ativo (C1, critério de aceite 4). "Todos" remove o filtro. */
export function TriagePoolStatusFilter({ value, onChange, scope = 'pool' }: TriagePoolStatusFilterProps) {
  const { t } = useTranslation()

  return (
    <label className="flex items-center gap-2 text-xs text-slate-600">
      {t('triagePool.filter.label')}
      <select
        className="rounded-md border border-slate-300 px-2 py-1 text-sm"
        value={value ?? ''}
        onChange={(event) => onChange(event.target.value ? (event.target.value as TriagemListStatus) : undefined)}
      >
        <option value="">{t('triagePool.filter.all')}</option>
        {statusFiltersForScope(scope).map((status) => (
          <option key={status} value={status}>
            {translateTriagemPoolStatus(status)}
          </option>
        ))}
      </select>
    </label>
  )
}
