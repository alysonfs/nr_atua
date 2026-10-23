/**
 * Pool de triagem — visão do operador (épico `docs/features/triagem-pool-operador.md`).
 *
 * Tipos espelhando o contrato de
 * `apps/api/Atua.Api/Endpoints/TriagemPoolEndpoints.cs` (serialização
 * camelCase padrão do ASP.NET Core minimal APIs).
 */

/** Os quatro estados do pool compartilhado (C1). Nunca `EmColeta`/`AssumidaPorOperador`. */
export type TriagemPoolStatus =
  | 'LiberadaAoPool'
  | 'Expirada'
  | 'FalhaDeContato'
  | 'AguardandoIntervencao'

/** Estados "em triagem" (pré-pool): a LLM ainda conduz a coleta. Somente leitura no Office. */
export type TriagemInProgressStatus = 'ContatoIniciado' | 'EmColeta' | 'InformacoesSuficientes'

/** Qualquer estado listável no Office (pool ou em triagem). */
export type TriagemListStatus = TriagemPoolStatus | TriagemInProgressStatus

/**
 * Escopo da listagem (query param `scope` da API): `pool` (padrão),
 * `in-progress` (pré-pool) ou `all` (união de ambos — usado apenas para o
 * contador de navegação, que precisa de uma única requisição).
 */
export type TriagemListScope = 'pool' | 'in-progress' | 'all'

export interface TriagemPoolListItem {
  triagemId: string
  workOrderProviderNo: string | null
  customerName: string | null
  productModel: string | null
  productBrand: string | null
  status: TriagemListStatus
  checklistItemsCompleted: number
  checklistItemsTotal: number
  enteredPoolAtUtc: string
  hasNewActivity: boolean
}

export interface TriagemPoolListResponse {
  totalCount: number
  items: TriagemPoolListItem[]
}

export type TriagemChecklistItem =
  | 'DescricaoDefeito'
  | 'NotaFiscal'
  | 'Etiqueta'
  | 'MidiaDefeito'
  | 'Endereco'
  | 'PontoReferencia'
  | 'Localizacao'

export interface TriagemPoolChecklistItemView {
  item: TriagemChecklistItem
  required: boolean
  completed: boolean
  value: string | null
}

export type TriagemInboundMediaKind = 'Audio' | 'Image' | 'Video' | 'Document'

export interface TriagemPoolAttachment {
  mediaId: string
  mediaKind: TriagemInboundMediaKind
  contentType: string
  isVoiceNote: boolean
  durationSeconds: number | null
  caption: string | null
  receivedAtUtc: string
  url: string
  urlExpiresAtUtc: string
}

export interface TriagemPoolConversationEntry {
  atUtc: string
  sender: string
  kind: string
  text: string | null
  attachmentMediaId: string | null
}

export interface TriagemPoolWorkOrderView {
  workOrderId: string
  workOrderProviderNo: string | null
  customerName: string | null
  customerCpf: string | null
  contactPhone: string | null
  contactEmail: string | null
  address: string | null
  zipCode: string | null
  cityName: string | null
  stateName: string | null
  productBrand: string | null
  productModel: string | null
  symptom: string | null
}

export interface TriagemPoolDetail {
  triagemId: string
  status: TriagemListStatus | 'AssumidaPorOperador' | string
  stateReason: string
  enteredPoolAtUtc: string
  assumidaPorUserId: string | null
  assumidaEmUtc: string | null
  workOrder: TriagemPoolWorkOrderView
  checklist: TriagemPoolChecklistItemView[]
  attachments: TriagemPoolAttachment[]
  conversation: TriagemPoolConversationEntry[]
}
