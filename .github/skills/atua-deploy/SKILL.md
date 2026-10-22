---
name: atua-deploy
description: Automatiza o ciclo de redeploy "a quente" da API, do Collector e do Channel WhatsApp (ADR-049/050) já rodando na AWS, o disparo de ciclos de teste do Collector e o acompanhamento de logs via journalctl. Use quando precisar publicar uma alteração de código na API/Collector/Channel já provisionados, gerar um novo comando de coleta para teste, ou inspecionar logs das instâncias EC2 vivas.
---

# Deploy e operação ao vivo (API + Collector + Channel)

Esta skill evita repetir manualmente, comando a comando, o ciclo de
publish → sync no S3 → SSH → restart → verificação que era feito a cada
alteração no Collector/API durante o RF-009 (e, desde ADR-050, também no
Channel WhatsApp Node/Baileys). Os scripts vivem em
`.github/skills/atua-deploy/scripts/` e são chamados pelo
`infra/Makefile` (fonte de verdade dos targets).

## Pré-requisitos (ver `.env.example` na raiz do repo)

* AWS CLI v2 configurado com um profile (`AWS_PROFILE`, ex.: `moldato`) com
  permissão de: `cloudformation:DescribeStacks`, `s3:*` no bucket de
  releases, `sts:GetCallerIdentity`.
* Chave SSH local em `~/.ssh/atua-mvp-key.pem` (rode `make retrieve-keypair`
  uma vez se ainda não tiver).
* Seu IP público atual autorizado no Security Group da instância (porta 22).
  Os scripts NÃO abrem regras de SG automaticamente.
* `AtuaComputeStack` (API + Collector) no ar — confirme com `make status`.
* SDK do .NET instalado localmente (os scripts de API/Collector rodam
  `dotnet publish`).
* Para o Channel: `pnpm` instalado localmente (os scripts rodam
  `pnpm --filter @atua/channel-node build` e `pnpm ... deploy --prod`) e os
  tokens de serviço do Channel exportados no ambiente local antes de rodar
  `make deploy-channel` (ver seção "Channel WhatsApp" abaixo e
  `docs/guides/channel-node-deploy-ec2.md`).

## Comandos disponíveis (rode a partir de `infra/`)

Todos requerem `AWS_PROFILE` explícito, por exemplo:
`AWS_PROFILE=moldato make redeploy-collector`.

### Redeploy a quente (sem recriar a EC2)

```bash
make redeploy-api          # compila, publica no S3, sincroniza via SSH, reinicia atua-api
make redeploy-collector    # idem para o Collector (RID linux-x64, self-contained=false)
make redeploy-channel      # idem para o Channel (pnpm deploy --prod, sem IAM novo)
```

Cada um: (1) build; (2) publica no S3 (prefixo de release); (3) SSH — para
o serviço, ressincroniza o artefato, reinicia; (4) confere MD5 local vs
remoto; (5) mostra status do systemd. Falha (`exit 1`) se o MD5 não bater —
indica que o artefato não foi realmente atualizado.

### Channel WhatsApp (primeira instalação vs. redeploy)

O Channel roda na MESMA instância EC2 da API (ADR-049/050 — não há EBS
nem instância dedicados; a sessão Baileys vive apenas no disco raiz e é
perdida em `make down`, sendo refeita via QR no Office).

```bash
# Primeira instalação (idempotente — também segura de rodar de novo após
# um 'make up', quando o runtime/diretórios/unit já foram restaurados
# automaticamente pelo UserData, mas o arquivo de credenciais nunca é):
export ChannelWorker__ClaimServiceToken=...
export ChannelWorker__CompleteServiceToken=...
export ChannelWorker__MessageIngestServiceToken=...
export ChannelWorker__MediaIngestServiceToken=...
export ChannelWorker__SessionReportServiceToken=...   # opcional
make deploy-channel

# Redeploy de código já instalado (não reescreve tokens nem a unit):
make redeploy-channel
```

`deploy-channel` também publica o bundle em
`s3://atua-<ACCOUNT_ID>-releases/channel-latest/`, para que um futuro
`make up` (instância recriada do zero) já restaure automaticamente o
runtime Node, o bundle e a unit systemd via UserData — mas o arquivo
`/etc/atua-channel.env` (tokens) NUNCA é restaurado automaticamente (não
exige nenhuma permissão IAM nova para o Channel); rode `make deploy-channel`
novamente após cada `make up` para reinjetar os tokens e iniciar o serviço.

### Disparar um novo ciclo de teste do Collector

```bash
make collector-trigger-cycle
```

Renova o JWT do usuário de teste, desativa e reativa o
`collector-activation` (com `Idempotency-Key`), gerando um novo
`ImmediateCollectionCommand` `Pending` para o Worker reivindicar no
próximo polling. Sobrescreva `ATUA_TEST_EMAIL` / `ATUA_TEST_PASSWORD` /
`ATUA_TENANT_ID` / `ATUA_INTEGRATION_ID` se o tenant de teste mudar.

### Acompanhar logs

```bash
make logs-api                 # últimas ~200 linhas
make logs-collector
make logs-channel
make logs-api-follow          # tempo real (Ctrl+C para sair)
make logs-collector-follow
make logs-channel-follow
```

Resolve o IP público via `CloudFormation Outputs` do `AtuaComputeStack` —
não dependa de IPs anotados manualmente, eles mudam a cada recriação da
instância. O Channel usa o mesmo output `ApiPublicIp` (mesma instância).

## Quando NÃO usar

* Para provisionar/recriar infraestrutura do zero, use `make up`/`make down`
  (ver `infra/Makefile`), não esta skill.
* Para inspecionar dados persistidos (Mongo), use a skill
  `atua-mongo-inspect`.
* Para gerar/semear credenciais de serviço do Channel no banco, use os
  scripts em `infra/scripts/` (`seed-channel-credentials.sh`,
  `add-channel-media-ingest-credential.sh`,
  `add-channel-session-report-credential.sh`), NÃO esta skill.

## Diagnóstico de falhas comuns

* `Connection attempt timed out` no SSH → seu IP mudou; adicione a regra no
  Security Group (porta 22) e revogue depois.
* MD5 local ≠ remoto → o `aws s3 sync` pode ter pulado o arquivo por
  heurística de tamanho/timestamp; o script já força `rm` antes do sync,
  mas se persistir, rode `redeploy-*` novamente.
* macOS não tem `md5sum` — o script já usa fallback `md5 -q`.
* `atua-channel` em crash-loop logo após `make up` → esperado se
  `/etc/atua-channel.env` ainda não foi reinjetado; rode
  `make deploy-channel` (ver seção "Channel WhatsApp" acima).

