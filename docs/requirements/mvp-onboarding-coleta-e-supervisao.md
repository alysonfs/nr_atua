# MVP - Onboarding, coleta e supervisao

## Objetivo

Permitir que um novo cliente crie sua conta Trial, conecte o ATUA ao iService
de forma segura e acompanhe o estado atual e as mudancas observadas nas ordens
de servico (OS).

## Escopo

O MVP cobre cadastro, confirmacao de e-mail, Trial, acesso ao Office,
configuracao e ativacao do Agente Coletor, exibicao de OS e supervisao por
superadministracao. A arquitetura aplicavel esta definida na ADR-003.

## Requisitos funcionais

### Cadastro e Trial

#### RF-001 - Cadastro de cliente

O visitante deve poder cadastrar-se como cliente com e-mail, senha e
confirmacao da senha. O e-mail deve ser unico no sistema. Dados empresariais,
incluindo CNPJ, nao devem ser solicitados durante o cadastro Trial.

#### RF-002 - Confirmacao de e-mail

O sistema deve enviar uma confirmacao para o e-mail informado e exigir sua
conclusao antes de liberar acesso as areas restritas.

#### RF-003 - Plano Trial

Ao confirmar o codigo recebido por e-mail, o cliente deve receber o plano Trial
com validade ate o fim do setimo dia contado em UTC. Enquanto o e-mail nao for
confirmado, o prazo do Trial nao deve iniciar.

#### RF-004 - Preferencia de fuso horario

O cliente deve possuir um fuso horario no formato IANA. O valor inicial deve
ser sugerido pelo idioma selecionado e o usuario deve poder altera-lo nas
preferencias da sessao. Datas devem ser armazenadas em UTC e apresentadas no
fuso horario selecionado.

### Office e integracao iService

#### RF-005 - Acesso ao Office

O cliente confirmado deve poder autenticar-se no Office por e-mail e senha e
acessar somente os dados do seu tenant.

#### RF-006 - Configuracao de credenciais iService

No menu de configuracoes do Office, o cliente deve poder informar as
credenciais necessarias para a integracao com o iService.

#### RF-007 - Validacao antes da ativacao

O sistema deve testar as credenciais obtendo uma sessao aceita pelo iService.
O controle para ativar o Agente Coletor deve permanecer indisponivel enquanto
nao houver uma validacao bem-sucedida.

#### RF-008 - Ativacao do Agente Coletor

O Agente Coletor deve iniciar desativado. Depois de credenciais validadas, o
cliente pode ativa-lo pelo Office. A ativacao deve solicitar uma coleta
imediata e iniciar a coleta recorrente configurada, com valor inicial de 15
minutos.

### Coleta e OS

#### RF-009 - Coleta inicial

A primeira coleta deve registrar o estado atual de todas as OS retornadas nos
status suportados pelo iService e iniciar o historico observado pelo ATUA.

#### RF-010 - Historico observado

O historico deve conter apenas estados observados pelo ATUA a partir da primeira
coleta. O sistema nao deve fabricar ou atribuir eventos anteriores a ela.

#### RF-011 - Atualizacao de estado e transicoes

O ATUA deve manter uma OS atual para cada OS identificada no iService. Cada
coleta posterior deve atualizar seu estado atual observado e acrescentar ao
historico da mesma OS uma observacao quando o status mudar.

Ao expandir uma OS, o cliente deve visualizar as observacoes historicas em
ordem cronologica. Por exemplo, se a OS 123 estiver Designada as 08:00 e Em
Processamento as 08:15, a OS atual deve exibir Em Processamento e seu historico
deve exibir ambas as observacoes com os respectivos instantes.

No MVP, os estados de OS suportados sao Designado, Em Processamento, Pendente,
Concluido e Cancelado. O estado Em Processamento corresponde ao valor tecnico
`accepted` do iService. A expressao "sessao ativa" refere-se a saude da sessao
do Agente Coletor e nao e um estado de OS.

#### RF-012 - Ausencia de OS

A ausencia de uma OS em uma coleta nao deve ser interpretada automaticamente
como cancelamento, conclusao ou exclusao.

#### RF-013 - Modo somente leitura

O Agente Coletor deve operar somente em leitura e nao pode aceitar, reatribuir
ou executar outra operacao de escrita no iService.

### Manager

#### RF-014 - Acesso de superadministracao

O superadministrador deve autenticar-se no Manager por e-mail e senha. Apenas
usuarios com essa permissao `ROOT` podem acessar a lista de clientes.

#### RF-015 - Supervisao de clientes

O Manager deve permitir ao superadministrador visualizar, para cada cliente,
o plano Trial e sua validade, o resultado e instante da ultima validacao do
iService e o estado atual do Agente Coletor.

