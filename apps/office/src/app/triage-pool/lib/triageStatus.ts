import type { StatusTone } from '../../dashboard/lib/providerStatus'
import type { TriagemPoolStatus } from '../../../shared/types/triagePool'

/** Rótulo PT-BR amigável para os estados do pool (C1/C2) e os dois estados fora do pool (detalhe/404 só). */
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
  EmColeta: 'neutral',
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
