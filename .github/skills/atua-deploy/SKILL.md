---
name: atua-deploy
description: Automatiza o ciclo de redeploy "a quente" da API e do Collector ATUA já rodando na AWS, o disparo de ciclos de teste do Collector e o acompanhamento de logs via journalctl. Use quando precisar publicar uma alteração de código na API/Collector já provisionados, gerar um novo comando de coleta para teste, ou inspecionar logs das instâncias EC2 vivas.
---

# Deploy e operação ao vivo (API + Collector)

Esta skill evita repetir manualmente, comando a comando, o ciclo de
publish → sync no S3 → SSH → restart → verificação que era feito a cada
alteração no Collector/API durante o RF-009. Os scripts vivem em
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
* SDK do .NET instalado localmente (os scripts rodam `dotnet publish`).

## Comandos disponíveis (rode a partir de `infra/`)

Todos requerem `AWS_PROFILE` explícito, por exemplo:
`AWS_PROFILE=moldato make redeploy-collector`.

### Redeploy a quente (sem recriar a EC2)

```bash
make redeploy-api          # compila, publica no S3, sincroniza via SSH, reinicia atua-api
make redeploy-collector    # idem para o Collector (RID linux-x64, self-contained=false)
```

Cada um: (1) `dotnet publish`; (2) `aws s3 sync` para o prefixo de release;
(3) SSH — para o serviço, remove `.dll/.pdb/.json` antigos, ressincroniza,
reinicia; (4) confere MD5 local vs remoto; (5) mostra status do systemd.
Falha (`exit 1`) se o MD5 não bater — indica que o binário não foi
realmente atualizado.

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
make logs-api-follow          # tempo real (Ctrl+C para sair)
make logs-collector-follow
```

Resolve o IP público via `CloudFormation Outputs` do `AtuaComputeStack` —
não dependa de IPs anotados manualmente, eles mudam a cada recriação da
instância.

## Quando NÃO usar

* Para provisionar/recriar infraestrutura do zero, use `make up`/`make down`
  (ver `infra/Makefile`), não esta skill.
* Para inspecionar dados persistidos (Mongo), use a skill
  `atua-mongo-inspect`.

## Diagnóstico de falhas comuns

* `Connection attempt timed out` no SSH → seu IP mudou; adicione a regra no
  Security Group (porta 22) e revogue depois.
* MD5 local ≠ remoto → o `aws s3 sync` pode ter pulado o arquivo por
  heurística de tamanho/timestamp; o script já força `rm` antes do sync,
  mas se persistir, rode `redeploy-*` novamente.
* macOS não tem `md5sum` — o script já usa fallback `md5 -q`.
