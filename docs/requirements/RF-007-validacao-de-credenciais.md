# RF-007 - Validação de credenciais antes da ativação

Status: `Pendente`

## Objetivo

Garantir que o Agente Coletor não possa ser ativado enquanto as credenciais
iService cadastradas não tiverem sido comprovadamente aceitas pelo
provedor, por meio de um teste de autenticação que não persiste sessão de
trabalho nem executa qualquer ação de coleta.

## Escopo

Teste de credenciais (obtenção de uma sessão válida junto ao iService),
tratamento de falha, controle de liberação da ativação do Agente Coletor e
proteção de segredos nas respostas. Não inclui execução de coleta,
scraping de OS ou qualquer automação além da tentativa de autenticação
necessária para validar a credencial.

## Requisitos funcionais

### RF-007.1 - Teste de validação

Após o cadastro ou alteração de credenciais (RF-006), o sistema deve
permitir/executar uma validação que tenta obter uma sessão aceita pelo
iService usando as credenciais informadas, sem persistir essa sessão como
sessão de trabalho de coleta.

### RF-007.2 - Bloqueio de ativação até validação bem-sucedida

O controle de ativação do Agente Coletor deve permanecer indisponível
enquanto não houver uma validação com resultado de sucesso registrado para
a integração corrente. Este comportamento já está definido no critério de
aceite 4 do documento mestre (`mvp-onboarding-coleta-e-supervisao.md`) e é
detalhado aqui.

### RF-007.3 - Resultado da validação

O sistema deve registrar e expor ao cliente (no Office) e ao
superadministrador (no Manager, RF-015) apenas:

- o resultado da validação (sucesso ou falha);
- o instante da última validação (`evaluatedAtUtc` ou equivalente).

Nenhum dado sensível (senha, cookie de sessão CAS, resposta bruta do
iService) pode ser exposto em nenhuma dessas superfícies (RS-001).

### RF-007.4 - Tratamento de falha

Quando a validação falhar, o sistema deve:

- manter o controle de ativação indisponível;
- informar ao cliente que a validação falhou, sem detalhar a causa técnica
  exata (ex.: não diferenciar "usuário inexistente" de "senha incorreta" do
  provedor terceiro, para não incentivar tentativas de enumeração no
  iService);
- permitir nova tentativa de validação a qualquer momento, sem limite de
  tentativas definido neste requisito (ver decisão mínima abaixo).

### RF-007.5 - Momento de execução da validação

A validação deve ocorrer:

- obrigatoriamente após o cadastro inicial de credenciais (RF-006.2);
- obrigatoriamente após qualquer alteração de credenciais (RF-006.5), já
  que esta reseta o estado para "não validada";
- sob demanda, quando o cliente solicitar explicitamente uma nova
  validação (ex.: botão "testar credenciais" no Office).

Validação periódica automática (sem ação do cliente) **não está incluída**
neste requisito para o MVP — ver ambiguidade abaixo.

## Regras de negócio

- RN-007.1: Nenhuma ativação do Agente Coletor é permitida sem um registro
  de validação com resultado de sucesso para a integração vigente.
- RN-007.2: Qualquer alteração de credenciais invalida o resultado de
  validação anterior (RF-006.5), exigindo nova validação antes de nova
  ativação.
- RN-007.3: A validação não deve executar, direta ou indiretamente,
  qualquer operação de escrita no iService (alinhado a RF-013, modo
  somente leitura).
- RN-007.4: Segredos e sessão obtida durante o teste não podem ser
  persistidos como sessão de trabalho nem expostos em logs ou respostas de
  API (RS-001, ADR-004).

## Casos de borda

- Validação bem-sucedida seguida de alteração de credenciais → estado deve
  voltar a "não validada" imediatamente (decorre de RF-006.5).
- Validação solicitada repetidamente em curto intervalo → decisão mínima
  de MVP: sem limite de taxa definido por este requisito; se necessário,
  rate limiting é decisão técnica de `software-architect`/`aws-architect`.
- Iservice indisponível durante o teste (timeout, erro 5xx do provedor) →
  deve ser tratado como falha de validação, com mensagem que distinga (ao
  menos para fins de suporte/log interno, nunca para o cliente final) falha
  de credencial de indisponibilidade do provedor, sem expor detalhes
  técnicos ao cliente.
- Tentativa de ativar o Agente Coletor sem nenhuma validação prévia
  registrada (nunca validado) → deve ser bloqueada da mesma forma que uma
  validação malsucedida.

## Ambiguidades identificadas (requerem confirmação)

1. **Revalidação periódica automática**: o requisito mestre (RF-007) não
   menciona revalidação periódica, apenas validação "antes da ativação".
   Decisão mínima assumida para o MVP: a validação ocorre apenas nos
   momentos descritos em RF-007.5 (cadastro, alteração, sob demanda). Se o
   produto precisar detectar credenciais que se tornaram inválidas após a
   ativação (ex.: senha do iService trocada externamente), isso exigirá um
   novo requisito e decisão arquitetural — não incluído neste escopo.
2. **Limite de tentativas de validação**: não há regra de negócio definida
   limitando quantas vezes o cliente pode solicitar validação. Assumido
   como ilimitado no MVP, sujeito a controles técnicos de rate limiting a
   critério da arquitetura, se necessário por abuso ou custo.

## Critérios de aceite

1. Dadas credenciais recém-cadastradas, quando o cliente solicitar a
   validação, então o sistema deve tentar obter uma sessão junto ao
   iService e registrar o resultado sem persistir essa sessão como sessão
   de trabalho.
2. Dada uma validação com resultado de sucesso, quando o cliente acessar a
   configuração, então o controle de ativação do Agente Coletor deve estar
   disponível.
3. Dada uma integração nunca validada ou com última validação falha,
   quando o cliente acessar a configuração, então o controle de ativação
   deve permanecer indisponível.
4. Dada uma validação malsucedida, quando o resultado for exibido ao
   cliente, então nenhum dado sensível (senha, cookie, resposta bruta do
   provedor) deve estar presente na resposta ou em logs.
5. Dada uma integração validada com sucesso, quando as credenciais forem
   alteradas, então o estado de validação deve retornar a "não validado" e
   o controle de ativação deve ser bloqueado novamente até nova validação.
6. Dado um superadministrador no Manager, quando visualizar uma integração,
   então deve ver apenas o resultado e o instante da última validação, sem
   qualquer segredo (RF-015, RS-001).

## Dependências

- RF-006 (credenciais cadastradas e cifradas).
- ADR-004 (invalidação de sessão CAS, proteção de segredos).
- ADR-017 (o Agente Coletor, quando existir, não deve acessar Trial/tenant
  diretamente; a validação aqui descrita é de responsabilidade da Master
  API, não do futuro coletor).

## Impactos

- Bloqueia RF-008 (ativação do Agente Coletor) até validação bem-sucedida.
- Não introduz nenhuma lógica de scraping/automação de coleta — a
  implementação técnica de "obter uma sessão do iService" deve ser
  desenhada pelo `software-architect` respeitando o modo somente leitura
  (RF-013) e sem acoplar-se ao Agente Coletor (hoje fora de escopo/pausado).

## Fora do escopo

- Execução de coleta real de OS.
- Qualquer automação de scraping/Playwright associada ao Agente Coletor
  (explicitamente fora de escopo por decisão do usuário neste momento).
- Revalidação periódica automática sem ação do cliente (ver ambiguidade 1).
