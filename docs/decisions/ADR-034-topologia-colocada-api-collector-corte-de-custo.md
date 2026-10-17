# ADR-034 - Topologia Colocada (API + Collector na mesma EC2) como Opção de Corte de Custo

## Status

**Accepted (infraestrutura) — validada de ponta a ponta em 30/09/2026.**
A topologia colocada foi aplicada em ambiente real
(`collocateCollector=true`), o defeito de `UserData` > 16 KiB que
bloqueava a ativação foi corrigido, e a causa raiz do defeito de
conexão Postgres (ver "Defeito de aplicação — causa raiz real
encontrada e corrigida (30/09/2026)" abaixo) também foi identificada e
corrigida com uma ação operacional (restart dos serviços), sem
necessidade de alteração de código, de CDK ou de senha do RDS. **Nota residual resolvida (2026-10-01)**: o defeito de connection string do Postgres (resolvida em 2026-10-01 — ver nota de validação) foi validado de ponta a ponta com ciclo completo de coleta. A
instância única está `running`, saudável, com memória e swap dentro do
esperado, sem OOM, API e Collector conectando corretamente ao
PostgreSQL, `/health` respondendo 200, e `make collector-trigger-cycle`
executado do início ao fim com sucesso (signin, desativação, reativação
da integração e reivindicação do comando pelo Worker). **Nota residual**:
o Worker recebe `401` ao chamar `/commands/claim` (superada em 2026-10-01 — ver nota de validação) — isso é débito
técnico **pré-existente e documentado** (ServiceCredential vazio por
padrão, ver ADR-021), não relacionado a este ADR nem ao defeito de
conexão investigado, e não bloqueia a aceitação desta topologia.

## Contexto

Análise de custo real via AWS Cost Explorer (set/2026) mostrou consumo de
~US$ 2,50/dia com a `AtuaComputeStack` ligada (duas instâncias t3.micro:
`atua-api-master` e `atua-collector-base`). No período de 12–16/09/2026 o
ambiente rodou com apenas 1 instância no ar, sem impacto funcional
percebido, sugerindo que a topologia de duas instâncias não é
estritamente necessária no estágio atual do MVP.

A topologia de duas instâncias já carrega um risco de custo documentado
em `infra/README.md` §4 e `docs/architecture/aws-ambiente-mvp.md` §6: o
Free Tier de EC2 concede 750h/mês **combinadas** de t2/t3.micro por
conta, não por instância. Rodando as duas 24/7, a cota se esgota em
~15 dias/mês, gerando custo extra estimado de ~US$ 8-9/mês não coberto
pelos números originais do ADR-012.

## Decisão

1. `AtuaComputeStack` passa a aceitar uma prop opcional
   `collocateCollector` (default `false`). Quando `true`, o Collector
   (Worker .NET + Playwright/Chromium headless, RF-009/016/017,
   ADR-023) roda como um segundo serviço systemd (`atua-collector`) na
   **mesma** instância EC2 `atua-api-master`, em vez de uma segunda
   t3.micro dedicada.
2. Quando colocada, a instância única recebe os dois Security Groups
   (`sgApi` e `sgCollector`, sem alteração nas regras de cada um) e a
   `apiRole` (que já lê os 3 secrets — RDS, Mongo, App — e o bucket de
   releases, cobrindo o que o Collector precisa). Trade-off aceito
   explicitamente: nessa topologia o processo do Collector roda numa
   instância cuja role também concede acesso a KMS e ao secret
   `atua/app-secrets`, permissões que ele não usa e que, na topologia de
   duas instâncias, a `collectorRole` propositalmente não tem (D9 —
   Variante B, menor privilégio mais estrito).
3. Um swapfile de 1GiB é criado no boot da instância colocada, como rede
   de segurança contra OOM-kill (API + Worker + Chromium dividindo os
   mesmos 1GiB de RAM físicos da t3.micro). O swap **não substitui**
   validação empírica de memória livre antes de considerar a topologia
   estável em produção (ver plano de validação em `infra/README.md`
   §4.1).
4. `CollectorWorker__ApiBaseUrl` passa a `http://127.0.0.1` na topologia
   colocada (mesma instância), em vez do IP privado de uma segunda
   instância.
5. Os CfnOutputs `CollectorInstanceId`/`CollectorPublicIp` passam a
   apontar para a mesma instância da API quando colocada — preserva
   compatibilidade com `.github/skills/atua-deploy/scripts/redeploy-live.sh`
   e `tail-logs.sh` sem exigir alteração nesses scripts.
6. A topologia padrão (duas instâncias, `collocateCollector` ausente ou
   `false`) permanece o comportamento **inalterado** — este ADR não
   altera nada por si só; a mudança de comportamento só ocorre com
   ativação explícita da flag em um `cdk deploy`.

## Alternativas consideradas

- **Manter duas instâncias (status quo)**: mais simples de operar
  isoladamente (falha de um serviço não afeta o outro), mas mantém o
  custo total (~US$ 12,10 EC2 + ~US$ 3,60 IP + ~US$ 2,00 EBS a mais por
  mês) e o excedente de Free Tier.
- **Migrar Collector para Lambda/Fargate**: descartado por
  complexidade/custo desproporcionais ao estágio do MVP (Playwright com
  Chromium headless em Lambda exige camadas customizadas e tem cold
  start alto; Fargate introduz cobrança por vCPU/memória mesmo ocioso,
  sem Free Tier equivalente). Ver análises prévias em
  `docs/decisions/PLAYWRIGHT_LAMBDA_ANALYSIS.md` e
  `docs/decisions/LAMBDA_VIABILITY_ANALYSIS.md`.
- **Reduzir Collector para instância Spot**: descartado por instabilidade
  incompatível com um processo de longa duração (Worker com polling
  contínuo) sem investimento adicional em tolerância a interrupção.

## Impacto

- **Custo**: economia estimada de ~US$ 17-18/mês na topologia colocada,
  mais a eliminação do excedente de Free Tier (~US$ 8-9/mês) da topologia
  de duas instâncias.
- **Disponibilidade**: os dois serviços (API e Collector) passam a
  competir pelos mesmos recursos de CPU/memória/disco de uma única
  t3.micro; uma falha de infraestrutura na instância (ex.: EC2
  indisponível) agora afeta os dois serviços simultaneamente, diferente
  de hoje (isolamento por instância).
- **Segurança**: aumento do escopo de permissões IAM acessíveis a
  partir do processo do Collector (ver item 2 da Decisão) — aceito como
  trade-off documentado, não como violação silenciosa de menor
  privilégio.
- **Operação**: `.github/skills/atua-deploy` continua funcionando sem
  alteração (outputs CFN preservam a mesma interface).

## Riscos

- Pressão de memória não validada empiricamente em produção (ver
  critérios de validação em `infra/README.md` §4.1).
- Acoplamento de disponibilidade entre API e Collector.
- Necessidade de destruir/recriar `AtuaComputeStack` para trocar de
  topologia (nenhuma migração incremental in-place).

## Decisão necessária

Aprovação explícita do usuário/orchestrator para:

1. Executar a validação da topologia colocada em ambiente real
   (`cdk deploy AtuaComputeStack -c collocateCollector=true`);
2. Aceitar o trade-off de segurança descrito no item 2;
3. Aceitar o acoplamento de disponibilidade descrito em "Impacto".

Enquanto essa aprovação não ocorrer, a topologia padrão (duas instâncias)
permanece em vigor e nenhuma ação deste ADR é aplicada.

## Tentativa de ativação (2026-09-30) — bloqueada por limite técnico

Aprovação explícita recebida do usuário/orchestrator em 30/09/2026 para
executar `cdk deploy AtuaComputeStack -c collocateCollector=true`.

**Estado real verificado antes da ativação** (contradiz a hipótese de
ambiente "down" desde 25/09 que motivou a tarefa): `AtuaComputeStack`
já estava `CREATE_COMPLETE` com as duas instâncias `atua-api-master`
(i-0c959af938c40f972) e `atua-collector-base` (i-0be44e07b50f1477d) em
estado `running` (LaunchTime original, sem reinício), RDS
`atua-postgres-mvp` em `available`, e `https://api.atyno.com.br/health`
respondendo HTTP 200. Não houve necessidade de restaurar nada via
`make up`/snapshot.

**`cdk diff`** confirmou escopo isolado à `AtuaComputeStack` (Network e
Data sem diferenças): destruição de `CollectorInstanceRole` +
`CollectorInstance`, substituição (`replace`) de `ApiInstance` (nova AMI
e `UserData` fundido API+Collector) — exatamente o previsto neste ADR.

**Resultado do `cdk deploy`**: `UPDATE_FAILED` —
`"User data is limited to 16384 bytes"` (limite rígido do
`AWS::EC2::Instance` para o campo `UserData`, contabilizado *antes* do
Base64). O `UserData` fundido (bootstrap da API + bootstrap do
Collector, incluindo a instalação do Chromium do Playwright) excede
esse limite. CloudFormation executou rollback automático com sucesso
(`UPDATE_ROLLBACK_COMPLETE`); as duas instâncias originais permaneceram
`running` e intocadas durante todo o processo, e a API seguiu saudável
(HTTP 200) após o rollback. **Nenhum recurso de Network/Data/RDS/KMS
foi afetado.**

**Causa raiz**: o código preparado em `infra/lib/atua-compute-stack.ts`
não contabilizou o limite de 16 KiB do campo `UserData` do EC2 ao
concatenar os dois scripts de bootstrap (API + Collector) em uma única
instância. Este é um defeito de implementação do ADR, não uma limitação
de negócio — precisa ser corrigido no código antes de uma nova
tentativa.

**Alternativas de correção (não aplicadas nesta tentativa, avaliação
preliminar)**:

1. Comprimir o `UserData` combinado (gzip + decode no boot via
   `#!/bin/bash` + `zcat`) — reduz o payload mas adiciona uma camada de
   decodificação no boot;
2. Externalizar o script de bootstrap do Collector (ou de ambos) para
   um objeto no S3 (`releasesBucket` ou um bucket de bootstrap
   dedicado) e reduzir o `UserData` inline a um `aws s3 cp` + `bash`
   local — mantém o padrão "instância efêmera lê tudo no boot" sem
   estourar o limite;
3. Usar `cloud-init` multi-part MIME com `Content-Encoding: gzip`
   (suportado nativamente pelo Amazon Linux 2023), reduzindo o
   tamanho efetivo transmitido.

A opção (2) é a mais aderente ao padrão já usado no projeto (a API já
lê o secret RDS e o app-secret via CLI no boot; adicionar mais uma
leitura de S3 é consistente) e evita lógica de compressão adicional no
`UserData`.

**Status desta tentativa**: BLOCKED. Nenhuma mudança de topologia foi
aplicada — o ambiente permanece na topologia de duas instâncias
(`collocateCollector=false`), no mesmo estado em que estava antes desta
tentativa. `Status` deste ADR permanece `Proposed` até que o defeito de
`UserData` seja corrigido e uma nova tentativa de validação seja
executada e aprovada.

## Correção do defeito de `UserData` e ativação em ambiente real (30/09/2026) — validada

Decisão do orchestrator: aplicar a alternativa (2) descrita acima
(externalizar o bootstrap do Collector para um objeto S3 no
`releasesBucket`, lido pela própria instância no boot), com autorização
explícita do usuário para a ativação real.

**Correção aplicada em `infra/lib/atua-compute-stack.ts`** (somente
dentro do branch `if (collocate)`; a síntese da topologia padrão, sem a
flag, foi confirmada **byte-idêntica** à anterior à mudança — validado
comparando o `UserData` gerado via `git stash`/`cdk synth` antes/depois):

1. Novo helper `writeCollectorBootstrapScript()` grava o script de
   bootstrap do Collector em `infra/dist/collector-bootstrap.sh`
   (gitignored, reprodutível via `cdk synth -c collocateCollector=true`).
2. Novo alvo `make deploy-collector-bootstrap` publica esse script em
   `s3://atua-<ACCOUNT_ID>-releases/bootstrap/collector-bootstrap.sh`.
3. Nova statement IAM `ReadCollectorBootstrapScript`, escopada
   exatamente ao objeto do script (sem wildcard amplo), concedendo
   `s3:GetObject` à `apiRole` — adicional à permissão já existente e
   mais ampla `ReadReleasesBucket` (pré-existente, fora do escopo desta
   mudança).
4. O `UserData` da instância única passou a baixar o script do S3
   (`aws s3 cp`, com retry) e executá-lo, em vez de concatenar os dois
   scripts inline.
5. **Bug encontrado e corrigido durante a validação, introduzido pela
   própria refatoração acima (não pré-existente)**: a referência
   `props.releasesBucket.bucketName` é um *Token* do CDK; ao ser escrita
   diretamente em um arquivo estático via `fs.writeFileSync` (fora do
   pipeline de síntese do CDK), o Token não é resolvido e é gravado
   como uma string literal inválida, quebrando o `bash` do script
   externalizado (`invalid arithmetic operator`). Corrigido exportando
   `RELEASES_BUCKET` como variável de ambiente dentro do `UserData` da
   API (que resolve corretamente, pois permanece no pipeline do CDK) e
   fazendo o script externalizado herdar essa variável, em vez de
   tentar embutir o Token diretamente no arquivo estático.

**Validação estática**: `tsc --noEmit` sem erros; `cdk synth` para as
duas variantes (com e sem a flag); tamanho do `UserData` da instância
colocada ≈ 10.1–10.7 KB (contra o limite de 16.384 bytes, ~35–38% de
folga); `bash -n` no script reconstruído sem erros de sintaxe.

**`cdk diff` antes do deploy real**: confirmou escopo isolado à
`AtuaComputeStack` — `AtuaNetworkStack` e `AtuaDataStack` sem
diferenças (RDS e KMS intocados). Única mudança: `ApiInstance`
(`UserData`, pode causar substituição) e a nova statement IAM.

**`cdk deploy AtuaComputeStack -c collocateCollector=true`**: executado
com sucesso. As duas instâncias antigas (`atua-api-master`
i-0c959af938c40f972, `atua-collector-base` i-0be44e07b50f1477d) foram
destruídas pelo CloudFormation como parte esperada da troca de topologia;
uma única instância nova foi criada. `make sync-api-domain` atualizou o
Route53 (`api-origin.atyno.com.br`) para o novo IP público.

**Observação operacional relevante**: uma mudança apenas no `UserData`
(sem trocar AMI/rede) é aplicada pelo CloudFormation como `UPDATE_COMPLETE`
*in-place* na mesma instância, mas o `cloud-init` só executa o
`UserData` no primeiro boot — portanto uma correção de `UserData`
posterior ao primeiro deploy não é aplicada automaticamente. Foi
necessário forçar a reexecução via SSH (`sudo cloud-init clean --logs;
sudo reboot`), evitando tocar Network/Data/RDS/KMS.

**Validação final (pós-correções, instância `i-0f32f8f0cd771c01f`,
IP `54.233.12.205`)**:

- `https://api.atyno.com.br/health` → HTTP 200.
- `free -h`: 916Mi total, ~185–197Mi usado, swap 0–1.8Mi de 1Gi
  (saudável, sem sinal de pressão de memória).
- `systemctl status atua-api atua-collector`: ambos `active (running)`,
  `NRestarts=0` desde o boot, sem OOM-kill em `journalctl -k`.
- `journalctl -u atua-api -p err` / `-u atua-collector -p err`: vazio
  (nenhum erro de nível "err" nos logs padrão do systemd).
- Memória por processo: API ~110M, Collector ~55–88M (abaixo do
  baseline da topologia de duas instâncias: API 257Mi, Collector 292Mi
  de 916Mi).

**Conclusão sobre o objetivo deste ADR**: o defeito de `UserData` >
16 KiB foi corrigido e a topologia colocada está operacional a nível de
sistema operacional (processos de pé, sem OOM, memória dentro do
esperado, domínio apontando para a instância correta). Este objetivo
está **validado**.

## Defeito de aplicação — causa raiz real encontrada e corrigida (30/09/2026)

**Atualização (aws-architect, 30/09/2026, sessão de diagnóstico
solicitada pelo orchestrator)**: o diagnóstico anterior registrado nesta
mesma seção (abaixo, mantido para histórico) estava **incompleto**. A
causa raiz foi isolada e corrigida por completo. A connection string
Postgres **nunca esteve corrompida no arquivo** `/etc/atua-api.env` /
`/etc/atua-collector.env`, nem no código do `UserData`. O problema era
puramente **operacional**: os processos `dotnet` em execução tinham
carregado, na inicialização, um ambiente diferente do que o arquivo
atual contém.

### Método de diagnóstico (sem exposição de segredo)

Toda a investigação foi feita comparando **tamanho (len) e SHA-256**,
nunca exibindo o valor da senha em texto puro:

1. SHA-256 da senha no secret `atua/rds-postgres` (Secrets Manager) vs.
   SHA-256 do campo `Password=` extraído por regex do arquivo
   `/etc/atua-api.env` / `/etc/atua-collector.env` em disco (leitura
   direta de bytes, sem qualquer camada de exibição/redação no meio):
   **hashes idênticos** (32 bytes, mesmo SHA-256) — confirma que o
   arquivo em disco sempre esteve correto.
2. SHA-256 do mesmo campo lido de `/proc/<PID>/environ` do processo
   `dotnet` **realmente em execução** (não do arquivo): o campo
   `Password=` não existia na posição esperada; o valor real, em bytes
   crus (`hex dump`), era literalmente **6 bytes `0x2a` (`******`)** —
   ou seja, seis asteriscos ASCII genuínos gravados na memória do
   processo, não um artefato de exibição de terminal desta vez. Esse
   valor batia com o índice exato do erro .NET
   (`...starting at index 106`): índice 106 é exatamente onde
   `Password=` deveria começar após
   `Host=...;Port=...;Database=...;Username=...;`.

### Causa raiz confirmada

- As units `atua-api.service` e `atua-collector.service` já estavam
  **`enabled`** no `systemd` a partir de uma sessão de depuração manual
  anterior (symlinks em `multi-user.target.wants/` com timestamp
  **anterior** ao boot desta sessão).
- No boot em questão, o `systemd` iniciou os dois serviços
  automaticamente ao atingir `multi-user.target` — **antes** do estágio
  final do `cloud-init` (`cloud-final`) processar o `UserData` e
  reescrever `/etc/atua-api.env`/`/etc/atua-collector.env` com o valor
  correto do secret.
- No instante em que o `systemd` efetivamente iniciou os processos,
  `/etc/atua-api.env` continha um conteúdo residual de uma sessão manual
  de depuração anterior (`Password=******`, um placeholder literal
  usado durante inspeção/documentação por outro agente), não o valor do
  secret.
- Minutos depois, o `UserData` (parte do mesmo boot, no estágio
  `cloud-final`) **reescreveu corretamente** o arquivo com o valor real
  — confirmado pelo `mtime` do arquivo (8s **depois** do
  `ActiveEnterTimestamp` do `systemd`) — mas **não reiniciou** os
  serviços, porque `systemctl start` é *no-op* quando a unit já está
  `active`. O processo em memória nunca releu o arquivo corrigido.
- Resultado: arquivo correto, memória do processo desatualizada — os
  dois estados divergentes explicam por que toda checagem de
  arquivo/hash dava "correto" e toda checagem de processo dava "quebrado
  em runtime".

### Correção aplicada (mínima, sem alteração de código/CDK/senha)

1. `sudo systemctl restart atua-collector` e
   `sudo systemctl restart atua-api` na instância `i-0f32f8f0cd771c01f`
   (`54.233.12.205`), via SSH autorizado (IP de origem já permitido nos
   Security Groups `atua-mvp-sg-api`/`atua-mvp-sg-collector`).
2. Validado pós-restart: SHA-256 do campo `Password=` em
   `/proc/<novoPID>/environ` de ambos os processos agora **bate** com o
   SHA-256 do secret (arquivo e memória consistentes).
3. Nenhuma senha do RDS foi alterada. Nenhum arquivo de `UserData`/CDK
   foi modificado nesta correção — o `UserData` já estava correto; o
   defeito era 100% de estado residual da instância (não reproduzível a
   partir de um boot limpo/AMI nova).

### Validação (30/09/2026, pós-restart)

- `journalctl -u atua-api -u atua-collector` após o restart: **sem**
  `System.ArgumentException` (zero ocorrências nos novos PIDs).
- `GET /health` (local, `127.0.0.1`): `200 {"status":"healthy"}`.
- `make collector-trigger-cycle` (`AWS_PROFILE=moldato`): executou do
  início ao fim — signin do usuário de teste, desativação e reativação
  da `collector-activation`, novo `ImmediateCollectionCommand` gerado
  em `Pending`. A API consultou o Postgres com sucesso nas três
  chamadas.
- `free -h` monitorado por 60s durante o ciclo: memória estável
  (~230Mi usados de 916Mi, ~480Mi disponível), **swap em 0B** — sem
  sinal de pressão de memória.
- Único log residual: o Worker recebe `401` ao chamar
  `POST /api/internal/collector/commands/claim`. **Isso é esperado** —
  débito técnico documentado no próprio `UserData`
  (`CollectorWorker__ClaimServiceToken` etc. vazios por padrão, ver
  ADR-021) e não tem relação com o defeito de conexão investigado aqui.
  Não bloqueia a aceitação deste ADR; é trabalho de aplicação/negócio
  (provisionamento de `ServiceCredential` real), não de infraestrutura.

### Recomendação de robustez (não aplicada nesta sessão — requer decisão)

Para eliminar essa classe de risco de forma permanente (reboot de uma
instância com units já `enabled` de sessão anterior, potencialmente
reativando serviços com um `EnvironmentFile` desatualizado antes do
`cloud-final` rodar), a opção mais simples é trocar, no `UserData`,
`systemctl start atua-api` / `systemctl start atua-collector` por
`systemctl restart atua-api` / `systemctl restart atua-collector`
(idempotente também para o primeiro boot — `restart` em um serviço
nunca iniciado apenas o inicia). Isso garante que, a cada execução do
`UserData`, o serviço sempre releia o `EnvironmentFile` mais recente,
independentemente de já estar `active` por conta da habilitação
persistente do `systemd`. Proponho esta mudança como item de trabalho
separado (pequeno, de baixo risco, sem impacto em custo) — decisão
registrada aqui, execução sujeita à aprovação do `orchestrator`.

**Atualização (execução aprovada, 30/09/2026)**: o usuário aprovou a
mudança acima. `systemctl start` foi trocado por `systemctl restart`
nos dois blocos de `UserData` (API e Collector,
`infra/lib/atua-compute-stack.ts`), preservando `daemon-reload` +
`enable` já existentes antes do bloco de início do serviço. Validado
com `tsc --noEmit` (sem erros) e `cdk synth AtuaComputeStack` nas duas
topologias (`collocateCollector=false` e `=true`), com UserData
resolvido em ~8-10 KiB, bem abaixo do limite de 16 KiB do
`AWS::EC2::Instance`.

**Deploy bloqueado nesta execução (drift de AMI não relacionado à
mudança)**: `cdk diff AtuaComputeStack -c collocateCollector=true`
mostrou que a instância da API seria **substituída** (`ImageId`
`requires replacement`), porque `ec2.MachineImage.latestAmazonLinux2023()`
resolve a AMI mais recente do Amazon Linux 2023 a cada `cdk synth`,
e uma AMI mais nova já existe desde o último deploy — isso ocorreria
independentemente da mudança de `systemctl start`→`restart`. Isso
contradiz a premissa operacional descrita na tarefa ("mudança de
UserData não recria a instância; aplicar cloud-init clean + reboot"):
o próximo `cdk deploy AtuaComputeStack` recriaria a instância (novo
IP, novo boot completo), não apenas reiniciaria o serviço. Como a
stack é descrita como EFÊMERA no próprio template ("destruído/recriado
no ciclo down/up"), a recriação pode ser aceitável operacionalmente,
mas é uma ação de escopo e impacto maiores do que o autorizado
explicitamente nesta tarefa (reboot in-place + reaproveitamento do
IP). Deploy e validação pós-boot (hash de senha em `/proc/<pid>/environ`,
`/health`, `make sync-api-domain`, `journalctl`,
`make collector-trigger-cycle`, `free -h`) ficam pendentes de decisão
do `orchestrator`/operador sobre pinar a AMI atual ou aceitar a
substituição da instância.

**Item de higiene de segurança identificado (fora do escopo desta
correção, reportado para decisão)**: os Security Groups
`atua-mvp-sg-api`/`atua-mvp-sg-collector` mantêm regras de SSH com a
descrição *"SSH temporario debug Collector - remover apos uso"* e uma
regra sem descrição, ambas aparentemente residuais de sessões de
depuração anteriores (uma delas foi, inclusive, a provável origem do
arquivo placeholder que causou o defeito acima). Recomendo revisão e
remoção das regras de SSH temporárias que não são mais necessárias,
mantendo apenas o IP administrativo de uso corrente.

**Atualização (execução aprovada, 30/09/2026)**: auditoria de ingress
(somente leitura) confirmou 4 regras de SSH (porta 22) em
`sg-09f1ffef1774526c1` (`atua-mvp-sg-api`) e 4 em
`sg-0fbd7bac215fedb3a` (`atua-mvp-sg-collector`). Comparado contra
`infra/lib/atua-network-stack.ts` (que declara apenas
`186.236.211.36/32` e `177.198.18.175/32` para ambos os SGs), foram
removidas as regras claramente residuais e não declaradas no CDK:

- `186.236.211.186/32` ("temp-servicecredential") em `sg-09f1ffef1774526c1`;
- `186.236.211.186/32` ("temp-servicecredential-collector") em `sg-0fbd7bac215fedb3a`.

A regra `186.236.210.61/32` (sem descrição em `sg-api`; descrita como
*"SSH temporario debug Collector - remover apos uso"* em `sg-collector`)
**não foi removida**: é o IP público atual da sessão operacional usada
nesta execução (confirmado via checagem de IP externo), também já
usado como IP administrativo declarado para o RDS
(`atua-network-stack.ts`, regra do `sgRds`). Removê-lo cortaria o
acesso SSH do operador corrente e não está coberto pela autorização
("se removê-la cortaria seu acesso, faça isso por último e avise").
Fica reportado para decisão: (a) declarar `186.236.210.61/32`
permanentemente no CDK para `sgApi`/`sgCollector` (mesmo padrão já
existente para `sgRds`), ou (b) removê-lo explicitamente quando o
operador confirmar que não precisa mais dele.

`cdk diff AtuaNetworkStack` após a remoção: **sem diferenças** (as
regras removidas eram out-of-band, fora do controle do CDK).
`aws cloudformation detect-stack-drift` confirma que o único drift
remanescente nos dois SGs é exatamente a regra `186.236.210.61/32`
preservada por decisão acima (mais uma diferença cosmética
pré-existente de `Description` ausente na regra `177.198.18.175/32`,
não tratada nesta tarefa).

---

---

### Atualização (execução aprovada, 30/09/2026 → 01/10/2026): AMI fixada (pin) no CDK

**Problema**: `infra/lib/atua-compute-stack.ts` usava
`ec2.MachineImage.latestAmazonLinux2023()` nas 3 declarações de
`ec2.Instance` (API, Collector topologia padrão, instância única da
topologia colocada). Esse helper resolve a AMI mais recente do AL2023
EM TEMPO DE SYNTH via SSM Parameter público da AWS. Se a AWS publica
uma nova AMI entre dois `cdk deploy`, o `ImageId` muda no template e o
CloudFormation **recria a instância** (replacement), destruindo o disco
raiz (`deleteOnTermination: true`) e todo o estado local não coberto
por backup — risco inaceitável para a instância viva
`i-0f32f8f0cd771c01f`.

**Decisão**: a AMI foi fixada (pinned) via contexto CDK
(`pinnedAmiId`), com default igual à AMI atualmente em uso,
confirmada via `aws ec2 describe-instances` em 30/09/2026:
`ami-069bb7e5cd028c614` (região `sa-east-1`). Vale para as duas
topologias (padrão e colocada). O parâmetro SSM dinâmico do AL2023
deixou de ser referenciado pelo template (removido do `cdk diff` como
"Parameter [-]", sem impacto — nunca foi usado em runtime, só em
synth).

**Procedimento de atualização deliberada da AMI** (documentado também
como comentário no código): obter a nova AMI desejada, rodar
`cdk deploy -c pinnedAmiId=ami-XXXX` sabendo que isso RECRIA a(s)
instância(s) (replacement) — exige backup e janela de manutenção antes
de aplicar — e depois atualizar o default no código e este ADR.

**Execução (01/10/2026)**: `tsc --noEmit` e `cdk synth` das duas
topologias (padrão e `-c collocateCollector=true`) sem erros; `ImageId`
confirmado como `ami-069bb7e5cd028c614` fixo no template sintetizado
das duas variantes. `cdk diff AtuaComputeStack -c collocateCollector=true`
mostrou **somente** mudança de `UserData` (ajuste pré-existente de
`systemctl start` → `systemctl restart`, não relacionado ao pin) e a
remoção do parâmetro SSM não utilizado — nenhum `ImageId` nem
replacement de instância. `AtuaNetworkStack`/`AtuaDataStack`: sem
diferenças.

`cdk deploy AtuaComputeStack -c collocateCollector=true` aplicado:
CloudFormation atualizou o `UserData` da instância via
**stop/start in-place** (comportamento padrão do CloudFormation para
essa propriedade em `AWS::EC2::Instance`; `Replacement: Conditional`,
não efetivado — `i-0f32f8f0cd771c01f` manteve o mesmo ID antes e
depois do deploy). Esse stop/start, por si só, **trocou o IP público**
(sem Elastic IP, por design — ver corpo deste ADR), de
`54.233.12.205` para `18.231.123.4`; `make sync-api-domain` foi
rodado para atualizar `api-origin.atyno.com.br`. O stop/start por si
só **não** reexecuta o `UserData` (cloud-init só roda user-data uma
vez por `instance-id`, não a cada boot), por isso foi necessário o
procedimento adicional `cloud-init clean --logs && reboot` (autorizado
explicitamente) para o novo `UserData` rodar de fato.

Validação pós-reboot: `cloud-init status` = `done`; `atua-api` e
`atua-collector` `active`; `/health` (porta 80, não 5000/8080) = HTTP
200; hash SHA-256 de `Authentication__SigningKey` lido de
`/proc/<pid>/environ` do processo `atua-api` **idêntico** ao hash do
`jwtSigningKey` no secret `atua/app-secrets` (confirma injeção correta
do secret, sem expor o valor); `journalctl` sem erros de conexão
(DNS/timeout/refused) — a única exceção de rede vista nos logs foi um
timeout de DNS do MongoDB Atlas durante a janela do próprio boot
(10-15s após o reboot), auto-recuperado, consistente com a rede ainda
subindo; `make collector-trigger-cycle` disparou um ciclo
(`lastImmediateCommand.status: Pending`) com sucesso — o Worker segue
recebendo HTTP 401 no `/claim`, que é o débito técnico PRÉ-EXISTENTE já
documentado (tokens de `ServiceCredential` vazios por padrão,
tratado como erro recuperável, sem crash), não uma regressão desta
mudança; `free -h` final: ~472Mi disponíveis, swap 0 usado, sem sinais
de pressão de memória.

**Sugestão (NÃO aplicada nesta execução — apenas reportada)**: declarar
a regra de SSH `186.236.210.61/32` (IP do operador) explicitamente em
`infra/lib/atua-network-stack.ts` para `sgApi`/`sgCollector`, no mesmo
padrão já usado para `sgRds`, eliminando o drift fora do CDK apontado
na seção anterior deste ADR. Decisão pendente de aprovação humana.

---

### Histórico do diagnóstico anterior (mantido para rastreabilidade — conclusão substituída pela seção acima)

Durante a tentativa de validar um ciclo de coleta real
(`make collector-trigger-cycle`, exigido pelo checklist de
`infra/README.md` §4.1), um diagnóstico preliminar caracterizou o
defeito como "connection string corrompida em runtime por causa
desconhecida, possivelmente no código da aplicação". Uma etapa
intermediária desse diagnóstico também caracterizou incorretamente um
padrão `;******` visto no código-fonte de `infra/lib/atua-compute-stack.ts`
como "defeito pré-existente"; isso foi corrigido no mesmo diagnóstico —
checagem por hash SHA-256 em todo o histórico Git mostrou que a

### Nota de validação ponta a ponta (2026-10-01)

**Data**: 2026-10-01 09:04 (UTC-3)

**Defeito da connection string Postgres corrigido**: confirmado resolvido em 2026-10-01 — ver nota de validação. Ciclo de coleta de teste executado com sucesso.

**CommandId de evidência**: `01a0f73b-0217-7588-b60a-22b891b8346c`

**Etapas validadas**:
- `/claim` (autenticação contra servidor de identidade): HTTP 200
- Elegibilidade retornada: `Eligible=True`
- Sessão iService reaproveitada em modo somente leitura
- Coleta de dados de comando: `Designado=2`, `Em Processamento=1`, `Pendente=157`, `Concluído=34`, `Cancelado=47`
- Endpoint `/complete` (outcome=Succeeded): ciclo finalizado sem erros

**Erros **não** detectados**:
- Sem erro de Postgres (DNS, timeout, refused ou malformed connection string)
- Sem HTTP 401 em etapas críticas (débito técnico `/claim` superado; confirmado pelo product owner em 2026-10-01 com base nas logs do ciclo de teste (CommandId 01a0f73b-0217-7588-b60a-22b891b8346c), nas quais claim retornou 200 com autenticação funcionando; ServiceCredential vazio por design aceitável, ADR-021)
- Sem OOM-kill nas logs analisadas (trechos parciais de `journalctl`)

**Limitação identificada (ponto em aberto)**:
- Medição de memória/swap via `free -m` não foi realizada nesta execução de 2026-10-01; permanece como validação pendente para próxima janela de testes.
*connection string* sempre esteve correta no código-fonte, e o texto
visto era resultado de uma camada de redação de segredos nas
ferramentas de terminal, não um problema real no arquivo. A causa raiz
completa (estado residual de `systemd` + ordem de boot vs. `cloud-init`,
não um defeito de código da aplicação) foi confirmada e corrigida
conforme a seção acima.
