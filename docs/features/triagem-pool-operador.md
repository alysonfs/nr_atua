# Pool de triagem — visão do operador (C1, C2, C3)

**Status:** IMPLEMENTATION
**Épico:** [Triagem WhatsApp Fase 1](./triagem-whatsapp-fase-1.md), seção 10, Bloco C.
**Frontend:** `apps/office`. **Autoridade de estado:** Master API (`Triagem`).

## 1. Objetivo

Permitir que operadores do posto vejam as triagens que precisam de decisão
humana, consultem o que foi coletado e assumam o atendimento com exclusividade.

## 2. Escopo da fase 1

### C1 — Lista do pool

- Mostra apenas triagens do tenant do usuário nos estados:
  `LIBERADA_AO_POOL`, `EXPIRADA`, `FALHA_DE_CONTATO` e
  `AGUARDANDO_INTERVENCAO`.
- Colunas mínimas: número da OS, consumidor, produto/modelo (se houver),
  estado, itens do checklist mínimo completos (ex.: 4/5), tempo no pool e
  indicador de "nova atividade".
- Filtro por estado; ordenação padrão pela entrada mais antiga no pool.
- Contador de itens do pool visível na navegação.
- Atualização por polling a cada **30 s** (push em tempo real é incremento futuro).
- Sem tratamento especial para triagens antigas na fase 1.

### C2 — Detalhe (painel de suporte)

- Dados da OS e do consumidor.
- Checklist: descrição, nota fiscal legível, etiqueta (modelo e série),
  foto ou vídeo do defeito, endereço completo (obrigatórios); ponto de
  referência e localização (opcionais). Cada item com status e valor.
- Anexos com acesso por link autorizado e temporário.
- Conversa em ordem cronológica, incluindo transcrições quando existirem.
- Motivo do estado (ex.: expirada em 48h, falha de contato, 3 desvios).

### C3 — Assumir

- Botão "Assumir" em triagens do pool.
- Exclusividade: o primeiro operador vence (concorrência otimista); quem
  perder recebe aviso de que a triagem já foi assumida, e por quem.
- A triagem vai para `ASSUMIDA_POR_OPERADOR` (terminal); a LLM para.
- Registra operador e horário da assunção (auditoria).
- Não é possível devolver a triagem ao pool na fase 1.

### Notificação

- Indicador de "nova atividade" quando chega mensagem do consumidor antes
  da assunção (sem resumo do conteúdo).
- Contador e lista atualizados pelo polling.

### Permissões

- Pool e detalhes de triagens não assumidas: operadores autorizados do posto.
- Triagem assumida: visível apenas para quem assumiu e para gestores.
- Isolamento por tenant obrigatório.

## 3. Fora de escopo

C4 (responder ao consumidor e editar campos), grupos, priorização, SLA,
sugestão de peças, condução pela LLM, devolver ao pool e arquivamento.

## 4. Critérios de aceite

1. **Dado** uma triagem em `LIBERADA_AO_POOL` no meu tenant, **quando** abro o
   pool, **então** ela aparece com as colunas mínimas.
2. **Dado** triagens de outro tenant, **quando** abro o pool, **então** elas
   não aparecem e o acesso direto ao detalhe é negado.
3. **Dado** triagens em `EM_COLETA` ou `ASSUMIDA_POR_OPERADOR`, **quando** abro
   o pool, **então** elas não aparecem.
4. **Dado** o filtro "Expirada", **quando** aplicado, **então** só aparecem
   triagens `EXPIRADA`.
5. **Dado** o pool aberto, **quando** uma nova triagem é liberada, **então**
   ela aparece em até 30 s e o contador é atualizado.
6. **Dado** o detalhe, **quando** aberto, **então** exibe checklist, anexos,
   conversa cronológica e motivo do estado.
7. **Dado** dois operadores assumindo a mesma triagem, **quando** ambos
   confirmam, **então** apenas um vence e o outro vê quem assumiu.
8. **Dado** uma triagem assumida, **quando** outro operador (não gestor) tenta
   abrir o detalhe, **então** o acesso é negado.
9. **Dado** uma triagem assumida, **quando** o consumidor envia mensagem,
   **então** a LLM não responde.
10. **Dado** nova mensagem do consumidor antes da assunção, **quando** o
    polling ocorre, **então** o item exibe indicador de "nova atividade".

## 5. Decisões do PO (2026-10)

| Pergunta | Decisão |
|---|---|
| Devolver ao pool após assumir | Não na fase 1 |
| Triagens antigas | Sem tratamento especial na fase 1 |
| Ver triagem assumida por colega | Apenas quem assumiu e gestores |
| Notificação de nova mensagem | Apenas indicador de "nova atividade" |
| Intervalo de polling | 30 s |

## 6. Próximos passos

### Backend — implementado

Rotas autenticadas por sessão do Office (`BrowserSession`), isolamento por
tenant obrigatório (`/api/tenants/{tenantId}/triage-pool...`):

- `GET /api/tenants/{tenantId}/triage-pool?status={estado}` — C1, lista do
  pool. `status` opcional, aceita apenas um dos quatro estados do pool.
- `GET /api/tenants/{tenantId}/triage-pool/{triagemId}` — C2, detalhe
  (checklist, anexos com URL pré-assinada temporária, conversa cronológica,
  motivo do estado).
- `POST /api/tenants/{tenantId}/triage-pool/{triagemId}/assume` — C3,
  assumir com concorrência otimista.

Casos de uso: `IListTriagemPoolUseCase`, `IGetTriagemPoolDetailUseCase`,
`IAssumeTriagemUseCase` (`Application/Triagens/Contracts`). Domínio:
`Triagem.AssumirPorOperador(operadorUserId, now)` passou a aceitar os
quatro estados do pool e registra `AssumidaPorUserId`/`AssumidaEmUtc`.
Migration `AddTriagemAssumidaPorOperador` adiciona as colunas e um índice
de apoio (`ix_triagens_tenant_status_updated_at`) — gerada, **não aplicada**
em nenhum banco.

Limitações/decisões conhecidas, para acompanhamento:

- Não existe coluna dedicada para o momento de entrada no pool; usa-se
  `UpdatedAtUtc` como aproximação (coincide com a transição mais recente,
  incluindo a entrada no estado do pool).
- "Triagem assumida por outro operador não-gestor" e "triagem de outro
  tenant" retornam ambos 404 no detalhe (nunca 403), para não revelar a
  existência/estado do recurso a quem não tem acesso.
- **Pendência de infraestrutura:** o anexo usa URL pré-assinada do S3
  (`ITriagemInboundMediaStorage.CreatePresignedReadUrlAsync`), mas a role
  da EC2 hoje só tem `s3:PutObject`/`s3:DeleteObject` (ADR-046). A geração
  da URL não exige `s3:GetObject`, mas o **uso** da URL por quem a recebe
  exige — permissão a ser concedida via CDK pelo `aws-architect` (fora do
  escopo deste incremento).
- A matéria-prima do indicador de "nova atividade" (critério de aceite 10)
  exigiu estender o casamento de mensagens inbound
  (`TriagemInboundMessageRepository`) para qualquer estado da triagem, não
  mais restrito a `ContatoIniciado`/`EmColeta` — o que o ADR-043 já previa
  para quando "o pool e o painel do operador consumirem este mesmo evento".

`frontend-engineer`: telas em `apps/office`.
