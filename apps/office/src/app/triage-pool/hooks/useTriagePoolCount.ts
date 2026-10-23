import { useMyTenants } from '../../office/hooks/useTenants'
import { useTriagePoolList } from './useTriagePoolList'

/**
 * Contador do pool de triagem para a navegação (épico, seção 2 — "Contador
 * de itens do pool visível na navegação"), atualizado pelo mesmo polling de
 * 30 s da lista (C1).
 *
 * Conta o pool propriamente dito e os estados "em triagem" (pré-pool:
 * `ContatoIniciado`, `EmColeta`, `InformacoesSuficientes`), via
 * `scope=all`, numa única requisição.
 *
 * Auto-contido: resolve o tenant ativo do usuário internamente (mesmo
 * padrão de `TriagemMessageTemplatesSection`/`TrialBadge`), para ser usado
 * diretamente no Sidebar/MobileMenu sem exigir prop-drilling de tenantId.
 */
export function useTriagePoolCount() {
  const { defaultTenantId } = useMyTenants()
  const { totalCount, isError } = useTriagePoolList(defaultTenantId, undefined, 'all')

  return { count: totalCount, isError }
}
