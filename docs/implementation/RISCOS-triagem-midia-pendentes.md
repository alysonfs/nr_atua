# Riscos pendentes — ingestão de mídia da triagem

Registrado em 2026-10-22, após o deploy da API (commit `eae1edc`).
Decisão do Product Owner: tratar estes riscos no próximo ciclo de orçamento;
seguir agora para a visão do pool de triagem e para a LLM.

| # | Severidade | Componente | Risco | Mitigação proposta |
|---|---|---|---|---|
| R1 | Alta | Channel (`baileys-session.ts`, `incoming-media-worker.ts`) | Mídia é baixada inteira para memória e enfileirada sem limite; volume alto ou API indisponível pode esgotar a memória do processo. | Limitar tamanho durante o download (16 MiB) e impor capacidade/backpressure à fila. |
| R2 | Alta | Channel (`incoming-media-worker.ts`, `main.ts`) | Um erro não retentável (ex.: 400/409) encerra o processo inteiro do Channel. | Tratar rejeição permanente por evento: registrar e seguir consumindo a fila. |
| R3 | Média | API (`TriagemInboundMediaRepository.cs`) | Objeto é gravado no S3 antes do commit no banco; falha posterior deixa objeto órfão, fora do alcance do expurgo. | Compensar com `DeleteObject` na falha ou reconciliar objetos sem metadados. |
| R4 | Média | Channel | Após `SessionFatalError`, a sessão pode reconectar sem receber eventos; exige novo pareamento por QR. | Detectar ausência de eventos e alertar; documentar procedimento de repareamento. |
| R5 | Baixa | API | Avisos NU1902/NU1903 em `System.Security.Cryptography.Pkcs` 9.0.0. | Atualizar a dependência transitiva. |

## Já resolvido (2026-10-22)

- Reentrega idempotente com bytes diferentes agora compara o SHA-256 real.
- Job de expurgo interrompe o ciclo quando um lote não progride.
- Limite de tamanho de requisição no endpoint de mídia.

## Bloqueios do Channel

O Channel com ingestão de mídia **não deve ser publicado** em produção antes
de R1 e R2. O E2E remoto de mídia (PDF, áudio, vídeo) também depende disso.
Transcrição de áudio segue adiada (gates do ADR-046).
