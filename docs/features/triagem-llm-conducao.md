# Triagem – Condução da conversa pela LLM (B2, B4, B5, B6)

Status: Proposta (fase 1). A especificação não depende de fornecedor.
Épico: [triagem-whatsapp-fase-1.md](./triagem-whatsapp-fase-1.md).
Arquitetura: [triagem-whatsapp-fase-1-arquitetura.md](../architecture/triagem-whatsapp-fase-1-arquitetura.md)
(§2.5 e §7).

Este documento não define prompts nem modelo de dados. Fornecedor:
[ADR-048](../decisions/ADR-048-llm-bedrock-sa-east-1-cobranca-por-tenant.md).
Tom de voz: [triagem-llm-identidade-tom.md](./triagem-llm-identidade-tom.md).

## 1. Papel da LLM

A LLM pode:

- conduzir a coleta dos itens do checklist com o consumidor (B2);
- interpretar a descrição livre e propor campos estruturados (B4);
- indicar o que ainda falta e redigir a próxima mensagem dentro do roteiro.

A LLM não pode:

- alterar o estado da triagem, classificar SubStatus, agendar, cancelar ou
  concluir uma OS;
- sugerir peças (B5) antes de existir um catálogo confiável. A sugestão fica
  desabilitada;
- falar sobre assuntos fora da triagem, prometer prazos ou dar orientação
  técnica de reparo;
- continuar respondendo depois que um operador assume a triagem.

A Master API é a única autoridade. A LLM só **propõe**, e a API **valida e
decide**.

## 2. Contrato abstrato

A Master API usa uma interface que não depende de fornecedor, por exemplo
`ITriagemLlmClient`. O fornecedor fica atrás de um adaptador configurável.

**Entrada mínima**, seguindo o princípio de minimização da LGPD:

- itens do checklist já atendidos e itens pendentes;
- histórico recente da conversa, só em texto, limitado a uma janela;
- dados da OS estritamente necessários, como produto e marca.

Não são enviados CPF, telefone, endereço completo nem nome, salvo se forem
indispensáveis. As mídias são enviadas apenas como referência temporária, e
só quando a tarefa exigir.

**Saída estruturada**, validada contra um esquema:

- `respostaAoConsumidor`: texto da próxima mensagem;
- `camposEstruturados`: tipo de defeito, sintoma, código de erro e
  categoria, cada um com o grau de confiança;
- `itensAtendidos`: itens do checklist que a LLM considera atendidos, com a
  evidência (mensagem ou anexo);
- `sinalDesvio`: indica que a conversa saiu do escopo ou que o consumidor
  pediu atendimento humano.

Quando a saída não segue o esquema, ela é descartada e conta como tentativa
inválida. O texto sem validação nunca é enviado ao consumidor.

## 3. Checklist e suficiência (B6)

| Item | Obrigatório | Atendido quando |
|---|---|---|
| Descrição do defeito | Sim | Texto ou transcrição com sintoma identificável |
| Vídeo/foto do defeito | Não | Mídia opcional — nunca bloqueia a suficiência; não é interpretada pela LLM nem por visão computacional |
| Nota fiscal (imagem ou PDF) | Sim | Anexo recebido e classificado como nota fiscal legível no Bedrock (regra determinística); texto do consumidor não valida o documento |
| Foto da etiqueta (modelo e série) | Sim | Imagem recebida e classificada como legível, com modelo e número de série extraídos pela visão computacional no Bedrock (regra determinística) |
| Endereço completo | Sim | Logradouro, número, bairro e cidade identificados |
| Ponto de referência | Não | Texto recebido |
| Localização | Não | Localização compartilhada no WhatsApp ou link de mapa |

A suficiência é **determinística**: a API move a triagem para
`INFORMACOES_SUFICIENTES` e depois para `LIBERADA_AO_POOL` quando todos os
itens obrigatórios estão atendidos. A marcação da LLM é um insumo, e a
decisão é da API. O operador confere o resultado no painel C2.

Decisão do PO (adiantar a visão computacional no Bedrock): quando uma mídia
de imagem (jpeg/png/webp) ou um PDF chega numa triagem elegível, ela é classificada
automaticamente — antes da chamada de conversa do mesmo turno — como nota
fiscal, etiqueta ou outra imagem, com legibilidade e, no caso da etiqueta,
modelo e número de série. Quem decide marcar os itens `NotaFiscal` e
`Etiqueta` como atendidos é uma regra determinística (nunca a LLM
conversacional), a partir desse resultado. Imagens acima do limite de
tamanho do Bedrock (~3,75 MiB) ou com falha do fornecedor ficam pendentes
— sem quebrar o fluxo nem levar a triagem a `AGUARDANDO_INTERVENCAO` só por
isso — e são retentadas em um turno futuro. Quando a imagem é classificada
como ilegível, a LLM recebe apenas esse fato (nunca a imagem) e pede uma
nova foto mais nítida. Vídeo e áudio nunca são enviados ao
classificador; documentos PDF são enviados nativamente ao Converse com
citações habilitadas para leitura visual, inclusive de páginas digitalizadas.
O limite adotado para PDF é 4.500.000 bytes (o limite de ingestão/armazenamento
continua 16 MiB). PDF inválido, protegido ou rejeitado pelo fornecedor não
valida nota fiscal; o item permanece pendente e a conversa continua.
Não há verificação de autenticidade ou consulta fiscal.

Texto **e mídia armazenada** acionam o mesmo loop conversacional quando a
triagem está em coleta e dentro da allowlist. O evento de mídia e sua
legenda entram no histórico recente como turno do consumidor, sem criar um
evento adicional de mensagem de texto. A chave da resposta usa o ID interno
persistido da mídia: reentregas idênticas não repetem classificação ou
resposta já gerada. A foto do defeito pode ser classificada como `Outro`;
isso não interrompe a conversa nem satisfaz nota fiscal/etiqueta. Vídeo e
áudio não são interpretados.

