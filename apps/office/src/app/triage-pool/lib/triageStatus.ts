import type { StatusTone } from '../../dashboard/lib/providerStatus'
import type {
  TriagemInProgressStatus,
  TriagemListScope,
  TriagemListStatus,
  TriagemPoolStatus,
} from '../../../shared/types/triagePool'

/** Rótulo PT-BR amigável para os estados do pool, os estados "em triagem" (pré-pool) e os demais. */
const STATUS_LABELS: Record<string, string> = {
  LiberadaAoPool: 'Liberada ao pool',
  Expirada: 'Expirada',
  FalhaDeContato: 'Falha de contato',
  AguardandoIntervencao: 'Aguardando intervenção',
  EmColeta: 'Em coleta',
  AssumidaPorOperador: 'Assumida por operador',
  AguardandoEnvio: 'Aguardando envio',
  ContatoIniciado: 'Contato iniciado',
  InformacoesSuficientes: 'Informações suficientes',
}

const STATUS_TONES: Record<string, StatusTone> = {
  LiberadaAoPool: 'info',
  Expirada: 'danger',
  FalhaDeContato: 'warning',
  AguardandoIntervencao: 'warning',
  ContatoIniciado: 'neutral',
  EmColeta: 'neutral',
  InformacoesSuficientes: 'info',
  AssumidaPorOperador: 'success',
}

export function translateTriagemPoolStatus(status: string): string {
  return STATUS_LABELS[status] ?? status
}

export function triagemPoolStatusTone(status: string): StatusTone {
  return STATUS_TONES[status] ?? 'neutral'
}

/** As quatro opções de filtro de estado do pool (épico, "C1 — Lista do pool"). */
export const TRIAGE_POOL_STATUS_FILTERS: TriagemPoolStatus[] = [
  'LiberadaAoPool',
  'Expirada',
  'FalhaDeContato',
  'AguardandoIntervencao',
]

/** Opções de filtro dos estados "em triagem" (pré-pool), visíveis no Office só para leitura. */
export const TRIAGE_IN_PROGRESS_STATUS_FILTERS: TriagemInProgressStatus[] = [
  'ContatoIniciado',
  'EmColeta',
  'InformacoesSuficientes',
]

export function statusFiltersForScope(scope: TriagemListScope): TriagemListStatus[] {
  return scope === 'in-progress' ? TRIAGE_IN_PROGRESS_STATUS_FILTERS : TRIAGE_POOL_STATUS_FILTERS
}

const CHECKLIST_ITEM_LABELS: Record<string, string> = {
  DescricaoDefeito: 'Descrição do defeito',
  NotaFiscal: 'Nota fiscal legível',
  Etiqueta: 'Etiqueta (modelo e série)',
  MidiaDefeito: 'Foto ou vídeo do defeito',
  Endereco: 'Endereço completo',
  PontoReferencia: 'Ponto de referência',
  Localizacao: 'Localização',
}

export function translateChecklistItem(item: string): string {
  return CHECKLIST_ITEM_LABELS[item] ?? item
}

const SENDER_LABELS: Record<string, string> = {
  consumidor: 'Consumidor',
  posto: 'Posto',
}

export function translateConversationSender(sender: string): string {
  return SENDER_LABELS[sender] ?? sender
}
