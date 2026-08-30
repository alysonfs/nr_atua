# RF-006 - Configuração de credenciais iService

Status: `Pendente`

## Objetivo

Permitir que o cliente crie o tenant (nome + CNPJ) e configure as
credenciais de integração com o iService, armazenadas de forma cifrada,
conforme ADR-004 e ADR-005. Este requisito trata exclusivamente de
cadastro e persistência segura de configuração — não inclui qualquer lógica
de scraping, automação ou execução de coleta.

## Escopo

Criação do tenant na primeira configuração de integração, cadastro de
credenciais iService, edição posterior de credenciais e suas
consequências. O Agente Coletor está fora de escopo desta análise.

## Requisitos funcionais

### RF-006.1 - Criação do tenant na primeira integração

Antes da primeira configuração de integração, o cliente com Trial ativo e
sem tenant deve informar nome e CNPJ da empresa. O sistema deve:

- validar o formato do CNPJ;
- validar unicidade global do CNPJ (ADR-005: "CNPJ deve ser validado e ter
  unicidade global antes de criar Tenant");
- criar o Tenant, atribuir membership `OWNER` ao usuário e associar o Trial
  em andamento sem reiniciar sua validade (RF-003, ADR-005).

### RF-006.2 - Cadastro de credenciais iService

Após a existência do tenant (criado em RF-006.1 ou já existente), o membro
`OWNER` deve poder cadastrar as credenciais necessárias para a integração
com o iService através do menu de configurações do Office.

### RF-006.3 - Autorização para configurar

Apenas o membership `OWNER` do tenant pode criar, editar ou remover a
configuração de credenciais iService no MVP. Este requisito decorre de
ADR-004/ADR-005, que definem um único `OWNER` por tenant no MVP; papéis
adicionais (`ADMIN`) existem no modelo de dados, mas suas permissões
específicas sobre integrações não estão definidas neste requisito e ficam
como ambiguidade registrada abaixo.

### RF-006.4 - Persistência cifrada

As credenciais devem ser persistidas exclusivamente cifradas, seguindo
integralmente o mecanismo já decidido em ADR-004 (chave de dados por
integração, AES-256-GCM, chave externa via AWS KMS). Nenhuma credencial
pode ser retornada em claro em qualquer resposta de API após o cadastro.

### RF-006.5 - Alteração de credenciais

Quando as credenciais forem alteradas, o sistema deve:

- invalidar qualquer sessão CAS previamente obtida com as credenciais
  antigas (decorrência direta de ADR-004: "A sessão CAS deve ser invalidada
  quando a credencial iService mudar...");
- marcar a integração como **não validada**, exigindo nova execução do
  fluxo de validação (RF-007) antes de permitir qualquer ativação do
  Agente Coletor.

## Regras de negócio

- RN-006.1: Um CNPJ está associado a no máximo um tenant (ADR-005).
- RN-006.2: O tenant só é criado no momento da primeira configuração de
  integração, nunca no cadastro inicial (RF-001, ADR-005).
- RN-006.3: Toda alteração de credenciais reseta o estado de validação da
  integração para "não validada".
- RN-006.4: Nenhuma credencial iService pode aparecer em claro em logs,
  respostas de API, exceções ou builds frontend (RS-001).

## Campos assumidos como mínimos do MVP

Como decisão mínima para não bloquear a implementação, assume-se que o
formulário de configuração de credenciais do iService no MVP solicita, no
mínimo:

- usuário (login) do iService;
- senha do iService;
- URL base ou identificador do tenant/instância do iService (quando
  aplicável ao formato de autenticação do provedor).

Esta lista de campos é uma **suposição mínima de produto** e deve ser
confirmada/detalhada pelo `software-architect` junto ao contrato técnico de
integração com o iService (ex.: se há campo adicional de subdomínio,
domínio corporativo, etc.). Caso o iService exija campos adicionais
descobertos durante análise técnica, eles devem ser incorporados sem
necessidade de nova decisão de produto, desde que mantenham a mesma
finalidade (autenticação no iService).

## Casos de borda

- CNPJ com formato inválido → rejeitar antes de qualquer tentativa de
  criação de tenant, com mensagem de erro específica.
- CNPJ já associado a outro tenant → rejeitar a criação, informando conflito
  de unicidade, sem revelar dados do tenant existente.
- Cliente tenta configurar integração sem possuir tenant e sem informar
  nome/CNPJ → deve ser conduzido ao formulário de criação do tenant.
- Alteração de credenciais enquanto o Agente Coletor está ativo → a
  integração deve ser marcada como não validada e (fora de escopo desta
  análise, mas como impacto a considerar) a ativação do coletor deve ser
  reavaliada por RF-007/RF-008.
- Tentativa de um usuário sem membership `OWNER` de configurar credenciais
  → deve ser negada.

## Ambiguidades identificadas (requerem decisão ou confirmação)

1. **Campos exatos do formulário do iService**: assumidos como mínimo
   (usuário, senha, URL/tenant) — ver seção acima. Recomenda-se confirmação
   técnica com `software-architect` durante o desenho do contrato, pois o
   formato real de autenticação do iService (CAS) pode exigir parâmetros
   adicionais.
2. **Permissão de `ADMIN` sobre integrações**: ADR-004/ADR-005 preveem o
   papel `ADMIN` no membership, mas não definem se um `ADMIN` pode
   configurar/alterar credenciais iService. Decisão mínima de MVP: apenas
   `OWNER` pode. Caso o produto precise liberar `ADMIN` para essa ação,
   isso exige um novo requisito.

## Critérios de aceite

1. Dado um usuário com Trial ativo e sem tenant, quando informar nome e
   CNPJ válidos e únicos, então o sistema deve criar o tenant, o membership
   `OWNER` e associar o Trial sem alterar sua validade.
2. Dado um CNPJ já associado a outro tenant, quando o usuário tentar criar
   um novo tenant com o mesmo CNPJ, então o sistema deve rejeitar a
   operação informando o conflito.
3. Dado um tenant existente sem integração configurada, quando o `OWNER`
   informar as credenciais do iService, então elas devem ser persistidas
   cifradas e nunca retornadas em claro em qualquer resposta subsequente.
4. Dada uma integração já configurada e validada, quando o `OWNER` alterar
   as credenciais, então a integração deve voltar ao estado "não validada"
   e qualquer sessão CAS anterior deve ser invalidada.
5. Dado um usuário sem membership `OWNER` no tenant, quando tentar
   configurar ou alterar credenciais iService, então o sistema deve negar a
   operação.

## Dependências

- ADR-004 (cifra de credenciais, invalidação de sessão CAS).
- ADR-005 (tenancy, unicidade de CNPJ, criação de tenant na primeira
  integração).
- RF-005 (usuário autenticado e com sessão válida).

## Impactos

- Impacta diretamente o fluxo de ativação do Agente Coletor (RF-007,
  RF-008), pois alteração de credenciais reseta a validação.

## Fora do escopo

- Qualquer lógica de scraping, automação de coleta ou execução de sessão
  real de trabalho no iService (Agente Coletor pausado por decisão do
  usuário).
- Múltiplos membros configurando integrações concorrentes (fora do MVP).
