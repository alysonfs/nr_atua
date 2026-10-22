# ADR-045 - Retenção de Anexos e Transcrição de Áudios da Triagem

## Status

**Accepted** — política de produto definida pelo Product Owner em 2026-10-20.
Não representa parecer jurídico nem implementação concluída.

## Contexto

O ADR-044 define retenção das mensagens inbound no banco primário, mas não
dos anexos em S3. O próximo incremento da triagem inclui recebimento de
mídias. O Product Owner definiu uma política comum para todos os anexos e
incluiu a transcrição dos áudios recebidos.

## Decisão

- Contar a retenção desde o recebimento da mensagem no inbound do posto,
  não desde o upload do anexo, sua transcrição ou a conclusão da OS.
- Manter todos os tipos de anexo pelo mesmo prazo de cinco anos desde esse
  recebimento. O anexo torna-se elegível para expurgo físico um ano depois
  desse marco, isto é, seis anos desde o recebimento.
- Armazenar os anexos, incluindo os arquivos originais de áudio, em S3,
  vinculados à OS e à triagem, com isolamento por tenant.
- Transcrever os áudios recebidos e salvar a transcrição segundo a mesma
  lógica de persistência e retenção das mensagens inbound do ADR-044.
  Preservar a associação com o áudio original e o instante de recebimento
  da mensagem; gerar ou reprocessar a transcrição não reinicia a retenção.
- A transcrição não substitui o armazenamento do áudio original.
- Preservar a precedência de RS-003: solicitação de exclusão ou inatividade
  da conta por mais de cinco anos pode antecipar a remoção. O fluxo geral de
  exclusão do tenant deve abranger anexos e transcrições, além das mensagens.

## Limites e pendências

- O prazo foi informado pelo Product Owner como legal. A justificativa
  jurídica, a base legal e eventuais exceções ainda precisam de validação
  jurídica antes da produção; este registro não comprova obrigação legal de
  cinco anos.
- A retenção e a exclusão de cópias em backups permanecem pendentes.
  Excluir o arquivo ativo não comprova a remoção dessas cópias.
- O mecanismo de armazenamento, expurgo e transcrição, seu fornecedor,
  local de processamento, limites e custos foram detalhados no
  [ADR-046](ADR-046-pipeline-midia-transcricao-audio-triagem.md), aceito como
  direção arquitetural. A ingestão inicial autorizada está delimitada pelo
  [ADR-047](ADR-047-ingestao-inicial-midias-triagem.md).
- Esta decisão define retenção, mas não autoriza por si só implementação,
  acesso operacional ao S3, transcrição ou exclusão geral do tenant.
- Este ADR não altera o checklist mínimo nem autoriza a LLM a agendar,
  cancelar ou concluir a OS.

## Consequências

- Anexos e mensagens passam a compartilhar os marcos de cinco anos de
  retenção e elegibilidade para expurgo físico um ano depois, sem prazos
  distintos por tipo de anexo.
- O armazenamento deve considerar o volume de áudios, fotos e vídeos durante
  esse período. A transcrição introduz custo e processamento adicionais.
- Arquivo de áudio e texto transcrito devem ser rastreáveis entre si e
  respeitar as mesmas restrições de acesso e exclusão do atendimento.
- Uma falha na transcrição deve ser explícita, sem texto fictício ou
  resultado apresentado como transcrição concluída.

## Alternativas consideradas

- Prazos diferentes por tipo de mídia: não adotados pelo Product Owner.
- Manter apenas a transcrição e descartar o áudio: não adotado; o original
  também deve ser armazenado em S3.

## Agentes envolvidos

- Product Owner
- `orchestrator`

## Data

2026-10-20

## Relações

- Complementa o [ADR-044](ADR-044-retencao-mensagens-inbound.md), sem
  substituir a política de mensagens.
- O pipeline técnico está no
  [ADR-046](ADR-046-pipeline-midia-transcricao-audio-triagem.md), aceito como
  direção arquitetural em 2026-10-20. O escopo inicial de ingestão está no
  [ADR-047](ADR-047-ingestao-inicial-midias-triagem.md); escolha final do
  fornecedor de transcrição, qualidade e custo permanecem pendentes.
- Deve respeitar RS-003.
- Detalha os requisitos RF-TRIAGEM-017 e RF-TRIAGEM-018 do
  [épico da triagem](../features/triagem-whatsapp-fase-1.md).
