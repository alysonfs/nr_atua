import { useState } from 'react'
import { ProviderSelector, type EIntegrationProvider } from './ProviderSelector'
import { IServiceIntegrationPanel } from './IServiceIntegrationPanel'

interface ProviderIntegrationSectionProps {
  tenantId: string
  integrationId: string
  className?: string
}

/**
 * Orquestra o fluxo de "Integração com provedor": o usuário decide qual
 * provedor deseja configurar antes de qualquer formulário de credenciais ser
 * exibido. A configuração do provedor escolhido é exibida dentro do próprio
 * cartão de seleção, para manter o contexto. Hoje só existe o iService, mas
 * o passo de decisão fica explícito para comportar futuros provedores
 * (ex.: Meta/WhatsApp).
 */
export function ProviderIntegrationSection({
  tenantId,
  integrationId,
  className = '',
}: ProviderIntegrationSectionProps) {
  const [selectedProvider, setSelectedProvider] = useState<EIntegrationProvider | null>(null)

  // Clicar de novo no provedor já selecionado recolhe a área (toggle).
  const handleSelect = (provider: EIntegrationProvider) => {
    setSelectedProvider((current) => (current === provider ? null : provider))
  }

  return (
    <div className={className}>
      <ProviderSelector selectedProvider={selectedProvider} onSelect={handleSelect}>
        {selectedProvider === 'iservice' && (
          <IServiceIntegrationPanel tenantId={tenantId} integrationId={integrationId} />
        )}
      </ProviderSelector>
    </div>
  )
}

export default ProviderIntegrationSection
