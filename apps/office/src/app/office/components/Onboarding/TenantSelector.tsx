import { useState } from 'react'
import type { TenantMembershipDTO } from '../../../../shared/types/tenant'

interface TenantSelectorProps {
  tenants: TenantMembershipDTO[]
  onSelect: (tenantId: string) => void
  className?: string
}

/**
 * RF-005.2: seleção explícita de tenant quando o usuário possui múltiplos
 * memberships ativos. A UI é intencionalmente simples (seletor discreto),
 * conforme decisão mínima registrada em RF-005 (não há tela dedicada de
 * troca de tenant obrigatória no MVP).
 */
export function TenantSelector({ tenants, onSelect, className = '' }: TenantSelectorProps) {
  const [selected, setSelected] = useState('')

  if (tenants.length === 0) {
    return null
  }

  return (
    <div className={`rounded-lg border border-slate-200 bg-white p-6 shadow-sm ${className}`}>
      <h2 className="mb-1 text-lg font-semibold text-slate-900">Selecione uma empresa</h2>
      <p className="mb-4 text-sm text-slate-600">
        Você possui acesso a mais de uma empresa. Escolha qual deseja acessar.
      </p>

      <label htmlFor="tenant-select" className="sr-only">
        Empresa
      </label>
      <select
        id="tenant-select"
        value={selected}
        onChange={(event) => setSelected(event.target.value)}
        className="w-full rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500/20"
      >
        <option value="" disabled>
          Selecione...
        </option>
        {tenants.map((tenant) => (
          <option key={tenant.tenantId} value={tenant.tenantId}>
            {tenant.name}
          </option>
        ))}
      </select>

      <button
        type="button"
        disabled={!selected}
        onClick={() => onSelect(selected)}
        className="mt-4 w-full rounded-lg bg-blue-600 px-4 py-2 font-semibold text-white transition-colors hover:bg-blue-700 active:bg-blue-800 disabled:cursor-not-allowed disabled:bg-slate-300"
      >
        Acessar
      </button>
    </div>
  )
}

export default TenantSelector
