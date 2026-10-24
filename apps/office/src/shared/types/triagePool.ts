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

/**
 * Consumo e custo estimado de LLM (IA) de uma única triagem (ADR-048),
 * espelhando `TriagemLlmUsageResponse` em
 * `apps/api/Atua.Api/Endpoints/TriagemPoolEndpoints.cs`. Opcional e com
 * todos os campos opcionais enquanto o backend não publica (ou ainda não
 * implantou) o campo — nesse caso a UI exibe "—" em vez de "US$ 0,00".
 */
export interface TriagemLlmUsage {
  calls?: number
  inputTokens?: number
  outputTokens?: number
  /** Custo estimado em dólares americanos (USD). */
  estimatedCostUsd?: number
}

/** Os três estados da estimativa de garantia (nunca uma certeza jurídica). */
export type TriagemWarrantyEstimateStatus =
  | 'ProvavelmenteNaGarantia'
  | 'ProvavelmenteForaDaGarantia'
  | 'Indeterminado'

/**
 * Estimativa de garantia calculada na leitura a partir da data de emissão
 * da nota fiscal e de um prazo padrão (`defaultMonths`, hoje 12 meses) —
 * nunca uma regra por categoria de produto (prevista para uma versão
 * futura), nem uma certeza jurídica. Espelha `WarrantyEstimateResponse`.
 * Todos os campos são opcionais enquanto o backend não os publica.
 */
export interface TriagemWarrantyEstimate {
  status?: TriagemWarrantyEstimateStatus
  /** Data de emissão da nota fiscal usada como base do cálculo (ISO 8601). */
  baseDate?: string | null
  /** Prazo padrão usado na estimativa, em meses (hoje sempre 12). */
  defaultMonths?: number
}

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
  /**
   * Início do atendimento da triagem (ISO 8601). Opcional enquanto o
   * backend não publica o campo — tratar ausência como "—" na UI.
   */
  startedAtUtc?: string
  /**
   * Conclusão do atendimento da triagem (ISO 8601) ou `null`/ausente
   * enquanto a triagem está em andamento.
   */
  finishedAtUtc?: string | null
  /** Custo de LLM (IA) desta triagem. Opcional enquanto ausente do backend. */
  llmUsage?: TriagemLlmUsage
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
  contentPath?: string
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
  /**
   * Início do atendimento da triagem (ISO 8601). Opcional enquanto o
   * backend não publica o campo — tratar ausência como "—" na UI.
   */
  startedAtUtc?: string
  /**
   * Conclusão do atendimento da triagem (ISO 8601) ou `null`/ausente
   * enquanto a triagem está em andamento.
   */
  finishedAtUtc?: string | null
  /** Custo de LLM (IA) desta triagem. Opcional enquanto ausente do backend. */
  llmUsage?: TriagemLlmUsage
  /** Estimativa de garantia (ver `TriagemWarrantyEstimate`). Opcional enquanto ausente do backend. */
  warrantyEstimate?: TriagemWarrantyEstimate
  /** Número da nota fiscal extraído da classificação de visão, quando já extraído. */
  notaFiscalNumero?: string | null
  /** Série da nota fiscal — mesma origem e ressalva de `notaFiscalNumero`. */
  notaFiscalSerie?: string | null
}
