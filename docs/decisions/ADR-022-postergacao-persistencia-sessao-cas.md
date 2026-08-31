# ADR-022 - Postergacao da persistencia de sessao CAS no Worker

## Status

Accepted

## Contexto

Durante a implementacao do RF-009 (Coleta Inicial), identificou-se uma
contradicao entre duas decisoes aprovadas:

- **ADR-004** estabelece que "credenciais iService e estado de sessao CAS
  serao persistidos apenas no PostgreSQL, cifrados antes da gravacao", usando
  envelope encryption (DEK por integracao + CMK no KMS).

- **ADR-021 (D9-B)** estabelece que o Worker/Collector nao faz criptografia,
  nao recebe chave mestra, nao acessa a tabela de credenciais nem o PostgreSQL
  diretamente. Toda comunicacao ocorre via endpoints HTTP `claim`/`complete`
  da API, recebendo credenciais ja decifradas em memoria.

Essas duas decisoes sao mutuamente incompativeis no que diz respeito a sessao
CAS: o ADR-004 preve persistencia cifrada da sessao CAS, mas o unico
componente que produz e consome essa sessao (o Worker) nao tem acesso ao
Postgres nem a infraestrutura de cifra por decisao arquitetural deliberada
(ADR-021 D9-B).

Implementar persistencia de sessao CAS exigiria criar novos endpoints na API
(PUT e GET de sessao CAS), com cifra/decifra na API, trafego de cookies
sensiveis por HTTP, e logica de invalidacao/TTL — complexidade significativa
para um beneficio marginal no estagio atual do projeto.

### Analise de impacto do login CAS a cada ciclo

- **Volume atual:** MVP com poucos tenants ativos (previsao: < 10).
- **Frequencia:** 1 login CAS por ciclo de coleta por integracao ativa.
  `PollingIntervalSeconds` e configuravel (nao hardcoded). Com intervalo
  minimo razoavel de 60 segundos e < 10 integracoes ativas, o iService
  receberia no maximo ~10 logins/minuto — carga negligivel.
- **Custo do login:** uma interacao Playwright com o CAS que leva poucos
  segundos. O overhead e operacional (tempo de coleta), nao de carga no
  iService.
- **Throttling:** o Worker ja possui `PollingIntervalSeconds` configuravel
  como controle de taxa, conforme requisito nao-funcional de nao sobrecarregar
  o iService.

## Decisao

**Nao persistir sessao CAS entre execucoes do Worker por ora.** O Worker
realiza login CAS completo a cada ciclo de coleta. Isso e aceito como debito
tecnico deliberado.

A secao "Credenciais iService e sessao CAS" do ADR-004 permanece como
intencao arquitetural de longo prazo, mas a persistencia de sessao CAS e
formalmente postergada ate que um dos criterios de revisao abaixo seja
atingido.

### Criterios de revisao (qualquer um aciona reavaliacao)

1. **Volume:** mais de 50 integracoes ativas simultaneas.
2. **Rate-limit:** o iService impoe ou sinaliza throttling/bloqueio por excesso
   de logins CAS.
3. **Performance:** o tempo de login CAS passa a representar mais de 40% do
   tempo total de um ciclo de coleta, impactando SLA de frescor dos dados.
4. **Seguranca:** o iService altera o mecanismo CAS de forma que logins
   frequentes gerem alertas ou bloqueios de conta.

Quando qualquer criterio for atingido, a equipe deve revisitar esta decisao e
implementar persistencia de sessao CAS via endpoints internos da API
(opcao (a) descrita no contexto), mantendo o principio do ADR-021 D9-B: o
Worker nunca cifra, nunca acessa Postgres, e entrega/recebe sessao CAS via
HTTP para a API.

### Desenho reservado para implementacao futura (quando revisitado)

Quando necessario, o mecanismo sera:

- `PUT /api/internal/collector/integrations/{id}/session` — Worker entrega
  cookies CAS; API cifra e persiste no Postgres usando infraestrutura
  existente (mesmo envelope encryption de `IServiceCredential`).
- O endpoint `claim` passa a incluir campo opcional `casSession` na resposta,
  com a sessao CAS previamente salva (se existir e nao expirada).
- Worker tenta reutilizar a sessao antes de fazer login novo.
- Invalidacao: sessao CAS e invalidada nos mesmos eventos ja previstos no
  ADR-004 (troca de credencial, validacao falha, desativacao de integracao,
  remocao de tenant).

Este desenho nao sera implementado agora. Serve como referencia para evitar
re-analise quando o momento chegar.

## Motivos

- O MVP tem poucos tenants ativos; o custo de login a cada ciclo e aceitavel.
- Implementar persistencia de sessao CAS agora exigiria novos endpoints,
  logica de cifra de cookies, TTL, invalidacao — complexidade desproporcional
  ao beneficio no estagio atual.
- O ADR-021 D9-B foi uma decisao arquitetural deliberada de simplicidade e
  seguranca (Worker sem acesso a cifra/Postgres). Preserva-la e mais
  importante do que otimizar logins CAS prematuramente.
- `PollingIntervalSeconds` configuravel ja oferece controle de taxa suficiente
  para o volume atual.

## Alternativas consideradas

### (a) Endpoints internos de sessao CAS na API agora

Rejeitada por complexidade desproporcional ao MVP. Seria a solucao correta
quando o volume justificar — fica reservada como desenho futuro.

### (c) Worker acessa Postgres e cifra diretamente

Rejeitada. Viola o ADR-021 D9-B e reintroduz acoplamento entre Worker e
infraestrutura de cifra/persistencia. Aumenta superficie de ataque.

## Consequencias

- O Worker faz login CAS a cada ciclo. Custo operacional aceito.
- `PollingIntervalSeconds` deve ter valor minimo documentado para evitar
  abuso acidental (recomendacao: >= 60 segundos em producao).
- O ADR-004 e atualizado para registrar formalmente esta postergacao,
  sem alterar seu status — a intencao permanece, a implementacao e adiada.
- Quando um criterio de revisao for atingido, a implementacao futura
  seguira o desenho reservado nesta ADR, sem necessidade de nova analise
  arquitetural completa.

## Agentes envolvidos

- Usuario: identificacao do conflito e requisitos.
- software-architect: analise e decisao.

## Data

2026-08-30

## Altera

ADR-004 (adiciona postergacao formal da persistencia de sessao CAS).
Complementa ADR-021 (esclarece que sessao CAS nao e persistida no MVP).
