# RF-009 - Coleta Inicial

Status: `Requisitos definidos - com decisões pendentes`

## Objetivo

Executar a primeira coleta real de ordens de servico de um tenant no iService a
partir do `ImmediateCollectionCommand` criado pela ativacao (RF-008), registrando
o estado atual e exatamente uma observacao inicial de cada OS retornada, e
iniciando o historico observado pelo ATUA (RF-010).

## Escopo

O RF-009 e o gate do Coletor: e nele que passa a existir acesso real ao iService.

O fluxo abrangido e:

```
claim -> autenticacao CAS -> consulta dos 5 status suportados ->
enriquecimento das OS Designadas -> persistencia (estado atual +
observacao inicial por OS) -> complete (Succeeded | Failed | Cancelled)
```

O Office reflete o estado do comando em tempo real durante todo o processo.

### O que NAO e RF-009

- Coleta recorrente (RF-011).
- Atualizacao de estado de OS ja existente (RF-011).
- Interpretacao de ausencia de OS (RF-012).
- Escrita no iService (RF-013).
- Exibicao de OS na interface do Office (RF futuro).

### Fronteira critica com RF-010

As datas que o iService retorna sobre a OS (por exemplo, data de abertura) sao
metadados da OS no provedor, **nao observacoes historicas do ATUA**. O historico
registrado pelo ATUA comeca no `capturedAt` da coleta inicial. Essa distincao nao
estava articulada na documentacao existente e esta sendo definida neste requisito.

## Requisitos funcionais

### RF-009.1 - Acionamento via claim

A coleta inicial somente pode ser iniciada pelo Worker via `claim` do
`ImmediateCollectionCommand` criado na ativacao (RF-008). Nao existe atalho
direto pelo Office.

### RF-009.2 - Verificacao de elegibilidade antes do acesso ao iService

Imediatamente antes de qualquer acesso ao iService, o Worker deve verificar a
elegibilidade vigente do tenant. Se a elegibilidade for negada, o comando deve
ser concluido como `Cancelled` sem que nenhuma coleta seja realizada.

### RF-009.3 - Status suportados

Somente os cinco status suportados sao coletados. OS do iService em outros status
sao ignoradas sem geracao de erro.

Os cinco status suportados sao:

1. Designado
2. Em Processamento
3. Pendente
4. Concluido
5. Cancelado

Para OS no status **Designado**, o Worker deve realizar enriquecimento adicional
via `queryOneWorkOrder` para obter dados complementares.

### RF-009.4 - Modo somente leitura

O Worker opera exclusivamente no modo de leitura. Nenhuma escrita, nenhuma
atualizacao e nenhuma interacao que altere dados no iService e permitida. Esse
bloqueio nao e configuravel.

### RF-009.5 - Identidade das OS

Cada OS possui:

- `providerOrderId`: identificador externo proveniente do iService.
- UUID interno do ATUA (UUIDv7): gerado pelo proprio sistema e mantido separado
  do identificador externo.

Os dois identificadores nao devem ser misturados nem usados como substitutos um
do outro.

### RF-009.6 - Protecao de credenciais

Credenciais do iService e dados de sessao CAS sao **proibidos** em:

- logs;
- eventos de dominio;
- documentos do MongoDB;
- payloads de comunicacao interna;
- respostas de API.

### RF-009.7 - Retencao de dados

OS sao preservadas enquanto a conta do tenant estiver ativa. Apos cinco anos de
inatividade da conta ou mediante solicitacao formal, os dados devem ser removidos.

### RF-009.8 - Referencia temporal

O campo `capturedAt` registra o instante exato da coleta no Worker. Esse instante
e a referencia historica do ATUA para aquela OS. Nao e permitido retroagir datas
nem fabricar observacoes anteriores ao `capturedAt`.

### RF-009.9 - Idempotencia da persistencia

O mesmo `commandId` ou o mesmo `providerOrderId` nao devem gerar documentos
duplicados no MongoDB. A persistencia deve ser idempotente.

### RF-009.10 - Exclusividade do claim

Um comando no estado `Claimed` pertence exclusivamente ao Worker que o
reivindicou. Nenhum outro Worker ou processo pode processar ou reivindicar o
mesmo comando enquanto ele estiver nesse estado.

## Regras de negocio