Chamadas de visão entram no mesmo custo por tenant e orçamento da triagem,
reservando ao menos uma chamada para a resposta conversacional do turno.

Validação técnica em 24/10/2026: usando o adaptador real e o perfil global
Haiku 4.5 escolhido pelo PO, uma nota sintética em PNG e a mesma imagem
embutida em PDF (sem camada de texto) foram classificadas como `NotaFiscal`
legível. A chamada PNG consumiu 2.924 tokens de entrada e 76 de saída;
a PDF, 3.463 e 76. Custo estimado conjunto: USD 0,007147, fora do ledger
de tenants por serem probes sintéticos sem triagem persistida. Isso valida
o caminho técnico de imagem/PDF digitalizado, não a autenticidade da nota
nem a entrega WhatsApp. O adaptador usa AWS SDK BedrockRuntime 4.0.100
para expor a configuração de citações de documentos do Converse.

Ressalva de confiabilidade (smoke test manual pós-implementação, etiqueta
sintética): a classificação de tipo/legibilidade e a extração de modelo e
número de série são probabilísticas e sujeitas a erro (ex.: dígito a mais
ou a menos no número de série) — não são uma leitura determinística de
OCR nem equivalem a validação fiscal, autenticidade do produto ou garantia.
A conferência humana pelo operador no painel C2 continua obrigatória antes
de qualquer decisão que dependa desses dados; a marcação automática do
checklist acelera a triagem, mas não substitui essa conferência.

## 4. Guardrails e interrupção

- Limite de tentativas inválidas por item: 3, conforme a arquitetura §7.
- Limite de saídas fora do esquema ou de desvios consecutivos: a definir,
  com proposta de 3.
- Ao atingir qualquer um dos limites, ou quando o consumidor pede
  atendimento humano, a triagem vai para `AGUARDANDO_INTERVENCAO`, a LLM
  para e a triagem aparece no pool.
- Se o fornecedor estiver indisponível ou exceder o tempo limite, há até
  2 novas tentativas com backoff exponencial (3 chamadas no total). Se a
  falha persistir, a triagem vai para `AGUARDANDO_INTERVENCAO`. O
  consumidor nunca fica sem resposta por tempo indefinido.
- Quando a triagem chega a `ASSUMIDA_POR_OPERADOR`, `EXPIRADA` ou
  `FALHA_DE_CONTATO`, a API não chama mais a LLM.

## 5. Auditoria, custo e LGPD

- Toda chamada registra triagem, versão do roteiro, fornecedor e modelo,
  tokens de entrada e saída, latência, resultado da validação e decisão
  aplicada pela API.
- Haverá orçamento por tenant e por triagem: limite de chamadas e de tokens.
  Quando o orçamento é excedido, a triagem vai para `AGUARDANDO_INTERVENCAO`.
- O fornecedor deve ter contrato ou DPA, não reter nem treinar com os dados
  e informar a região de processamento. Isso é bloqueio de produção.
- Os campos propostos pela LLM ficam marcados como "sugeridos pela IA" no
  painel. A edição pelo operador é auditada, conforme a arquitetura §2.6.

## 6. Critérios de aceite

1. A LLM só é chamada pela Master API e com a triagem em `CONTATO_INICIADO`
   ou `EM_COLETA`.
2. Uma saída fora do esquema não gera mensagem ao consumidor e é contada
   como tentativa inválida.
3. A triagem só é liberada ao pool quando todos os itens obrigatórios estão
   atendidos, com verificação feita pela API.
4. Depois de 3 tentativas inválidas do mesmo item, a triagem vai para
   `AGUARDANDO_INTERVENCAO`.
5. Quando o consumidor pede atendimento humano, a triagem vai para
   `AGUARDANDO_INTERVENCAO`.
6. Depois da assunção, nenhuma chamada à LLM é feita para aquela triagem.
7. Os campos estruturados aparecem no painel com a marcação "sugerido pela
   IA" e podem ser editados com auditoria.
8. O payload enviado ao fornecedor não contém CPF nem telefone.
9. Cada chamada fica registrada com o consumo de tokens e o resultado.
10. Trocar de fornecedor exige só configuração e um adaptador, sem mudar o
    caso de uso.

## 7. Pendências (decisão do PO e da arquitetura)

- ~~Fornecedor da LLM~~: **decidido**. Amazon Bedrock em `sa-east-1`, com
  cobrança por tenant
  ([ADR-048](../decisions/ADR-048-llm-bedrock-sa-east-1-cobranca-por-tenant.md)).
  Falta escolher o modelo nativo na região e verificar o plano da conta.
- Identidade e tom de voz: [triagem-llm-identidade-tom.md](./triagem-llm-identidade-tom.md).
- Parecer jurídico e LGPD.
- Estimativa de custo por triagem e orçamento mensal por tenant.
- Catálogo de peças e sintomas por fabricante e produto, pré-requisito do
  B5.
- ~~Avaliação da legibilidade de imagens (nota fiscal, etiqueta com modelo e
  série)~~: **implementado**. Visão computacional no Bedrock classifica
  automaticamente nota fiscal e etiqueta a cada turno elegível (decisão do
  PO de adiantar esta capacidade, antes do motor JEV completo); ver §3. O
  vídeo/foto do defeito continua opcional e nunca é interpretado.
- Transcrição de áudio, adiada (ADR-046). Enquanto ela não existir, o áudio
  não conta como descrição atendida.
