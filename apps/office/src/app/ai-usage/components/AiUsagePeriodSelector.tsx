import { useTranslation } from 'react-i18next'
import { cn } from '../../../shared/lib/cn'
import { AI_USAGE_PERIODS, type AiUsagePeriod } from '../lib/aiUsagePeriod'

interface AiUsagePeriodSelectorProps {
  value: AiUsagePeriod
  onChange: (period: AiUsagePeriod) => void
}

/**
 * Seletor de período da página "Inteligência Artificial": 7/30/90 dias e
 * mês corrente. Mesmo padrão de abas (`role="tablist"`) usado em
 * `TriagePoolPage` para alternar escopo.
 */
export function AiUsagePeriodSelector({ value, onChange }: AiUsagePeriodSelectorProps) {
  const { t } = useTranslation()

  return (
    <div role="tablist" aria-label={t('aiUsage.period.label')} className="flex gap-2 border-b border-slate-200">
      {AI_USAGE_PERIODS.map((period) => (
        <button
          key={period}
          type="button"
          role="tab"
          aria-selected={value === period}
          className={cn(
            'border-b-2 px-3 py-2 text-sm font-medium transition-colors',
            value === period
              ? 'border-slate-900 text-slate-950'
              : 'border-transparent text-slate-500 hover:text-slate-700',
          )}
          onClick={() => onChange(period)}
        >
          {t(`aiUsage.period.options.${period}`)}
        </button>
      ))}
    </div>
  )
}