| Numero    | Regra                                                                                                              |
|-----------|--------------------------------------------------------------------------------------------------------------------|
| RN-009.1  | Coleta so comeca via `claim` de um `ImmediateCollectionCommand`; sem atalho pelo Office.                          |
| RN-009.2  | Elegibilidade verificada imediatamente antes do acesso ao iService; negativa resulta em `Cancelled` sem coleta.   |
| RN-009.3  | Somente os 5 status suportados sao coletados; outros status do iService sao ignorados.                            |
| RN-009.4  | Modo estritamente somente leitura; bloqueio nao e configuravel.                                                    |
| RN-009.5  | OS possui `providerOrderId` externo e UUIDv7 interno do ATUA, mantidos separados.                                 |
| RN-009.6  | Credenciais e sessao CAS sao proibidas em logs, eventos, MongoDB, payloads e respostas.                           |
| RN-009.7  | OS preservadas enquanto conta ativa; removidas apos 5 anos de inatividade ou solicitacao formal.                  |
| RN-009.8  | `capturedAt` e o instante de referencia historica; retroatividade e fabricacao de datas sao vedadas.             |
| RN-009.9  | Persistencia idempotente: mesmo `commandId` ou `providerOrderId` nao gera duplicatas.                             |
| RN-009.10 | Comando `Claimed` pertence exclusivamente ao Worker que o reivindicou.                                            |

## Criterios de aceite

1. Dado um `ImmediateCollectionCommand` no estado `Pending`, quando o Worker
   realizar o `claim`, entao o comando deve transitar para `Claimed` e nenhum
   outro Worker deve poder reivindicar o mesmo comando.

2. Dado um comando `Claimed`, quando a elegibilidade do tenant for negada antes
   do acesso ao iService, entao o comando deve ser concluido como `Cancelled` sem
   que qualquer OS seja consultada ou persistida.

3. Dado um comando `Claimed` com elegibilidade valida, quando o Worker consultar
   o iService, entao somente OS nos cinco status suportados devem ser processadas;
   OS em outros status devem ser ignoradas sem erro.

4. Dado um comando `Claimed` com elegibilidade valida, quando o Worker processar
   OS no status Designado, entao deve realizar enriquecimento adicional via
   `queryOneWorkOrder` antes de persistir.

5. Dado que o Worker persistira uma OS, quando a persistencia for executada,
   entao deve ser gravado exatamente um documento de estado atual e exatamente uma
   observacao inicial com `capturedAt`, sem retroagir datas.

6. Dado que o mesmo `providerOrderId` ja existe no MongoDB para o tenant, quando
   o Worker tentar persistir novamente, entao nenhuma duplicata deve ser criada.

7. Dado que a coleta foi concluida com sucesso, quando o Worker chamar `complete`,
   entao o comando deve transitar para `Succeeded`.

8. Dado que ocorreu falha irrecuperavel durante a coleta, quando o Worker chamar
   `complete`, entao o comando deve transitar para `Failed`.

9. Dado qualquer etapa da coleta, quando credenciais ou dados de sessao CAS
   estiverem presentes, entao eles nao devem aparecer em logs, eventos, MongoDB,
   payloads ou respostas.

10. Dado um usuario autorizado consultando o Office durante a coleta, quando o
    estado do comando mudar, entao o Office deve refletir o estado atualizado em
    tempo real, sem expor segredos.

## Decisoes pendentes

> **ATENCAO:** As decisoes listadas abaixo aguardam resposta do usuario (D1, D3,
> D5, D6, D7, D8) ou do software-architect (D2, D4, D9). A implementacao **NAO
> deve prosseguir** sobre os pontos afetados por cada decisao sem que a resposta
> correspondente tenha sido registrada.

---

### D1 - Recorte temporal: todas as OS ou janela de datas?

**Situacao:** A documentacao indica "estado atual de todas as OS retornadas nos
status suportados", o que implica ausencia de filtro de data. Para um cliente com
anos de historico no iService, isso pode retornar dezenas de milhares de OS na
primeira coleta.

**Impacto se nao decidido:** o Worker nao sabe se deve aplicar filtro de data.

**Recomendacao da analista:** confirmar que a coleta inicial busca **todas as OS
retornadas pelo iService nos 5 status, sem filtro de data**, e que a paginacao e
tratada pelo Worker.

**Aguarda:** decisao do usuario.

---

### D2 - Timeout de re-claim: o que acontece se o Worker morrer com o comando em `Claimed`?

