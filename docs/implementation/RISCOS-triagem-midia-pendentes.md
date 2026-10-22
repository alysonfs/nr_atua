# Riscos pendentes — ingestão de mídia da triagem

Registrado em 2026-10-22, após o deploy da API (commit `eae1edc`).
Decisão do Product Owner: tratar estes riscos no próximo ciclo de orçamento;
seguir agora para a visão do pool de triagem e para a LLM.

| # | Severidade | Componente | Risco | Mitigação proposta |
|---|---|---|---|---|
| R3 | Média | API (`TriagemInboundMediaRepository.cs`) | Objeto é gravado no S3 antes do commit no banco; falha posterior deixa objeto órfão, fora do alcance do expurgo. | Compensar com `DeleteObject` na falha ou reconciliar objetos sem metadados. |
| R4 | Média | Channel | Após `SessionFatalError`, a sessão pode reconectar sem receber eventos; exige novo pareamento por QR. | Detectar ausência de eventos e alertar; documentar procedimento de repareamento. |
| R5 | Baixa | API | Avisos NU1902/NU1903 em `System.Security.Cryptography.Pkcs` 9.0.0. | Atualizar a dependência transitiva. |

## Já resolvido (2026-10-22)

- Reentrega idempotente com bytes diferentes agora compara o SHA-256 real.
- Job de expurgo interrompe o ciclo quando um lote não progride.
- Limite de tamanho de requisição no endpoint de mídia.
- **R1** — Mídia inbound agora é baixada em streaming com limite de 16 MiB
  (`media-download-limit.ts`, alinhado ao limite aceito pela API); ao
  exceder o limite, o stream é destruído sem materializar o binário
  completo e o evento é descartado com log
  (`whatsapp_inbound_media_too_large_discarded`). A fila de mídia inbound
  (`incoming-media-worker.ts`) agora tem capacidade máxima configurável
  (`ChannelWorker__MediaQueueCapacity`, padrão 20) e descarta com log
  (`channel_inbound_media_queue_full_discarded`) ao atingir o limite, em
  vez de crescer sem limite.
- **R2** — Rejeições permanentes do `ingestMedia` (400/404/409/413/415
  etc.) deixaram de encerrar o processo do Channel: são registradas
  (`channel_inbound_media_rejected`) e o worker segue consumindo a
  fila normalmente. Apenas erros de autenticação da credencial de serviço
  (401/403) continuam fatais, pois indicam configuração incorreta e não
  devem ser mascarados.

## Bloqueios do Channel

R1 e R2 foram resolvidos em 2026-10-22 (ver acima) e deixam de bloquear a
publicação do Channel com ingestão de mídia. R3 (orfandade de objeto S3) e
R4 (repareamento silencioso) permanecem como riscos conhecidos, não
bloqueantes, a tratar no próximo ciclo. Transcrição de áudio segue adiada
(gates do ADR-046).
