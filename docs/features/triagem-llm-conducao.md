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
| Vídeo do defeito | Não | Anexo de vídeo recebido |
| Foto da nota fiscal | Sim | Imagem recebida e marcada pelo operador como legível |
| Foto da etiqueta (modelo e série) | Sim | Imagem recebida e marcada pelo operador como legível |
| Endereço completo | Sim | Logradouro, número, bairro e cidade identificados |
| Ponto de referência | Não | Texto recebido |
| Localização | Não | Localização compartilhada no WhatsApp ou link de mapa |

A suficiência é **determinística**: a API move a triagem para
`INFORMACOES_SUFICIENTES` e depois para `LIBERADA_AO_POOL` quando todos os
itens obrigatórios estão atendidos. A marcação da LLM é um insumo, e a
decisão é da API. O operador confere o resultado no painel C2.

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
- Avaliação da legibilidade de imagens (nota fiscal, etiqueta com modelo e
  série, vídeo do defeito): por visão computacional/Bedrock é evolução
  futura. Hoje o operador confere manualmente a legibilidade; não há
  reconhecimento automático pela LLM.
- Transcrição de áudio, adiada (ADR-046). Enquanto ela não existir, o áudio
  não conta como descrição atendida.