**Situacao:** ADR-020 define a maquina de estados mas nao especifica timeout. Um
comando `Claimed` orfao bloqueia indefinidamente a integracao, pois o indice
parcial impede novo `Pending`.

**Impacto se nao decidido:** o tenant fica travado e o Agente Coletor nunca
conclui a coleta inicial.

**Recomendacao da analista:** definir um **timeout de `Claimed`** (sugestao: 30
minutos). Apos o timeout, a Master API ou um job de reconciliacao transita o
comando para `Failed`, permitindo nova ativacao. A escolha entre polling e job e
decisao de arquitetura.

**Aguarda:** decisao do software-architect.

---

### D3 - Quantas tentativas sao permitidas para o Worker (`AttemptCount`)?

**Situacao:** A entidade `ImmediateCollectionCommand` ja possui `AttemptCount`
(ADR-020/RF-008), mas o numero maximo de tentativas nao foi definido.

**Impacto se nao decidido:** sem limite, o Worker pode entrar em loop; com limite
muito baixo, uma falha transitoria cancela definitivamente a coleta inicial.

**Recomendacao da analista:** **1 tentativa** para a coleta inicial (e um comando
unico, nao recorrente). Falha resulta em `Failed`; o cliente pode desativar e
reativar para tentar novamente. O numero exato e decisao de produto.

**Aguarda:** decisao do usuario.

---

### D4 - Ao concluir com `Failed`, a Master API deve desativar automaticamente o Agente?

**Situacao:** Se a credencial foi rejeitada definitivamente pelo iService durante
a coleta, o `ValidationStatus` deveria ser alterado para `Failed`, o que
acionaria `ReconcileEligibility` e desativaria o Agente. Porem, `ReconcileEligibility`
nao esta implementado (registrado como prioridade posterior em ADR-020/RF-008).

**Impacto se nao decidido:** o Agente pode permanecer `Active` com coleta em
estado `Failed`, criando inconsistencia operacional visivel no Office.

**Recomendacao da analista:** uma conclusao com `Failed` por credencial rejeitada
**deve** invalidar o `ValidationStatus` e acionar desativacao por
`CredentialNotValidated`. Isso exige que `ReconcileEligibility` seja implementado
como parte de RF-009, nao como prioridade posterior.

**Aguarda:** decisao do software-architect.

---

### D5 - Dados pessoais de tecnicos e clientes finais nas OS: ha LGPD aplicavel?

**Situacao:** OS do iService podem conter nome do tecnico, nome do cliente final,
endereco e telefone. O produto nao definiu se esses dados podem ser persistidos
no MongoDB ou se devem ser omitidos ou pseudonimizados.

**Impacto se nao decidido:** se persistidos sem tratamento, ha risco de
conformidade com a LGPD. Se omitidos, o ATUA perde informacoes operacionais
relevantes.

**Recomendacao da analista:** definir explicitamente quais campos das OS **podem**
ser persistidos no MVP. Sugestao minima: identificadores externos, status e
`capturedAt`, sem PII de tecnico ou cliente final ate que a base legal seja
definida.

**Aguarda:** decisao do usuario.

---

### D6 - Ha limite de OS por coleta inicial?

**Situacao:** A documentacao `mvp-onboarding` registra como pendencia o
comportamento acima de 10.000 OS por status. O produto nao definiu se existe um
teto.

**Impacto se nao decidido:** sem limite, uma coleta inicial de um cliente grande
pode durar horas, consumir memoria excessiva e potencialmente sobrecarregar o
iService do cliente.

**Recomendacao da analista:** definir um **limite de OS por coleta no MVP**
(sugestao: 1.000 OS por status, 5.000 no total). Se excedido, a coleta deve ser
concluida com as OS obtidas ate o limite, registrando uma nota de truncamento, sem
gerar erro. O limite exato e decisao de produto.

**Aguarda:** decisao do usuario.

---

### D7 - Qual identificador externo do iService e a chave de identidade da OS?

**Situacao:** A documentacao menciona `workOrderNo` e `workOrderId` (ADR-004,
mvp-onboarding pendencias) e registra incerteza sobre a estabilidade desses
identificadores em reaberturas, reatribuicoes ou alteracoes no iService.

**Impacto se nao decidido:** se o identificador escolhido nao for estavel, a
coleta pode criar duplicatas ou perder o historico de uma OS reaberta.