#### RF-016 - Gestao do plano pelo superadministrador

O superadministrador com permissao `ROOT` deve poder estender, renovar ou
converter manualmente o plano de um cliente. Essas acoes devem preservar o
tenant e o historico coletado.

#### RF-017 - Visibilidade do bloqueio de escrita

O Manager deve informar que o modo de escrita no iService esta bloqueado. O
superadministrador apenas visualiza esse estado e nao pode habilitar, desabilitar
ou enviar acoes de escrita ao iService.

### Landing

#### RF-018 - Jornada publica

A Landing deve apresentar o ATUA e disponibilizar navegacao para login e
cadastro no Office.

## Requisitos de seguranca

### RS-001 - Protecao de segredos

Senhas, credenciais do iService, cookies de sessao, tokens e chaves nao podem
ser enviados a logs, snapshots, respostas de API, builds frontend ou telas do
Manager. O Manager exibe apenas o estado operacional da integracao.

### RS-002 - Isolamento de tenant

Cliente, configuracao iService, Agente Coletor, OS e seus eventos devem ser
isolados por tenant.

### RS-003 - Retencao de dados

Os dados de cliente e OS devem ser preservados enquanto a conta permanecer
ativa. Eles devem ser removidos quando o cliente solicitar exclusao ou quando
a conta permanecer inativa por mais de cinco anos.

## Criterios de aceite

1. Dado um e-mail elegivel e senha valida, quando o visitante concluir o
   cadastro, entao deve receber uma confirmacao de e-mail e permanecer sem
   acesso as areas restritas ate confirma-la.
2. Dado um e-mail confirmado, quando a conta for ativada, entao o cliente deve
   receber Trial valido ate o fim do setimo dia contado em UTC.
3. Dadas credenciais iService nao validadas, quando o cliente acessar a
   configuracao, entao o controle de ativacao do Agente Coletor deve estar
   indisponivel.
4. Dadas credenciais iService validas, quando a sessao for obtida com sucesso,
   entao o controle de ativacao deve ser liberado sem expor dados sensiveis.
5. Dado um Agente Coletor ativado, quando ocorrer sua ativacao, entao uma
   coleta imediata deve ser solicitada; os ciclos posteriores devem usar o
   intervalo configurado.
6. Dada uma primeira coleta bem-sucedida, quando o iService retornar OS nos
   status suportados, entao cada OS retornada deve ter uma observacao inicial e
   estado atual registrados.
7. Dadas duas coletas com status diferente para a mesma OS, quando a segunda
   coleta for processada, entao o estado atual deve ser atualizado e o
   historico expandido da mesma OS deve exibir ambas as observacoes sem
   duplicacao.
8. Dado um superadministrador autenticado, quando listar clientes, entao deve
   visualizar Trial, validade operacional da integracao e estado do Agente
   Coletor sem visualizar qualquer segredo.
9. Dado um superadministrador autenticado, quando acessar o Manager, entao deve
   visualizar que a escrita no iService esta bloqueada e nao deve encontrar
   controle capaz de alterar esse bloqueio.
10. Dado um Trial expirado, quando sua validade terminar, entao o Agente
   Coletor deve ser desativado e os dados e historico devem continuar visiveis
   ao cliente e ao Manager.
11. Dado um superadministrador `ROOT`, quando estender, renovar ou converter o
   plano de um cliente, entao o tenant e seu historico devem ser preservados.
12. Dado um Trial ativo, quando faltarem dois dias e depois doze horas para seu
   termino, entao o cliente deve receber avisos de expiracao.
13. Dado um visitante da Landing, quando desejar entrar no produto, entao deve
   encontrar caminhos para login e cadastro no Office.

## Fora do escopo do MVP

- Cobranca ou conversao automatica apos o Trial.
- Coleta de CNPJ e demais dados empresariais, que ocorrera somente na
   contratacao do plano mensal.
- Recuperacao de senha e reenvio de confirmacao de e-mail.
- Multiplos usuarios por cliente.
- Login federado por Google ou Meta.
- RAG, LLMs, embeddings e busca vetorial para usuarios finais.
- Coleta ou transcricao de materiais de treinamento do iService.
- Recursos de supervisao no aplicativo Tecnica.

## Pendencias de descoberta

- A garantia de que a consulta do iService retorna todas as OS sem filtros ou
   limites implicitos, incluindo a paginacao por status e o comportamento acima
   de 10.000 OS por status.
- A estabilidade do par `workOrderNo` e `workOrderId` em reaberturas,
   reatribuicoes ou alteracoes feitas no iService.
