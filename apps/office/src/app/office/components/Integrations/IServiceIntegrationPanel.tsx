import { useState } from 'react'
import { IServiceCredentialsForm } from './IServiceCredentialsForm'
import { IServiceValidationStatus } from './IServiceValidationStatus'

interface IServiceIntegrationPanelProps {
  tenantId: string
  integrationId: string
  className?: string
}

/**
 * RF-006/RF-007: painel de configuração da integração iService.
 *
 * Compõe o formulário de credenciais e o status de validação. Ao salvar
 * credenciais com sucesso, força a releitura do status (a alteração reseta
 * validationStatus para NotValidated no backend, RN-006.3).
 */
export function IServiceIntegrationPanel({
  tenantId,
  integrationId,
  className = '',
}: IServiceIntegrationPanelProps) {
  const [statusKey, setStatusKey] = useState(0)

  return (
    <div className={`space-y-6 ${className}`}>
      <IServiceCredentialsForm
        tenantId={tenantId}
        integrationId={integrationId}
        onSaved={() => setStatusKey((key) => key + 1)}
      />
      <IServiceValidationStatus key={statusKey} tenantId={tenantId} integrationId={integrationId} />
    </div>
  )
}

export default IServiceIntegrationPanel
