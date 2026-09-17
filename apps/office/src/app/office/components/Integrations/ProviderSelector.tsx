import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'

export type EIntegrationProvider = 'iservice'

interface ProviderOption {
  id: EIntegrationProvider
  available: boolean
}

const PROVIDER_OPTIONS: ProviderOption[] = [{ id: 'iservice', available: true }]

interface ProviderSelectorProps {
  selectedProvider: EIntegrationProvider | null
  onSelect: (provider: EIntegrationProvider) => void
  /** Configuração do provedor selecionado, exibida dentro deste mesmo cartão. */
  children?: ReactNode
  className?: string
}

/**
 * Passo 1 da integração com provedor: o usuário decide explicitamente qual
 * provedor deseja configurar. Hoje apenas o iService está disponível, mas o
 * layout já comporta a listagem de futuros provedores.
 */
export function ProviderSelector({
  selectedProvider,
  onSelect,
  children,
  className = '',
}: ProviderSelectorProps) {
  const { t } = useTranslation()

  return (
    <div className={`rounded-md border border-slate-200 bg-white shadow-sm ${className}`}>
      <div className="border-b border-slate-200 px-5 py-4">
        <h2 className="text-base font-semibold text-slate-950">{t('integration.chooseProviderTitle')}</h2>
        <p className="mt-1 max-w-3xl text-sm text-slate-600">{t('integration.chooseProviderDescription')}</p>
      </div>

      <div role="radiogroup" aria-label={t('integration.chooseProviderTitle')} className="grid gap-3 px-5 py-4 sm:grid-cols-2">
        {PROVIDER_OPTIONS.map((option) => {
          const isSelected = selectedProvider === option.id
          return (
            <button
              key={option.id}
              type="button"
              role="radio"
              aria-checked={isSelected}
              disabled={!option.available}
              onClick={() => onSelect(option.id)}
              className={`rounded-lg border p-4 text-left transition ${
                isSelected
                  ? 'border-blue-600 bg-blue-50 ring-2 ring-blue-500/30'
                  : 'border-slate-300 bg-white hover:border-blue-400'
              } disabled:cursor-not-allowed disabled:opacity-50`}
            >
              <p className="text-sm font-semibold text-slate-950">{t(`integration.providers.${option.id}`)}</p>
              <p className="mt-1 text-xs text-slate-600">
                {option.available ? t('integration.providerAvailable') : t('integration.providerComingSoon')}
              </p>
            </button>
          )
        })}
      </div>

      {children && <div className="border-t border-slate-200 px-5 py-5">{children}</div>}
    </div>
  )
}

export default ProviderSelector
