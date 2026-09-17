import { useState } from 'react'
import { IServiceCredentialsForm } from './IServiceCredentialsForm'
import { IServiceValidationStatus } from './IServiceValidationStatus'
import { CollectorActivationPanel } from './CollectorActivationPanel'

interface IServiceIntegrationPanelProps {
  tenantId: string
  integrationId: string
  className?: string
}

/**
 * RF-006/RF-007/RF-008: painel de configuração da integração iService.
 *
 * Compõe o formulário de credenciais, o status de validação e a ativação do
 * Agente Coletor. Ao salvar credenciais com sucesso, força a releitura do
 * status (a alteração reseta validationStatus para NotValidated no backend,
 * RN-006.3) e do estado de ativação. Ao validar com sucesso, também força a
 * releitura da ativação para refletir o status mais recente.
 */
export function IServiceIntegrationPanel({
  tenantId,
  integrationId,
  className = '',
}: IServiceIntegrationPanelProps) {
  const [statusKey, setStatusKey] = useState(0)
  const [activationKey, setActivationKey] = useState(0)

  return (
    <div className={`space-y-6 ${className}`}>
      <IServiceCredentialsForm
        tenantId={tenantId}
        integrationId={integrationId}
        onSaved={() => {
          setStatusKey((key) => key + 1)
          setActivationKey((key) => key + 1)
        }}
      />
      <IServiceValidationStatus
        key={`validation-${statusKey}`}
        tenantId={tenantId}
        integrationId={integrationId}
        onValidated={() => setActivationKey((key) => key + 1)}
      />
      <CollectorActivationPanel
        key={`activation-${activationKey}`}
        tenantId={tenantId}
        integrationId={integrationId}
      />
    </div>
  )
}

export default IServiceIntegrationPanel