**Recomendacao da analista:** confirmar com o usuario qual dos dois (`workOrderNo`
ou `workOrderId`) e a chave primaria de identidade da OS no iService para o MVP,
e qual e o comportamento esperado em caso de reabertura. A descoberta via
Playwright (ADR-004) deve validar isso antes da implementacao.

**Aguarda:** decisao do usuario.

---

### D8 - O que fazer se o mesmo `providerOrderId` aparece em mais de um status na mesma coleta?

**Situacao:** O iService pode retornar a mesma OS em estados diferentes durante a
janela da coleta (por exemplo, em caso de race condition). O produto nao definiu
o comportamento esperado nessa situacao.

**Impacto se nao decidido:** duplicatas de estado atual ou historico inconsistente.

**Recomendacao da analista:** o Worker deve usar o **status mais recente** com
base na ordem de consulta (Designado, Em Processamento, Pendente, Concluido,
Cancelado) e registrar apenas uma observacao inicial. O criterio de desempate
exato e decisao de produto.

**Aguarda:** decisao do usuario.

---

### D9 - Como o Worker recebe as credenciais decifradas do iService em runtime?

> **Esta e a decisao de maior criticidade tecnica. E o bloqueador mais critico
> de RF-009. A implementacao NAO deve prosseguir sem que um ADR de arquitetura e
> seguranca seja aprovado para este ponto.**

**Situacao:** ADR-004 define que credenciais sao cifradas com AES-256-GCM por
chave de dados por integracao, com KMS externo. ADR-020 menciona "credencial
opaca vinculada a tenant, integracao e provedor", mas o **mecanismo concreto de
entrega ao Worker** (endpoint dedicado, Secrets Manager, variavel de ambiente
injetada no deployment ou outro mecanismo) nao foi definido.

**Impacto se nao decidido:** sem esse mecanismo, o Worker nao pode executar a
coleta. Nenhum acesso real ao iService e possivel.

**Recomendacao da analista:** esta e decisao de **arquitetura e seguranca**
(software-architect e aws-architect), nao de produto. O requisito de produto e:
o Worker recebe as credenciais apenas em tempo de execucao, isoladas por
tenant/integracao, sem que aparecam em logs ou payloads. O mecanismo concreto
deve ser definido em ADR antes de qualquer implementacao.

**Aguarda:** decisao do software-architect e, se aplicavel, do aws-architect.

---

## Dependencias

- RF-008 (implementado): cria o `ImmediateCollectionCommand` consumido por este
  requisito.
- ADR-020: define a maquina de estados do comando (endpoints `claim` e `complete`
  ainda nao implementados).
- ADR-003: define Worker Service e isolamento por tenant.
- ADR-004: define credenciais cifradas e KMS; mecanismo de entrega ao Worker
  pendente (D9).
- ADR-017: credencial de servico interno; limites do contrato de elegibilidade.
- RF-010: historico de observacoes; a fronteira entre metadados do iService e
  observacoes do ATUA esta definida na secao de escopo deste documento.

## Impactos

- Requer implementacao dos endpoints `claim` e `complete` na Master API.
- Requer implementacao do Worker Service .NET com Playwright para acesso ao
  iService.
- Requer mecanismo de entrega segura de credenciais ao Worker (D9 — arquitetura
  pendente, bloqueador critico).
- `ReconcileEligibility` pode entrar no escopo deste requisito dependendo da
  decisao de D4.
- Inicia o volume de dados de OS no MongoDB do tenant.

## Fora do escopo

- Coleta recorrente (RF-011).
- Atualizacao de OS ja existentes (RF-011).
- Interpretacao de ausencia de OS (RF-012).
- Escrita no iService (RF-013).
- Exibicao de OS no Office (RF futuro).
- Aprovacao de implementacao ou validacao de QA.

## Gate de implementacao

RF-009 nao pode ser implementado sem que:

1. D9 (mecanismo de entrega de credenciais ao Worker) esteja definido em ADR
   aprovado pelo software-architect e, se aplicavel, pelo aws-architect.
2. Os endpoints `claim` e `complete` da Master API estejam implementados
   (pendencia de ADR-020).
3. As decisoes de produto D1, D3, D5, D6, D7 e D8 tenham sido respondidas pelo
   usuario.
4. As decisoes arquiteturais D2 e D4 tenham sido respondidas pelo software-architect.
