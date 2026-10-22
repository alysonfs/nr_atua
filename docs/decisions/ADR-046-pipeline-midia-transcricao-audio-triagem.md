# ADR-046 - Pipeline de Mídia e Transcrição de Áudio da Triagem

## Status

**Accepted** — direção arquitetural aprovada por `software-architect` e
`aws-architect` em 2026-10-20. A escolha final do fornecedor, qualidade e
custo ainda não foram validados.

## Contexto

O [ADR-045](ADR-045-retencao-anexos-e-transcricao-audios-triagem.md)
define a política de produto para anexos e transcrições. O Product Owner
também definiu que a transcrição pode ocorrer em segundo plano, que o
Amazon Transcribe deve ser avaliado primeiro e que o volume de áudio ainda
é desconhecido.

A arquitetura atual atribui à Master API o controle de ingestão e
armazenamento durável dos anexos. O evento `MediaReceived` ainda não está
implementado.

## Decisão

### Ingestão e transcrição

- O Channel encaminha `MediaReceived` à Master API, que valida a associação
  com a triagem/OS e controla a persistência durável do arquivo original em
  S3 privado. A transcrição só começa após a persistência do áudio original.
- O processamento da transcrição é assíncrono e não prolonga a resposta
  síncrona do evento de mídia.
- Avaliar primeiro **Amazon Transcribe batch em `sa-east-1`**. A documentação
  consultada em 2026-10-20 lista `pt-BR` para transcrição batch e streaming
  ([idiomas suportados](https://docs.aws.amazon.com/transcribe/latest/dg/supported-languages.html))
  e endpoint batch em `sa-east-1`
  ([endpoints](https://docs.aws.amazon.com/general/latest/gr/transcribe.html)).
  A escolha final fica condicionada à avaliação de qualidade com áudios
  representativos e à estimativa de custo.
- O resultado da transcrição é persistido segundo a lógica de mensagens
  inbound, relacionado ao áudio original e ao mesmo atendimento. A
  transcrição herda o `ReceivedAtUtc` da mensagem original; gerar ou
  reprocessar o texto não reinicia a retenção.
- Enquanto a transcrição estiver pendente ou tiver falhado, a triagem pode
  continuar e ser liberada conforme o checklist vigente. O operador mantém
  acesso ao áudio original e vê a transcrição quando estiver pronta.
- Os estados pendente, concluído e falha devem ser distinguíveis. Falha não
  pode ser representada como texto vazio ou transcrição bem-sucedida. A
  política exata de retentativas permanece a definir na implementação.

### Retenção, expurgo e segurança

- Anexos e transcrições seguem o marco do ADR-045: cinco anos desde o
  recebimento da mensagem inbound e elegibilidade para expurgo físico um
  ano depois, respeitando a precedência de RS-003.
- O instante de elegibilidade é determinado pela aplicação a partir do
  recebimento inbound. Uma regra S3 Lifecycle baseada apenas na idade do
  objeto não deve ser a autoridade desse cálculo quando o upload ocorrer
  depois da mensagem. Se houver versionamento, o expurgo também deve
  considerar versões não atuais; a regra de expiração da versão atual pode
  apenas criar um delete marker. A exclusão será feita por um job ativo da
  aplicação que avalia o marco de retenção persistido; Lifecycle pode ser
  complementar, nunca o mecanismo autoritativo. Ver
  [considerações de expiração do S3](https://docs.aws.amazon.com/AmazonS3/latest/userguide/lifecycle-expire-general-considerations.html).
- O bucket permanece privado, com criptografia em trânsito e em repouso,
  isolamento entre tenants e permissões de menor privilégio.
- A exclusão de backups não é definida por esta proposta; backups continuam
  uma pendência separada.

## Alternativas consideradas

- **Amazon Transcribe streaming:** não recomendado para o primeiro
  incremento porque o Product Owner aceita processamento em segundo plano;
  streaming pode ser reavaliado se surgir um requisito de baixa latência.
- **Provedor externo de transcrição:** avaliar apenas se qualidade,
  requisitos de processamento ou custo não forem atendidos pela opção AWS.
  Exige revisão de privacidade e local de processamento antes de adoção.
- **Transcrição antes do upload durável:** rejeitada; o áudio original deve
  continuar disponível mesmo se a transcrição falhar.

## Custos e validação

Não há estimativa numérica aprovada porque o volume é desconhecido. A
estimativa deve considerar minutos de áudio transcritos, duração, tamanho e
quantidade de mídias, armazenamento acumulado durante a retenção, requests,
recuperação e transferências. Fontes oficiais: [preços do Amazon
Transcribe](https://aws.amazon.com/transcribe/pricing/) e [preços do
Amazon S3](https://aws.amazon.com/s3/pricing/).

Informação preliminar do Product Owner: consumidores enviam áudio às vezes;
a duração mais comum é estimada entre 1 e 5 minutos; o formato observado é
mensagem de voz gravada no WhatsApp. A quantidade mensal ainda não foi
estimada, portanto essa informação qualitativa não permite calcular custo.

Antes de implementar ou ativar a transcrição, validar qualidade de português
brasileiro com uma amostra representativa, formatos/duração máxima aceitos,
comportamento de retentativa e disponibilidade operacional do processamento
assíncrono.
O critério de qualidade, o prazo máximo esperado para transcrição, a
retenção de metadados/histórico de jobs mantidos pelo provedor e a política
de retentativa ainda precisam de definição. Uma amostra autorizada e
desidentificada e dados de volume/duração devem ser obtidos antes da
implementação da transcrição e da escolha final do fornecedor.

## Consequências

- O recebimento de uma mídia fica desacoplado do tempo de transcrição.
- Uma falha na transcrição não perde nem substitui o áudio original e não
  bloqueia a triagem.
- Custos e qualidade do serviço não podem ser considerados validados até
  haver amostra de teste e dados de volume.
- Validação jurídica da retenção e política de backups continuam pendentes
  para produção.
- Esta decisão, por si só, não autoriza chamadas a serviços AWS,
  provisionamento ou deploy. A implementação local do incremento de ingestão
  sem transcrição está limitada ao [ADR-047](ADR-047-ingestao-inicial-midias-triagem.md);
  o uso operacional do S3 e a transcrição continuam sujeitos aos gates deste
  ADR e a autorização separada.

## Agentes envolvidos

- Product Owner
- `orchestrator`
- `software-architect`
- `aws-architect`

## Data

2026-10-20

## Relações

- Complementa o [ADR-045](ADR-045-retencao-anexos-e-transcricao-audios-triagem.md)
  e respeita o [ADR-044](ADR-044-retencao-mensagens-inbound.md).
- Relaciona-se à
  [arquitetura da triagem WhatsApp Fase 1](../architecture/triagem-whatsapp-fase-1-arquitetura.md).
- A implementação inicial de ingestão é delimitada pelo
  [ADR-047](ADR-047-ingestao-inicial-midias-triagem.md).
